import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ChangePasswordRequest, CurrentUser, LoginRequest, Permission } from './api.types';

/**
 * The signed-in user.
 *
 * There is no token to keep. Authentication is an HttpOnly, SameSite=Strict cookie that
 * the browser attaches by itself, and which script cannot read by design. Nothing here
 * touches localStorage: the session outlives a page refresh because the cookie does, and
 * `restore()` is how the app discovers that on start.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly currentUser = signal<CurrentUser | null>(null);

  /** Null until restore() has run, so the shell can hold off rendering. */
  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  readonly fullName = computed(() => this.currentUser()?.fullName ?? '');
  readonly roles = computed(() => this.currentUser()?.roles ?? []);

  private readonly restored = signal(false);
  readonly isReady = this.restored.asReadonly();

  async login(request: LoginRequest): Promise<CurrentUser> {
    const user = await firstValueFrom(
      this.http.post<CurrentUser>('/api/v1/auth/login', request),
    );

    this.currentUser.set(user);
    return user;
  }

  /**
   * Called once on application start, not only after a login. A returning user still
   * holds a valid cookie, and bouncing them to the login page would be wrong.
   */
  async restore(): Promise<void> {
    try {
      const user = await firstValueFrom(this.http.get<CurrentUser>('/api/v1/auth/me'));
      this.currentUser.set(user);
    } catch {
      // A 401 here is the ordinary "not signed in" case, not a failure to report.
      this.currentUser.set(null);
    } finally {
      this.restored.set(true);
    }
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/logout', {}));
    } catch {
      // Whether the server heard us or not, this session is over on this machine.
    }

    this.clearAndRedirect();
  }

  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await firstValueFrom(this.http.post('/api/v1/auth/change-password', request));

    // The server ends every session on a password change, including this one.
    this.clearAndRedirect('password-changed');
  }

  /** Called by the interceptor when any request comes back 401. */
  clearAndRedirect(reason: 'session-expired' | 'password-changed' | null = null): void {
    const wasSignedIn = this.currentUser() !== null;
    this.currentUser.set(null);

    const returnTo = this.router.url.startsWith('/login') ? null : this.router.url;

    void this.router.navigate(['/login'], {
      queryParams: {
        reason: reason ?? (wasSignedIn ? 'session-expired' : null),
        returnTo,
      },
    });
  }

  /**
   * Hiding a control the user cannot use is a courtesy, not a security boundary - every
   * endpoint is enforced server-side and again in the service layer.
   */
  has(permission: Permission): boolean {
    return this.currentUser()?.permissions.includes(permission) ?? false;
  }

  hasAny(...permissions: Permission[]): boolean {
    return permissions.some((p) => this.has(p));
  }

  /**
   * PR-07: a clerk may cancel a document dated today; anything older is the owner's.
   *
   * This duplicates a server rule, because `permissions` is per-user and cannot express
   * a per-record one. If the two ever disagree the server wins and returns 403
   * CANCELLATION_WINDOW_EXPIRED, which the UI shows rather than treating as a crash.
   */
  canCancel(documentDate: string, today: string): boolean {
    if (!this.has('CanRecordTransactions')) return false;
    if (documentDate === today) return true;

    return this.has('CanCancelHistorical');
  }
}

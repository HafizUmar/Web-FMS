import { Component, computed, inject, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AppUserResponse } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { parseUtc } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog';
import { UserFormComponent } from './user-form';
import { ResetPasswordDialogComponent } from './reset-password-dialog';

/**
 * SE-13/SE-14. Users are deactivated, never deleted: every document they entered still
 * names them, and "who recorded this" has to stay answerable years later.
 */
@Component({
  selector: 'app-users-page',
  imports: [
    MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Users</h1>
        <p class="page__sub">Who can sign in, and what they are allowed to do.</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()">
        <mat-icon>person_add</mat-icon> New user
      </button>
    </header>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="users()">
        <ng-container matColumnDef="fullName">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let u">
            {{ u.fullName }}
            @if (isSelf(u)) { <span class="you">you</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="userName">
          <th mat-header-cell *matHeaderCellDef>Login</th>
          <td mat-cell *matCellDef="let u"><code>{{ u.userName }}</code></td>
        </ng-container>

        <ng-container matColumnDef="roles">
          <th mat-header-cell *matHeaderCellDef>Role</th>
          <td mat-cell *matCellDef="let u">{{ u.roles.join(', ') || '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="isActive">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let u">
            <!-- Icon and word together: a status that is only a colour is no status. -->
            <span class="status" [class.status--off]="!u.isActive">
              <mat-icon class="inline">{{ u.isActive ? 'check_circle' : 'block' }}</mat-icon>
              {{ u.isActive ? 'Active' : 'Deactivated' }}
            </span>
          </td>
        </ng-container>

        <ng-container matColumnDef="lastLoginAt">
          <th mat-header-cell *matHeaderCellDef>Last signed in</th>
          <td mat-cell *matCellDef="let u">{{ when(u.lastLoginAt) }}</td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let u" class="actions">
            <button matIconButton (click)="edit(u)" matTooltip="Edit name and role">
              <mat-icon>edit</mat-icon>
            </button>
            <button matIconButton (click)="resetPassword(u)" matTooltip="Set a new password">
              <mat-icon>key</mat-icon>
            </button>
            @if (u.isActive) {
              @if (isSelf(u)) {
                <!--
                  Disabled buttons take no pointer events, so the reason has to be
                  reachable some other way: the icon changes and the cell says why.
                -->
                <span class="blocked" role="note">
                  <mat-icon class="inline">lock</mat-icon> not your own account
                </span>
              } @else {
                <button matIconButton (click)="deactivate(u)" matTooltip="Deactivate">
                  <mat-icon>person_off</mat-icon>
                </button>
              }
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="!row.isActive"></tr>
      </table>

      @if (!loading() && users().length === 0) {
        <p class="empty">No users yet.</p>
      }
    </div>
  `,
  styles: `
    table { width: 100%; }
    .row--off { opacity: .6; }
    .you { margin-left: .4rem; font-size: .7rem; text-transform: uppercase; letter-spacing: .04em;
           background: var(--line); border-radius: 4px; padding: .1rem .35rem; opacity: .8; }
    .actions { text-align: right; white-space: nowrap; }
    .blocked { display: inline-flex; align-items: center; gap: .25rem; font-size: .75rem;
               opacity: .6; padding-left: .5rem; }
  `,
})
export class UsersPageComponent {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = ['fullName', 'userName', 'roles', 'isActive', 'lastLoginAt', 'actions'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly users = signal<AppUserResponse[]>([]);

  private readonly currentUserId = computed(() => this.auth.user()?.userId ?? '');

  constructor() {
    void this.load();
  }

  isSelf(user: AppUserResponse): boolean {
    return user.id === this.currentUserId();
  }

  when(value?: string): string {
    const parsed = parseUtc(value);
    return parsed ? parsed.toLocaleString() : 'Never';
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.users.set(await this.admin.users());
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    await this.openForm(null, 'User created.');
  }

  async edit(user: AppUserResponse): Promise<void> {
    await this.openForm(user, 'User updated.');
  }

  private async openForm(existing: AppUserResponse | null, done: string): Promise<void> {
    const ref = this.dialog.open(UserFormComponent, { data: { existing } });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(done, 'Dismiss', { duration: 4000 });
      await this.load();
    }
  }

  async resetPassword(user: AppUserResponse): Promise<void> {
    const ref = this.dialog.open(ResetPasswordDialogComponent, { data: { user } });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(`Password reset for ${user.userName}.`, 'Dismiss', { duration: 6000 });
    }
  }

  async deactivate(user: AppUserResponse): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Deactivate this user?',
        // No promise of an undo: the API has no reactivate endpoint, and telling somebody
        // a destructive action is reversible when it is not is worse than not warning at all.
        message:
          `${user.fullName} will be signed out and will not be able to sign in again. ` +
          'Everything they entered stays exactly where it is, still recorded against ' +
          'their name. Phase 1 has no way to switch an account back on.',
        confirmLabel: 'Deactivate',
        destructive: true,
      },
    });

    if ((await ref.afterClosed().toPromise()) !== true) return;

    try {
      await this.admin.deactivateUser(user.id);
      this.snackBar.open(`${user.fullName} deactivated.`, 'Dismiss', { duration: 4000 });
      await this.load();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }
}

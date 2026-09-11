import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <div class="login">
      <mat-card class="login__card">
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }

        <mat-card-header>
          <mat-card-title>Crockery Factory</mat-card-title>
          <mat-card-subtitle>Sign in to continue</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          @if (notice()) {
            <p class="login__notice" role="status">{{ notice() }}</p>
          }

          <app-error-banner [problem]="error()" />

          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Username</mat-label>
              <input matInput formControlName="userName" autocomplete="username" cdkFocusInitial />
              @if (form.controls.userName.touched && form.controls.userName.invalid) {
                <mat-error>Enter your username</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password" autocomplete="current-password" />
              @if (form.controls.password.touched && form.controls.password.invalid) {
                <mat-error>Enter your password</mat-error>
              }
            </mat-form-field>

            <button matButton="filled" color="primary" type="submit" [disabled]="busy()">
              {{ busy() ? 'Signing in…' : 'Sign in' }}
            </button>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: `
    .login { display: grid; place-items: center; min-height: 100vh; background: #f4f5f7; padding: 1rem; }
    .login__card { width: min(400px, 100%); padding-bottom: 1rem; }
    .login__notice {
      background: #e8f4fd; border: 1px solid #b6dcf7; color: #0b4a6f;
      border-radius: 8px; padding: .75rem 1rem; margin-bottom: 1rem;
    }
    form { display: flex; flex-direction: column; gap: .25rem; margin-top: .5rem; }
    button { margin-top: .5rem; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);

  readonly form = this.fb.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  /** Why the user was sent here, when they did not choose to come. */
  readonly notice = signal(
    {
      'session-expired': 'Your session has ended. Please sign in again.',
      'password-changed': 'Your password was changed. Please sign in again.',
    }[this.route.snapshot.queryParamMap.get('reason') ?? ''] ?? '',
  );

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.form.getRawValue());

      // Dropdown data every screen needs. Loaded before navigating so the first page
      // does not render with empty selects.
      await this.lookups.load();

      const returnTo = this.route.snapshot.queryParamMap.get('returnTo');
      await this.router.navigateByUrl(returnTo && !returnTo.startsWith('/login') ? returnTo : '/');
    } catch (problem) {
      // 401 for a wrong password and 401 for an unknown username are identical by
      // design - showing detail verbatim keeps it that way.
      this.error.set(problem as ProblemDetails);
      this.notice.set('');
    } finally {
      this.busy.set(false);
    }
  }
}

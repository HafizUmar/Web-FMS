import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

@Component({
  selector: 'app-change-password',
  imports: [
    ReactiveFormsModule, MatCardModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, ErrorBannerComponent,
  ],
  template: `
    <mat-card class="page">
      <mat-card-header><mat-card-title>Change password</mat-card-title></mat-card-header>
      <mat-card-content>
        <app-error-banner [problem]="error()" />

        <p class="hint">
          At least 10 characters, including a digit and a lowercase letter.
          You will be signed out and will need to sign in again.
        </p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field appearance="outline">
            <mat-label>Current password</mat-label>
            <input matInput type="password" formControlName="currentPassword" autocomplete="current-password" />
            @if (serverError('currentPassword'); as message) { <mat-error>{{ message }}</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>New password</mat-label>
            <input matInput type="password" formControlName="newPassword" autocomplete="new-password" />
            @if (serverError('newPassword'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.newPassword.touched && form.controls.newPassword.invalid) {
              <mat-error>At least 10 characters</mat-error>
            }
          </mat-form-field>

          <button matButton="filled" color="primary" type="submit" [disabled]="busy()">
            Change password
          </button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .page { max-width: 460px; }
    .hint { opacity: .75; margin-bottom: 1rem; }
    form { display: flex; flex-direction: column; gap: .25rem; }
    button { margin-top: .5rem; align-self: flex-start; }
  `,
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly form = this.fb.nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(10)]],
  });

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    try {
      await this.auth.changePassword(this.form.getRawValue());
    } catch (raw) {
      const problem = raw as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));

      // Field errors are shown on the fields; only show the banner when there are none.
      this.error.set(problem.errors?.length ? null : problem);
    } finally {
      this.busy.set(false);
    }
  }
}

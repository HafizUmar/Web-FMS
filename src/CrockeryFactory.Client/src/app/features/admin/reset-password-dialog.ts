import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AppUserResponse } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/**
 * An administrator setting a password for somebody who has forgotten theirs. No current
 * password is asked for - the administrator does not have it, which is the whole reason
 * this exists. The old password is never shown, and the new one is never audited.
 */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>Reset password</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <p class="who">
        A new password for <strong>{{ data.user.fullName }}</strong>
        (<code>{{ data.user.userName }}</code>). Tell it to them in person - it is not
        sent anywhere, and this screen will not show it again.
      </p>

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>New password</mat-label>
          <input matInput [type]="reveal() ? 'text' : 'password'" formControlName="newPassword"
                 autocomplete="new-password" />
          <button matIconButton matIconSuffix type="button" (click)="reveal.set(!reveal())"
                  [attr.aria-label]="reveal() ? 'Hide password' : 'Show password'">
            <mat-icon>{{ reveal() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
          <mat-hint>At least 8 characters</mat-hint>
          @if (serverError('password'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.newPassword.touched && form.controls.newPassword.invalid) {
            <mat-error>At least 8 characters</mat-error>
          }
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">Cancel</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Resetting…' : 'Reset password' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .who { margin: 0 0 1rem; max-width: 46ch; }
    .form { display: flex; flex-direction: column; min-width: min(420px, 80vw); }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
  `,
})
export class ResetPasswordDialogComponent {
  readonly dialogRef = inject(MatDialogRef<ResetPasswordDialogComponent, boolean>);
  readonly data = inject<{ user: AppUserResponse }>(MAT_DIALOG_DATA);

  private readonly admin = inject(AdminService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly reveal = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
  });

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    try {
      await this.admin.resetPassword(this.data.user.id, this.form.getRawValue().newPassword);
      this.dialogRef.close(true);
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));
      this.error.set(problem.errors?.length ? null : problem);
    } finally {
      this.busy.set(false);
    }
  }
}

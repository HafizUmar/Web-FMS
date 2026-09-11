import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AppUserResponse } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface UserFormData {
  /** Null creates. Editing cannot change the login name - it is what the audit records. */
  existing: AppUserResponse | null;
}

const ROLES = [
  { value: 'Owner', label: 'Owner', note: 'Sees everything, including prices and reports.' },
  { value: 'Clerk', label: 'Clerk', note: 'Enters production, dispatches and payments.' },
  { value: 'Administrator', label: 'Administrator', note: 'Manages users and settings.' },
];

@Component({
  selector: 'app-user-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit() ? 'Edit user' : 'New user' }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Login name</mat-label>
          <input matInput formControlName="userName" maxlength="64" autocomplete="off" />
          @if (isEdit()) {
            <mat-hint>The login name is fixed - every audit entry is written against it</mat-hint>
          } @else {
            <mat-hint>What they type to sign in</mat-hint>
          }
          @if (serverError('userName'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.userName.touched && form.controls.userName.invalid) {
            <mat-error>Required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Full name</mat-label>
          <input matInput formControlName="fullName" maxlength="160" />
          @if (serverError('fullName'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.fullName.touched && form.controls.fullName.invalid) {
            <mat-error>Required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Role</mat-label>
          <mat-select formControlName="role">
            @for (role of roles; track role.value) {
              <mat-option [value]="role.value">{{ role.label }}</mat-option>
            }
          </mat-select>
          <mat-hint>{{ roleNote() }}</mat-hint>
          @if (serverError('role'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        @if (!isEdit()) {
          <mat-form-field appearance="outline">
            <mat-label>Password</mat-label>
            <input matInput type="password" formControlName="password" autocomplete="new-password" />
            <mat-hint>At least 8 characters. They can change it once signed in.</mat-hint>
            @if (serverError('password'); as message) {
              <mat-error>{{ message }}</mat-error>
            } @else if (form.controls.password.touched && form.controls.password.invalid) {
              <mat-error>At least 8 characters</mat-error>
            }
          </mat-form-field>
        }

        @if (isEdit() && rolesChanged()) {
          <p class="warning">
            <mat-icon>logout</mat-icon>
            Changing the role signs this person out of any session they have open, so the
            new permissions take effect immediately rather than tomorrow.
          </p>
        }
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">Cancel</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Saving…' : 'Save' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(460px, 80vw); }
    .warning { display: flex; gap: .5rem; align-items: flex-start; background: var(--warn-bg);
               border: 1px solid var(--warn-line); color: var(--warn-ink); padding: .6rem .75rem;
               border-radius: 8px; margin: .5rem 0 0; font-size: .85rem; }
  `,
})
export class UserFormComponent {
  readonly dialogRef = inject(MatDialogRef<UserFormComponent, boolean>);
  readonly data = inject<UserFormData>(MAT_DIALOG_DATA);

  private readonly admin = inject(AdminService);
  private readonly fb = inject(FormBuilder);

  readonly roles = ROLES;
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly isEdit = computed(() => this.data.existing !== null);

  readonly form = this.fb.nonNullable.group({
    userName: [this.data.existing?.userName ?? '', Validators.required],
    fullName: [this.data.existing?.fullName ?? '', Validators.required],
    role: [this.data.existing?.roles[0] ?? 'Clerk', Validators.required],
    password: ['', this.data.existing ? [] : [Validators.required, Validators.minLength(8)]],
  });

  private readonly roleValue = signal(this.form.controls.role.value);

  readonly roleNote = computed(
    () => ROLES.find((r) => r.value === this.roleValue())?.note ?? '',
  );

  readonly rolesChanged = computed(
    () => this.roleValue() !== (this.data.existing?.roles[0] ?? ''),
  );

  constructor() {
    if (this.data.existing) this.form.controls.userName.disable();
    this.form.controls.role.valueChanges.subscribe((value) => this.roleValue.set(value));
  }

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

    const raw = this.form.getRawValue();

    try {
      if (this.data.existing) {
        await this.admin.updateUser(this.data.existing.id, {
          fullName: raw.fullName.trim(),
          role: raw.role,
        });
      } else {
        await this.admin.createUser({
          userName: raw.userName.trim(),
          fullName: raw.fullName.trim(),
          password: raw.password,
          role: raw.role,
        });
      }

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

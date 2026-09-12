import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Employee } from '../../core/api.types';
import { StaffService } from '../../core/staff.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface EmployeeFormData {
  existing: Employee | null;
}

@Component({
  selector: 'app-employee-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatDatepickerModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit() ? t('emp.edit') : t('emp.new') }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.code') }}</mat-label>
            <input matInput formControlName="code" (blur)="upperCase()" maxlength="24" />
            @if (isEdit()) {
              <mat-hint>{{ t('emp.code_locked') }}</mat-hint>
            }
            @if (serverError('code'); as m) {
              <mat-error>{{ m }}</mat-error>
            } @else if (form.controls.code.touched && form.controls.code.invalid) {
              <mat-error>{{ t('common.required') }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.name') }}</mat-label>
            <input matInput formControlName="name" maxlength="160" />
            @if (serverError('name'); as m) {
              <mat-error>{{ m }}</mat-error>
            } @else if (form.controls.name.touched && form.controls.name.invalid) {
              <mat-error>{{ t('common.required') }}</mat-error>
            }
          </mat-form-field>
        </div>

        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.father_name') }}</mat-label>
            <input matInput formControlName="fatherName" maxlength="160" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.cnic') }}</mat-label>
            <input matInput formControlName="cnic" maxlength="20" class="ltr" />
          </mat-form-field>
        </div>

        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.phone') }}</mat-label>
            <input matInput formControlName="phone" maxlength="32" class="ltr" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ t('emp.designation') }}</mat-label>
            <input matInput formControlName="designation" maxlength="80" />
          </mat-form-field>
        </div>

        @if (!isEdit()) {
          <div class="row">
            <mat-form-field appearance="outline">
              <mat-label>{{ t('emp.joined') }}</mat-label>
              <input matInput [matDatepicker]="picker" formControlName="joinedOn" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="picker" />
              <mat-datepicker #picker />
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>{{ t('emp.daily_rate') }}</mat-label>
              <input matInput type="number" formControlName="dailyRate" min="1" step="10" />
              <mat-hint>{{ t('emp.rate_from_joining') }}</mat-hint>
              @if (serverError('dailyRate'); as m) {
                <mat-error>{{ m }}</mat-error>
              } @else if (form.controls.dailyRate.touched && form.controls.dailyRate.invalid) {
                <mat-error>{{ t('emp.rate_positive') }}</mat-error>
              }
            </mat-form-field>
          </div>
        }

        <mat-form-field appearance="outline">
          <mat-label>{{ t('common.notes') }}</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">{{ t('common.cancel') }}</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? t('common.saving') : t('common.save') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(560px, 82vw); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: .75rem; }
    @media (max-width: 620px) { .row { grid-template-columns: 1fr; } }
  `,
})
export class EmployeeFormComponent {
  readonly dialogRef = inject(MatDialogRef<EmployeeFormComponent, boolean>);
  readonly data = inject<EmployeeFormData>(MAT_DIALOG_DATA);

  private readonly staff = inject(StaffService);
  private readonly fb = inject(FormBuilder);
  protected readonly t = inject(I18nService).t;

  readonly today = new Date();
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly isEdit = computed(() => this.data.existing !== null);

  readonly form = this.fb.nonNullable.group({
    code: [this.data.existing?.code ?? '', Validators.required],
    name: [this.data.existing?.name ?? '', Validators.required],
    fatherName: [this.data.existing?.fatherName ?? ''],
    cnic: [this.data.existing?.cnic ?? ''],
    phone: [this.data.existing?.phone ?? ''],
    designation: [this.data.existing?.designation ?? ''],
    joinedOn: [new Date()],
    dailyRate: [0, [Validators.required, Validators.min(1)]],
    notes: [this.data.existing?.notes ?? ''],
  });

  constructor() {
    if (this.data.existing) {
      // The code is snapshotted onto every payslip already issued; changing it would
      // leave those slips naming a code that no longer exists. The wage is changed
      // through its own effective-dated action, not by editing a field here.
      this.form.controls.code.disable();
      this.form.controls.joinedOn.disable();
      this.form.controls.dailyRate.disable();
    }
  }

  upperCase(): void {
    const control = this.form.controls.code;
    const upper = (control.value ?? '').trim().toUpperCase();

    if (upper !== control.value) control.setValue(upper);
  }

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async save(): Promise<void> {
    this.upperCase();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    const raw = this.form.getRawValue();
    const text = (value: string) => (value?.trim() ? value.trim() : null);

    try {
      if (this.data.existing) {
        await this.staff.updateEmployee(this.data.existing.id, {
          name: raw.name.trim(),
          fatherName: text(raw.fatherName),
          cnic: text(raw.cnic),
          phone: text(raw.phone),
          designation: text(raw.designation),
          notes: text(raw.notes),
        });
      } else {
        await this.staff.createEmployee({
          code: raw.code,
          name: raw.name.trim(),
          fatherName: text(raw.fatherName),
          cnic: text(raw.cnic),
          phone: text(raw.phone),
          designation: text(raw.designation),
          joinedOn: toIsoDate(raw.joinedOn) ?? '',
          dailyRate: Number(raw.dailyRate),
          notes: text(raw.notes),
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

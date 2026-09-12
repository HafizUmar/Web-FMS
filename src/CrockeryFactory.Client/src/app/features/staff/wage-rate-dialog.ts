import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Employee, WageRate } from '../../core/api.types';
import { StaffService } from '../../core/staff.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { money, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/**
 * Changing what a day's work is worth.
 *
 * Effective-dated rather than an edit, so a rise agreed today never reprices the payroll
 * already run. The history is shown alongside because the previous rate is the question
 * anyone asks before setting the next one.
 */
@Component({
  selector: 'app-wage-rate-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatDatepickerModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ t('emp.new_rate') }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <p class="who">
        <strong>{{ data.employee.name }}</strong>
        <code class="ltr">{{ data.employee.code }}</code>
      </p>

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>{{ t('emp.daily_rate') }}</mat-label>
          <input matInput type="number" formControlName="dailyRate" min="1" step="10" />
          @if (serverError('dailyRate'); as m) {
            <mat-error>{{ m }}</mat-error>
          } @else if (form.controls.dailyRate.touched && form.controls.dailyRate.invalid) {
            <mat-error>{{ t('emp.rate_positive') }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ t('emp.effective_from') }}</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="effectiveFrom" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
        </mat-form-field>
      </form>

      <p class="note">
        <mat-icon class="inline">info</mat-icon> {{ t('emp.rate_note') }}
      </p>

      @if (history().length > 0) {
        <h3 class="history__title">{{ t('emp.rate_history') }}</h3>
        <table class="history">
          @for (rate of history(); track rate.id) {
            <tr>
              <td class="ltr">{{ rate.effectiveFrom }}</td>
              <td class="num figure">{{ money(rate.dailyRate) }}</td>
            </tr>
          }
        </table>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">{{ t('common.cancel') }}</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? t('common.saving') : t('common.save') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .who { display: flex; gap: .6rem; align-items: baseline; margin: 0 0 1rem; }
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(420px, 82vw); }
    .note {
      display: flex; gap: .5rem; align-items: flex-start;
      background: var(--info-bg); border: 1px solid var(--info-line); color: var(--info-ink);
      border-radius: var(--radius-sm); padding: .6rem .8rem; margin: .25rem 0 0; font-size: .85rem;
    }
    .history__title { margin: 1.25rem 0 .35rem; font-size: .95rem; }
    .history { width: 100%; border-collapse: collapse; font-size: .875rem; }
    .history td { padding: .3rem 0; border-bottom: 1px solid var(--line); color: var(--ink-2); }
    .num { text-align: end; }
  `,
})
export class WageRateDialogComponent {
  readonly dialogRef = inject(MatDialogRef<WageRateDialogComponent, boolean>);
  readonly data = inject<{ employee: Employee }>(MAT_DIALOG_DATA);

  private readonly staff = inject(StaffService);
  private readonly fb = inject(FormBuilder);
  protected readonly t = inject(I18nService).t;

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly history = signal<WageRate[]>([]);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly form = this.fb.nonNullable.group({
    dailyRate: [this.data.employee.currentDailyRate ?? 0, [Validators.required, Validators.min(1)]],
    effectiveFrom: [new Date()],
  });

  constructor() {
    void this.loadHistory();
  }

  money(value: number): string { return money(value); }

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  private async loadHistory(): Promise<void> {
    try {
      this.history.set(await this.staff.wageRates(this.data.employee.id));
    } catch {
      // The history is context, not the job. Failing to load it must not stop somebody
      // setting a rate.
      this.history.set([]);
    }
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
      await this.staff.setWageRate(
        this.data.employee.id, Number(raw.dailyRate), toIsoDate(raw.effectiveFrom) ?? '');

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

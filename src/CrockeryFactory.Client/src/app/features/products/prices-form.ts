import { Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { Product, QualityGrade } from '../../core/api.types';
import { LookupsService } from '../../core/lookups.service';
import { ProductsService } from '../../core/products.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { toIsoDate, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

@Component({
  selector: 'app-prices-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatButtonModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>Prices — {{ product.code }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <p class="hint">
        The old rate is kept. Dispatches already raised keep the rate they were priced at
        and are not affected.
      </p>

      <form [formGroup]="form" class="form">
        <div formArrayName="prices">
          @for (row of prices.controls; track $index; let i = $index) {
            <div class="row" [formGroupName]="i">
              <mat-form-field appearance="outline">
                <mat-label>Grade</mat-label>
                <mat-select formControlName="grade">
                  @for (grade of grades(); track grade) {
                    <mat-option [value]="grade">{{ grade }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>New rate (PKR)</mat-label>
                <input matInput type="number" formControlName="unitRate" min="0.01" step="0.01" />
                @if (serverError('prices[' + i + '].unitRate'); as message) {
                  <mat-error>{{ message }}</mat-error>
                }
              </mat-form-field>
            </div>
          }
        </div>

        <mat-form-field appearance="outline">
          <mat-label>Effective from</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="effectiveFrom" [min]="earliest" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
          <mat-hint>Cannot start before the current rate's effective date</mat-hint>
          @if (serverError('effectiveFrom'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Reason</mat-label>
          <input matInput formControlName="reason" maxlength="300" />
          <mat-hint>Recorded in the audit trail — read when a bill is disputed</mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">Cancel</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Saving…' : 'Save prices' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(520px, 80vw); }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: .5rem; }
    .hint { opacity: .75; font-size: .85rem; margin: 0 0 .75rem; }
  `,
})
export class PricesFormComponent {
  readonly dialogRef = inject(MatDialogRef<PricesFormComponent, boolean>);
  readonly product = inject<Product>(MAT_DIALOG_DATA);

  private readonly products = inject(ProductsService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly grades = computed<QualityGrade[]>(() => this.lookups.enabledGrades());

  /** Backdating a price cannot change bills already printed, so the server refuses it. */
  readonly earliest = new Date();

  readonly form = this.fb.nonNullable.group({
    prices: this.fb.array(
      (this.product.prices.length > 0
        ? this.product.prices
        : this.lookups.enabledGrades().map((grade) => ({ grade, unitRate: 0 }))
      ).map((p) =>
        this.fb.nonNullable.group({
          grade: [p.grade as QualityGrade, Validators.required],
          unitRate: [p.unitRate, [Validators.required, Validators.min(0.01)]],
        }),
      ),
    ),
    effectiveFrom: [new Date() as Date | string, Validators.required],
    reason: [''],
  });

  get prices(): FormArray {
    return this.form.controls.prices as FormArray;
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
      await this.products.updatePrices(this.product.id, {
        prices: raw.prices as { grade: QualityGrade; unitRate: number }[],
        effectiveFrom: toIsoDate(raw.effectiveFrom) ?? todayIso(),
        reason: raw.reason?.trim() || null,
      });

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

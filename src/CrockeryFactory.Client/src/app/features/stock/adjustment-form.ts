import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { QualityGrade } from '../../core/api.types';
import { LookupsService } from '../../core/lookups.service';
import { StockService } from '../../core/stock.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { quantity, toIsoDate, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface AdjustmentFormData {
  productId?: string;
  grade?: QualityGrade;
}

const NOTES_REQUIRED_ABOVE = 500;

@Component({
  selector: 'app-adjustment-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>Adjust stock</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Product</mat-label>
          <mat-select formControlName="productId">
            @for (product of products(); track product.id) {
              <mat-option [value]="product.id">{{ product.code }} — {{ product.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Grade</mat-label>
          <mat-select formControlName="grade">
            @for (grade of grades(); track grade) {
              <mat-option [value]="grade">{{ grade }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Change in units</mat-label>
          <input matInput type="number" formControlName="quantityChange" />
          <mat-hint>Negative reduces stock, positive adds to it</mat-hint>
          @if (serverError('quantityChange'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <div class="balance" [class.balance--bad]="resulting() < 0">
          <span>On hand now <strong>{{ onHandLabel() }}</strong></span>
          <mat-icon>arrow_forward</mat-icon>
          <span>After this <strong>{{ resultingLabel() }}</strong></span>
          @if (resulting() < 0) {
            <span class="balance__note">Stock cannot go below zero</span>
          }
        </div>

        <mat-form-field appearance="outline">
          <mat-label>Reason</mat-label>
          <mat-select formControlName="reasonCodeId">
            @for (reason of reasons(); track reason.id) {
              <mat-option [value]="reason.id">{{ reason.description }}</mat-option>
            }
          </mat-select>
          @if (serverError('reasonCodeId'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Date</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="adjustedOn" [max]="today" [min]="earliest()" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
          <mat-hint>Within {{ backdateDays() }} days</mat-hint>
          @if (serverError('adjustedOn'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Notes{{ notesRequired() ? '' : ' (optional)' }}</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
          <mat-hint>
            @if (notesRequired()) {
              Required for a change of more than {{ threshold }} units
            } @else {
              This is what someone reads six months from now
            }
          </mat-hint>
          @if (serverError('notes'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">Cancel</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Saving…' : 'Save adjustment' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(520px, 85vw); }
    .balance {
      display: flex; gap: .75rem; align-items: center;
      background: #f1f3f4; border-radius: 8px; padding: .6rem .9rem; margin: .25rem 0 .75rem;
    }
    .balance--bad { background: #fce8e6; color: #c5221f; }
    .balance__note { margin-left: auto; font-size: .85rem; }
    mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: .6; }
  `,
})
export class AdjustmentFormComponent {
  readonly dialogRef = inject(MatDialogRef<AdjustmentFormComponent, boolean>);
  readonly data = inject<AdjustmentFormData>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  private readonly stock = inject(StockService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly threshold = NOTES_REQUIRED_ABOVE;
  readonly today = new Date();
  readonly products = computed(() => this.lookups.activeProducts().filter((p) => p.isActive));
  readonly grades = computed<QualityGrade[]>(() => this.lookups.enabledGrades());
  readonly reasons = computed(() => this.lookups.reasonsFor('StockAdjustment'));
  readonly backdateDays = computed(() => this.lookups.settingNumber('Backdate.Days.Adjustment', 30));

  readonly earliest = computed(() => {
    const d = new Date();
    d.setDate(d.getDate() - this.backdateDays());
    return d;
  });

  private readonly idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    productId: [this.data.productId ?? '', Validators.required],
    grade: [this.data.grade ?? ('First' as QualityGrade), Validators.required],
    quantityChange: [0, Validators.required],
    reasonCodeId: ['', Validators.required],
    adjustedOn: [new Date() as Date | string, Validators.required],
    notes: [''],
  });

  private readonly formValue = signal(this.form.getRawValue());

  constructor() {
    this.form.valueChanges.subscribe(() => this.formValue.set(this.form.getRawValue()));
  }

  /** Read from the cached catalogue so the clerk sees the effect before saving. */
  readonly onHand = computed(() => {
    const v = this.formValue();
    return v.productId ? this.lookups.stockFor(v.productId, v.grade) : 0;
  });

  readonly resulting = computed(() => this.onHand() + Number(this.formValue().quantityChange || 0));

  readonly onHandLabel = computed(() => quantity(this.onHand()));
  readonly resultingLabel = computed(() => quantity(this.resulting()));

  readonly notesRequired = computed(
    () => Math.abs(Number(this.formValue().quantityChange || 0)) > NOTES_REQUIRED_ABOVE,
  );

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async save(): Promise<void> {
    const v = this.form.getRawValue();

    if (Number(v.quantityChange) === 0) {
      this.error.set({
        title: 'Nothing to adjust', status: 400, code: 'QUANTITY_ZERO',
        detail: 'An adjustment of zero units records nothing.',
      });
      return;
    }

    if (this.notesRequired() && !v.notes.trim()) {
      this.fieldErrors.set(new Map([['notes', `Required for a change of more than ${NOTES_REQUIRED_ABOVE} units`]]));
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    try {
      await this.stock.adjust(
        {
          productId: v.productId,
          grade: v.grade,
          quantityChange: Number(v.quantityChange),
          reasonCodeId: v.reasonCodeId,
          adjustedOn: toIsoDate(v.adjustedOn) ?? todayIso(),
          notes: v.notes.trim() || null,
        },
        this.idempotencyKey,
      );

      await this.lookups.reloadProducts();
      this.dialogRef.close(true);
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));
      // STOCK_INSUFFICIENT names the product and grade in its detail.
      this.error.set(problem.errors?.length && problem.code !== 'STOCK_INSUFFICIENT' ? null : problem);
    } finally {
      this.busy.set(false);
    }
  }
}

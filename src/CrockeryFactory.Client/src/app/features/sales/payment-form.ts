import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Customer, Payment, PaymentMethod } from '../../core/api.types';
import { LookupsService } from '../../core/lookups.service';
import { SalesService } from '../../core/sales.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { money, toIsoDate, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { WarningListComponent } from '../../shared/components/warning-list';

export interface PaymentFormData { customerId?: string }

const METHODS: { value: PaymentMethod; label: string; needsReference: boolean }[] = [
  { value: 'Cash', label: 'Cash', needsReference: false },
  { value: 'BankTransfer', label: 'Bank transfer', needsReference: true },
  { value: 'Cheque', label: 'Cheque', needsReference: true },
  { value: 'Other', label: 'Other', needsReference: false },
];

@Component({
  selector: 'app-payment-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule,
    ErrorBannerComponent, WarningListComponent,
  ],
  template: `
    <h2 mat-dialog-title>Record a receipt</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      @if (saved(); as p) {
        <div class="saved" role="status">
          <mat-icon>check_circle</mat-icon>
          <div>
            <strong>{{ p.paymentNumber }}</strong> — {{ money(p.amount) }} from {{ p.customerName }}.
            @if (p.customerBalanceAfter < 0) {
              Now <strong>{{ money(-p.customerBalanceAfter) }}</strong> in advance.
            } @else {
              <strong>{{ money(p.customerBalanceAfter) }}</strong> still outstanding.
            }
          </div>
        </div>
        <app-warning-list [warnings]="p.warnings" />
      }

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Customer</mat-label>
          <mat-select formControlName="customerId">
            @for (c of customers(); track c.id) {
              <mat-option [value]="c.id">{{ c.code }} — {{ c.name }}</mat-option>
            }
          </mat-select>
          <mat-hint>Inactive customers can still pay off what they owe</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Amount (PKR)</mat-label>
          <input matInput type="number" formControlName="amount" min="0.01" step="0.01" />
          @if (serverError('amount'); as m) { <mat-error>{{ m }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Method</mat-label>
          <mat-select formControlName="method">
            @for (m of methods; track m.value) { <mat-option [value]="m.value">{{ m.label }}</mat-option> }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ referenceLabel() }}</mat-label>
          <input matInput formControlName="reference" maxlength="64" />
          @if (serverError('reference'); as m) {
            <mat-error>{{ m }}</mat-error>
          } @else if (referenceRequired() && form.controls.reference.touched && !form.controls.reference.value) {
            <mat-error>Required for this payment method</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Date</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="paymentDate" [max]="today" [min]="earliest()" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
          <mat-hint>Within {{ backdateDays() }} days</mat-hint>
          @if (serverError('paymentDate'); as m) { <mat-error>{{ m }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Notes</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(savedAny())">{{ savedAny() ? 'Done' : 'Cancel' }}</button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Saving…' : 'Save receipt' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(520px, 85vw); }
    .saved {
      display: flex; gap: .6rem; align-items: center;
      background: #e6f4ea; border: 1px solid #b7e1c4; color: #137333;
      border-radius: 8px; padding: .7rem 1rem; margin-bottom: 1rem;
    }
  `,
})
export class PaymentFormComponent {
  readonly dialogRef = inject(MatDialogRef<PaymentFormComponent, boolean>);
  readonly data = inject<PaymentFormData>(MAT_DIALOG_DATA, { optional: true }) ?? {};

  private readonly sales = inject(SalesService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  readonly methods = METHODS;
  readonly today = new Date();

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly saved = signal<Payment | null>(null);
  readonly savedAny = signal(false);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly customers = signal<Customer[]>([]);
  readonly backdateDays = computed(() => this.lookups.settingNumber('Backdate.Days.Payment', 30));
  readonly earliest = computed(() => {
    const d = new Date();
    d.setDate(d.getDate() - this.backdateDays());
    return d;
  });

  private idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    customerId: [this.data.customerId ?? '', Validators.required],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    method: ['Cash' as PaymentMethod, Validators.required],
    reference: [''],
    paymentDate: [new Date() as Date | string, Validators.required],
    notes: [''],
  });

  private readonly method = signal<PaymentMethod>('Cash');

  constructor() {
    this.form.controls.method.valueChanges.subscribe((m) => this.method.set(m));
    void this.loadCustomers();
  }

  private async loadCustomers(): Promise<void> {
    // Deliberately includes inactive customers: one who has stopped trading may still be
    // settling a debt, and refusing the receipt would mean the money arrived and the
    // system says it did not.
    const page = await this.sales.listCustomers({ pageSize: 200, includeInactive: true });
    this.customers.set(page.items);
  }

  readonly referenceRequired = computed(
    () => METHODS.find((m) => m.value === this.method())?.needsReference ?? false,
  );

  readonly referenceLabel = computed(() => {
    switch (this.method()) {
      case 'Cheque': return 'Cheque number';
      case 'BankTransfer': return 'Transfer reference';
      default: return 'Reference (optional)';
    }
  });

  money(value: number): string { return money(value); }
  serverError(field: string): string | undefined { return this.fieldErrors().get(field); }

  async save(): Promise<void> {
    if (this.referenceRequired() && !this.form.controls.reference.value.trim()) {
      this.form.controls.reference.markAsTouched();
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    const v = this.form.getRawValue();

    try {
      const payment = await this.sales.createPayment(
        {
          customerId: v.customerId,
          paymentDate: toIsoDate(v.paymentDate) ?? todayIso(),
          amount: Number(v.amount),
          method: v.method,
          reference: v.reference.trim() || null,
          notes: v.notes.trim() || null,
        },
        this.idempotencyKey,
      );

      this.saved.set(payment);
      this.savedAny.set(true);

      this.idempotencyKey = newIdempotencyKey();
      this.form.patchValue({ amount: 0, reference: '', notes: '' });
      this.form.markAsUntouched();
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));
      this.error.set(problem.errors?.length ? null : problem);
      this.saved.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}

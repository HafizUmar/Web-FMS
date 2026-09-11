import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { SalesService, CustomerWithVersion } from '../../core/sales.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface CustomerFormData {
  existing: CustomerWithVersion | null;
  /** True once anything has been posted to the account - the opening balance is fixed. */
  openingBalanceLocked: boolean;
}

@Component({
  selector: 'app-customer-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatDatepickerModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit() ? 'Edit customer' : 'New customer' }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <div class="two">
          <mat-form-field appearance="outline">
            <mat-label>Code</mat-label>
            <input matInput formControlName="code" (blur)="upperCaseCode()" maxlength="24" />
            @if (serverError('code'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Name</mat-label>
            <input matInput formControlName="name" maxlength="160" />
            @if (serverError('name'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>
        </div>

        <div class="two">
          <mat-form-field appearance="outline">
            <mat-label>City</mat-label>
            <input matInput formControlName="city" maxlength="80" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Phone</mat-label>
            <input matInput formControlName="phone" maxlength="24" />
            @if (serverError('phone'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline">
          <mat-label>Address</mat-label>
          <textarea matInput formControlName="address" rows="2" maxlength="300"></textarea>
        </mat-form-field>

        <div class="two">
          <mat-form-field appearance="outline">
            <mat-label>Opening balance (PKR)</mat-label>
            <input matInput type="number" formControlName="openingBalance" step="0.01" />
            <mat-hint>
              @if (data.openingBalanceLocked) {
                Locked — the account already has documents
              } @else {
                Positive means the customer owes the factory
              }
            </mat-hint>
            @if (serverError('openingBalance'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>As at</mat-label>
            <input matInput [matDatepicker]="picker" formControlName="openingBalanceAsOf" />
            <mat-datepicker-toggle matIconSuffix [for]="picker" />
            <mat-datepicker #picker />
          </mat-form-field>
        </div>

        @if (!isEdit()) {
          <p class="warn">
            <mat-icon>lock_clock</mat-icon>
            The opening balance can never be changed once this customer has a dispatch or a
            payment — every historical balance is built on it.
          </p>
        }

        <mat-form-field appearance="outline">
          <mat-label>Notes</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
        </mat-form-field>
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
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(620px, 88vw); }
    .two { display: grid; grid-template-columns: 1fr 1fr; gap: .5rem; }
    .warn {
      display: flex; gap: .5rem; align-items: flex-start; font-size: .85rem;
      background: #fff4e5; border: 1px solid #ffd9a0; color: #663c00;
      border-radius: 8px; padding: .6rem .75rem; margin: 0 0 .75rem;
    }
    .warn mat-icon { font-size: 18px; width: 18px; height: 18px; }
  `,
})
export class CustomerFormComponent {
  readonly dialogRef = inject(MatDialogRef<CustomerFormComponent, boolean>);
  readonly data = inject<CustomerFormData>(MAT_DIALOG_DATA);

  private readonly sales = inject(SalesService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly isEdit = computed(() => this.data.existing !== null);

  private readonly idempotencyKey = newIdempotencyKey();

  private readonly c = this.data.existing?.customer;

  readonly form = this.fb.nonNullable.group({
    code: [this.c?.code ?? '', [Validators.required, Validators.pattern(/^[A-Z0-9-]+$/)]],
    name: [this.c?.name ?? '', Validators.required],
    city: [this.c?.city ?? ''],
    phone: [this.c?.phone ?? ''],
    address: [this.c?.address ?? ''],
    openingBalance: [this.c?.openingBalance ?? 0, Validators.required],
    openingBalanceAsOf: [(this.c?.openingBalanceAsOf ?? null) as Date | string | null],
    notes: [this.c?.notes ?? ''],
  });

  constructor() {
    // Rendered read-only rather than left editable to fail: the server refuses the change
    // and there is nothing the clerk can do about it from here.
    if (this.data.openingBalanceLocked) this.form.controls.openingBalance.disable();
  }

  upperCaseCode(): void {
    const control = this.form.controls.code;
    const upper = (control.value ?? '').trim().toUpperCase();
    if (upper !== control.value) control.setValue(upper);
  }

  serverError(field: string): string | undefined { return this.fieldErrors().get(field); }

  async save(): Promise<void> {
    this.upperCaseCode();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    const v = this.form.getRawValue();

    const request = {
      code: v.code,
      name: v.name.trim(),
      city: v.city?.trim() || null,
      phone: v.phone?.trim() || null,
      address: v.address?.trim() || null,
      openingBalance: Number(v.openingBalance ?? 0),
      openingBalanceAsOf: toIsoDate(v.openingBalanceAsOf),
      notes: v.notes?.trim() || null,
    };

    try {
      if (this.data.existing) {
        await this.sales.updateCustomer(
          this.data.existing.customer.id, request, this.data.existing.etag ?? '',
        );
      } else {
        await this.sales.createCustomer(request, this.idempotencyKey);
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

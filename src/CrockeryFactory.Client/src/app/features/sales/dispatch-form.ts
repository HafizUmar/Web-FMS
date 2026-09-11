import { Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Customer, Dispatch, QualityGrade } from '../../core/api.types';
import { LookupsService } from '../../core/lookups.service';
import { SalesService } from '../../core/sales.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { money, quantity, toIsoDate, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { WarningListComponent } from '../../shared/components/warning-list';

const MAX_LINES = 50;

/**
 * The transaction with the most rules, measured at 90 seconds end to end.
 *
 * Everything the clerk would otherwise have to look up is on the line as he types:
 * what is on hand, the rate that will be applied, and the line amount. Duplicate
 * product+grade rows are caught here rather than on submit, because each would pass
 * the stock check alone and together take more than exists.
 */
@Component({
  selector: 'app-dispatch-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule,
    ErrorBannerComponent, WarningListComponent,
  ],
  template: `
    <h2 mat-dialog-title>New dispatch</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      @if (saved(); as d) {
        <div class="saved" role="status">
          <mat-icon>check_circle</mat-icon>
          <div>
            <strong>{{ d.dispatchNumber }}</strong> — {{ money(d.totalAmount) }}.
            {{ d.customerName }} now owes <strong>{{ money(d.customerBalanceAfter) }}</strong>.
          </div>
        </div>
        <app-warning-list [warnings]="d.warnings" />
      }

      <form [formGroup]="form" class="form">
        <div class="head">
          <mat-form-field appearance="outline">
            <mat-label>Customer</mat-label>
            <mat-select formControlName="customerId">
              @for (c of customers(); track c.id) {
                <mat-option [value]="c.id">{{ c.code }} — {{ c.name }}</mat-option>
              }
            </mat-select>
            @if (serverError('customerId'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Date</mat-label>
            <input matInput [matDatepicker]="picker" formControlName="dispatchDate" [max]="today" [min]="earliest()" />
            <mat-datepicker-toggle matIconSuffix [for]="picker" />
            <mat-datepicker #picker />
            @if (serverError('dispatchDate'); as m) { <mat-error>{{ m }}</mat-error> }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Vehicle</mat-label>
            <input matInput formControlName="vehicleNumber" maxlength="24" />
          </mat-form-field>
        </div>

        <div class="lines" formArrayName="lines">
          <div class="lines__head">
            <span>Product</span><span>Grade</span><span class="num">Quantity</span>
            <span class="num">Rate</span><span class="num">Amount</span><span></span>
          </div>

          @for (row of lines.controls; track $index; let i = $index) {
            <div class="lines__row" [formGroupName]="i" [class.lines__row--dup]="duplicateIndexes().includes(i)">
              <mat-form-field appearance="outline">
                <mat-select formControlName="productId" placeholder="Product">
                  @for (p of products(); track p.id) {
                    <mat-option [value]="p.id">{{ p.code }} — {{ p.name }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-select formControlName="grade">
                  @for (g of grades(); track g) { <mat-option [value]="g">{{ g }}</mat-option> }
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <input matInput type="number" formControlName="quantity" min="1" class="num" />
                <mat-hint>{{ onHandLabel(i) }} on hand</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <input matInput type="number" formControlName="unitRate" step="0.01" class="num"
                       [placeholder]="listRateLabel(i)" />
                <mat-hint>Blank uses the list rate</mat-hint>
              </mat-form-field>

              <div class="lines__amount">{{ lineAmountLabel(i) }}</div>

              <button matIconButton type="button" (click)="removeLine(i)" [disabled]="lines.length === 1">
                <mat-icon>delete_outline</mat-icon>
              </button>
            </div>

            @if (shortIndexes().includes(i)) {
              <p class="lines__warn">
                <mat-icon>report_problem</mat-icon>
                Only {{ onHandLabel(i) }} on hand — the server will refuse this line.
              </p>
            }
          }
        </div>

        @if (duplicateIndexes().length > 0) {
          <p class="dup">
            <mat-icon>error_outline</mat-icon>
            The same product and grade appears on more than one line. Combine them — each
            would pass the stock check alone and together take more than exists.
          </p>
        }

        <button matButton type="button" (click)="addLine()" [disabled]="lines.length >= maxLines">
          <mat-icon>add</mat-icon> Add line
        </button>

        <mat-form-field appearance="outline">
          <mat-label>Notes</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
        </mat-form-field>

        <div class="total">
          <span>{{ lines.length }} line{{ lines.length === 1 ? '' : 's' }}</span>
          <strong>{{ money(total()) }}</strong>
        </div>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(savedAny())">{{ savedAny() ? 'Done' : 'Cancel' }}</button>
      <button matButton="filled" color="primary" (click)="save()"
              [disabled]="busy() || duplicateIndexes().length > 0">
        {{ busy() ? 'Saving…' : 'Save dispatch' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(1020px, 94vw); }
    .head { display: grid; grid-template-columns: 2fr 1fr 1fr; gap: .5rem; }
    .lines__head, .lines__row {
      display: grid;
      grid-template-columns: 3fr 1.3fr 1.2fr 1.2fr 1.3fr 48px;
      gap: .5rem; align-items: center;
    }
    .lines__head { font-size: .78rem; opacity: .6; padding: .25rem .25rem 0; text-transform: uppercase; letter-spacing: .04em; }
    .lines__head .num, .num { text-align: right; }
    .lines__row--dup { background: var(--bad-bg); border-radius: 8px; }
    .lines__amount { text-align: right; font-weight: 500; padding-bottom: 1.25rem; }
    .lines__warn, .dup {
      display: flex; gap: .4rem; align-items: center; font-size: .85rem;
      color: var(--warn-ink); margin: -.5rem 0 .5rem .25rem;
    }
    .dup { color: var(--bad-ink); }
    .lines__warn mat-icon, .dup mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .total {
      display: flex; justify-content: space-between; align-items: center;
      background: var(--surface-sunken); border-radius: 8px; padding: .75rem 1rem; margin-top: .25rem;
      font-size: 1.05rem;
    }
    .saved {
      display: flex; gap: .6rem; align-items: center;
      background: var(--ok-bg); border: 1px solid var(--ok-line); color: var(--ok-ink);
      border-radius: 8px; padding: .7rem 1rem; margin-bottom: 1rem;
    }
  `,
})
export class DispatchFormComponent {
  readonly dialogRef = inject(MatDialogRef<DispatchFormComponent, boolean>);

  private readonly sales = inject(SalesService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  readonly maxLines = MAX_LINES;
  readonly today = new Date();

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly saved = signal<Dispatch | null>(null);
  readonly savedAny = signal(false);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly customers = signal<Customer[]>([]);
  readonly products = computed(() => this.lookups.activeProducts().filter((p) => p.isActive));
  readonly grades = computed<QualityGrade[]>(() => this.lookups.enabledGrades());

  readonly backdateDays = computed(() => this.lookups.settingNumber('Backdate.Days.Dispatch', 7));
  readonly earliest = computed(() => {
    const d = new Date();
    d.setDate(d.getDate() - this.backdateDays());
    return d;
  });

  private idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    customerId: ['', Validators.required],
    dispatchDate: [new Date() as Date | string, Validators.required],
    vehicleNumber: [''],
    notes: [''],
    lines: this.fb.array([this.lineGroup()]),
  });

  /** Recomputed on every keystroke so the per-line figures below stay live. */
  private readonly formVersion = signal(0);

  constructor() {
    this.form.valueChanges.subscribe(() => this.formVersion.update((n) => n + 1));
    void this.loadCustomers();
  }

  private async loadCustomers(): Promise<void> {
    const page = await this.sales.listCustomers({ pageSize: 200 });
    this.customers.set(page.items.filter((c) => c.isActive));
  }

  get lines(): FormArray {
    return this.form.controls.lines as FormArray;
  }

  private lineGroup() {
    return this.fb.nonNullable.group({
      productId: ['', Validators.required],
      grade: ['First' as QualityGrade, Validators.required],
      quantity: [0, [Validators.required, Validators.min(1)]],
      unitRate: [null as number | null],
    });
  }

  addLine(): void { this.lines.push(this.lineGroup()); }
  removeLine(i: number): void { this.lines.removeAt(i); }

  private lineAt(i: number) {
    this.formVersion();
    return this.lines.at(i)?.value as { productId: string; grade: QualityGrade; quantity: number; unitRate: number | null };
  }

  onHand(i: number): number {
    const l = this.lineAt(i);
    return l?.productId ? this.lookups.stockFor(l.productId, l.grade) : 0;
  }

  onHandLabel(i: number): string { return quantity(this.onHand(i)); }

  listRate(i: number): number | undefined {
    const l = this.lineAt(i);
    return l?.productId ? this.lookups.rateFor(l.productId, l.grade) : undefined;
  }

  listRateLabel(i: number): string {
    const rate = this.listRate(i);
    return rate === undefined ? 'No list rate' : money(rate);
  }

  /** The rate that will actually be applied: the clerk's if typed, otherwise the list. */
  private effectiveRate(i: number): number | undefined {
    const typed = this.lineAt(i)?.unitRate;
    return typed !== null && typed !== undefined && Number(typed) > 0 ? Number(typed) : this.listRate(i);
  }

  lineAmount(i: number): number {
    const rate = this.effectiveRate(i);
    const qty = Number(this.lineAt(i)?.quantity ?? 0);
    return rate === undefined ? 0 : rate * qty;
  }

  lineAmountLabel(i: number): string {
    return this.effectiveRate(i) === undefined ? '—' : money(this.lineAmount(i));
  }

  readonly total = computed(() => {
    this.formVersion();
    return this.lines.controls.reduce((sum, _, i) => sum + this.lineAmount(i), 0);
  });

  /** Lines asking for more than is on hand. Flagged before the server refuses them. */
  readonly shortIndexes = computed(() => {
    this.formVersion();
    return this.lines.controls
      .map((_, i) => i)
      .filter((i) => {
        const l = this.lineAt(i);
        return !!l?.productId && Number(l.quantity) > this.onHand(i);
      });
  });

  readonly duplicateIndexes = computed(() => {
    this.formVersion();
    const seen = new Map<string, number>();
    const dups: number[] = [];

    this.lines.controls.forEach((_, i) => {
      const l = this.lineAt(i);
      if (!l?.productId) return;

      const key = `${l.productId}|${l.grade}`;
      if (seen.has(key)) dups.push(i, seen.get(key)!);
      else seen.set(key, i);
    });

    return [...new Set(dups)];
  });

  money(value: number): string { return money(value); }

  serverError(field: string): string | undefined { return this.fieldErrors().get(field); }

  async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    const v = this.form.getRawValue();

    try {
      const dispatch = await this.sales.createDispatch(
        {
          customerId: v.customerId,
          dispatchDate: toIsoDate(v.dispatchDate) ?? todayIso(),
          vehicleNumber: v.vehicleNumber?.trim() || null,
          notes: v.notes?.trim() || null,
          lines: (v.lines as typeof v.lines).map((l) => ({
            productId: l.productId,
            grade: l.grade,
            quantity: Number(l.quantity),
            unitRate: l.unitRate !== null && l.unitRate !== undefined ? Number(l.unitRate) : null,
          })),
        },
        this.idempotencyKey,
      );

      this.saved.set(dispatch);
      this.savedAny.set(true);

      // Nothing was half-written on a failure, and on success the stock has moved - the
      // catalogue cache has to catch up or the next dispatch prices off stale figures.
      await this.lookups.reloadProducts();

      this.idempotencyKey = newIdempotencyKey();
      this.form.setControl('lines', this.fb.array([this.lineGroup()]));
      this.form.patchValue({ vehicleNumber: '', notes: '' });
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));

      // STOCK_INSUFFICIENT names the products in its detail and its field path carries no
      // line index, so it belongs in the banner where all of it is readable.
      this.error.set(problem);
      this.saved.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}

import { Component, ViewChild, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule, MatInput } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { LookupsService } from '../../core/lookups.service';
import { ProductionService } from '../../core/production.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { toIsoDate, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { WarningListComponent } from '../../shared/components/warning-list';
import { ProductionEntry } from '../../core/api.types';

/**
 * One kiln unload. The highest-frequency write in the system, with a 45-second
 * end-to-end target including typing, which is why:
 *
 *  - the product picker takes focus immediately;
 *  - the total and loss update as the clerk types, so nothing needs checking afterwards;
 *  - the breakage reason only appears once something is actually broken;
 *  - saving reopens the form empty and refocused, because kiln loads come in batches.
 */
@Component({
  selector: 'app-production-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule,
    ErrorBannerComponent, WarningListComponent,
  ],
  template: `
    <h2 mat-dialog-title>Record production</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      @if (justSaved(); as saved) {
        <div class="saved" role="status">
          <mat-icon>check_circle</mat-icon>
          <div>
            <strong>{{ saved.entryNumber }}</strong> saved —
            {{ saved.totalFired }} fired, {{ saved.lossPercentage }}% loss.
          </div>
        </div>
        <app-warning-list [warnings]="saved.warnings" />
      }

      <form [formGroup]="form" class="form" (keydown.enter)="$event.preventDefault(); save()">
        <mat-form-field appearance="outline">
          <mat-label>Product</mat-label>
          <mat-select formControlName="productId" #productSelect>
            @for (product of products(); track product.id) {
              <mat-option [value]="product.id">{{ product.code }} — {{ product.name }}</mat-option>
            }
          </mat-select>
          @if (serverError('productId'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Date</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="entryDate" [max]="today" [min]="earliest()" />
          <mat-datepicker-toggle matIconSuffix [for]="picker" />
          <mat-datepicker #picker />
          <mat-hint>Within {{ backdateDays() }} days</mat-hint>
          @if (serverError('entryDate'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <div class="quantities">
          <mat-form-field appearance="outline">
            <mat-label>Good</mat-label>
            <input matInput type="number" formControlName="quantityGood" min="0" />
            <mat-hint>Enters stock at First</mat-hint>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Seconds</mat-label>
            <input matInput type="number" formControlName="quantitySeconds" min="0" />
            <mat-hint>Enters stock at Second</mat-hint>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Broken</mat-label>
            <input matInput type="number" formControlName="quantityBroken" min="0" />
            <mat-hint>Never enters stock</mat-hint>
          </mat-form-field>
        </div>

        <div class="totals" [class.totals--high]="lossPercentage() > lossWarningAt()">
          <span><strong>{{ totalFired() }}</strong> fired</span>
          <span><strong>{{ lossPercentage() }}%</strong> loss</span>
          @if (lossPercentage() > lossWarningAt()) {
            <span class="totals__note">
              <mat-icon>info_outline</mat-icon> High, but it will still be recorded
            </span>
          }
        </div>

        @if (form.controls.quantityBroken.value > 0) {
          <mat-form-field appearance="outline">
            <mat-label>Breakage reason</mat-label>
            <mat-select formControlName="breakageReasonCodeId">
              @for (reason of breakageReasons(); track reason.id) {
                <mat-option [value]="reason.id">{{ reason.description }}</mat-option>
              }
            </mat-select>
            @if (serverError('breakageReasonCodeId'); as message) { <mat-error>{{ message }}</mat-error> }
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Batch reference</mat-label>
          <input matInput formControlName="batchReference" maxlength="48" />
          <mat-hint>Optional — the factory's own lot reference</mat-hint>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Notes</mat-label>
          <textarea matInput formControlName="notes" rows="2" maxlength="500"></textarea>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(saveCount() > 0)">
        {{ saveCount() > 0 ? 'Done' : 'Cancel' }}
      </button>
      <button matButton="filled" color="primary" (click)="save()" [disabled]="busy()">
        {{ busy() ? 'Saving…' : 'Save entry' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(560px, 85vw); }
    .quantities { display: grid; grid-template-columns: repeat(3, 1fr); gap: .5rem; }
    .totals {
      display: flex; gap: 1.5rem; align-items: center;
      background: #f1f3f4; border-radius: 8px; padding: .6rem .9rem; margin: .25rem 0 .75rem;
    }
    .totals--high { background: #fff4e5; color: #663c00; }
    .totals__note { display: flex; align-items: center; gap: .35rem; font-size: .85rem; margin-left: auto; }
    .saved {
      display: flex; gap: .6rem; align-items: center;
      background: #e6f4ea; border: 1px solid #b7e1c4; color: #137333;
      border-radius: 8px; padding: .7rem 1rem; margin-bottom: 1rem;
    }
  `,
})
export class ProductionFormComponent {
  readonly dialogRef = inject(MatDialogRef<ProductionFormComponent, boolean>);

  private readonly production = inject(ProductionService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  @ViewChild('productSelect') productSelect?: { focus: () => void };

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly justSaved = signal<ProductionEntry | null>(null);
  readonly saveCount = signal(0);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly today = new Date();
  readonly products = computed(() => this.lookups.activeProducts().filter((p) => p.isActive));
  readonly breakageReasons = computed(() => this.lookups.reasonsFor('Breakage'));
  readonly backdateDays = computed(() => this.lookups.settingNumber('Backdate.Days.Production', 7));
  readonly lossWarningAt = computed(() =>
    this.lookups.settingNumber('Production.LossWarningPercent', 25),
  );

  readonly earliest = computed(() => {
    const d = new Date();
    d.setDate(d.getDate() - this.backdateDays());
    return d;
  });

  /** One key per submission. Replaced only after a save actually succeeds. */
  private idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    productId: ['', Validators.required],
    entryDate: [new Date() as Date | string, Validators.required],
    quantityGood: [0, [Validators.required, Validators.min(0)]],
    quantitySeconds: [0, [Validators.required, Validators.min(0)]],
    quantityBroken: [0, [Validators.required, Validators.min(0)]],
    breakageReasonCodeId: [null as string | null],
    batchReference: [''],
    notes: [''],
  });

  /** Mirrors the server's own arithmetic so the clerk never has to guess. */
  readonly totalFired = signal(0);
  readonly lossPercentage = signal(0);

  constructor() {
    this.form.valueChanges.subscribe(() => this.recompute());

    // The reason field only exists while something is broken, so its requirement has to
    // come and go with it rather than being declared once.
    effect(() => {
      const broken = this.form.controls.quantityBroken.value;
      const control = this.form.controls.breakageReasonCodeId;

      const required = Number(broken) > 0;
      const hasValidator = control.hasValidator(Validators.required);

      if (required && !hasValidator) {
        control.addValidators(Validators.required);
        control.updateValueAndValidity({ emitEvent: false });
      } else if (!required && hasValidator) {
        control.removeValidators(Validators.required);
        control.setValue(null, { emitEvent: false });
        control.updateValueAndValidity({ emitEvent: false });
      }
    });
  }

  private recompute(): void {
    const v = this.form.getRawValue();
    const total = Number(v.quantityGood || 0) + Number(v.quantitySeconds || 0) + Number(v.quantityBroken || 0);

    this.totalFired.set(total);
    this.lossPercentage.set(
      total === 0 ? 0 : Math.round((Number(v.quantityBroken || 0) / total) * 10000) / 100,
    );
  }

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async save(): Promise<void> {
    if (this.totalFired() === 0) {
      this.error.set({
        title: 'Nothing to record',
        status: 400,
        detail: 'This entry records no pieces at all. Enter what came out of the kiln.',
        code: 'NO_QUANTITY',
      });
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
      const saved = await this.production.create(
        {
          productId: v.productId,
          entryDate: toIsoDate(v.entryDate) ?? todayIso(),
          quantityGood: Number(v.quantityGood || 0),
          quantitySeconds: Number(v.quantitySeconds || 0),
          quantityBroken: Number(v.quantityBroken || 0),
          breakageReasonCodeId: v.breakageReasonCodeId,
          batchReference: v.batchReference?.trim() || null,
          notes: v.notes?.trim() || null,
        },
        this.idempotencyKey,
      );

      this.justSaved.set(saved);
      this.saveCount.update((n) => n + 1);

      // Kiln loads come in batches, so the form resets for the next one rather than
      // closing. The date and product are kept: the next unload is usually the same day
      // and often the same product.
      this.idempotencyKey = newIdempotencyKey();
      this.form.patchValue(
        { quantityGood: 0, quantitySeconds: 0, quantityBroken: 0, breakageReasonCodeId: null, batchReference: '', notes: '' },
        { emitEvent: true },
      );
      this.form.markAsUntouched();

      await this.lookups.reloadProducts();
      queueMicrotask(() => this.productSelect?.focus());
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));
      this.error.set(problem.errors?.length ? null : problem);
      this.justSaved.set(null);
    } finally {
      this.busy.set(false);
    }
  }
}

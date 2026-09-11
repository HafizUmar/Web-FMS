import { Component, computed, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { QualityGrade } from '../../core/api.types';
import { LookupsService } from '../../core/lookups.service';
import { ProductsService, ProductWithVersion } from '../../core/products.service';
import { newIdempotencyKey } from '../../core/idempotency';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface ProductFormData {
  /** Null creates; a loaded product edits. */
  existing: ProductWithVersion | null;
  /** True once the product appears on a document - the code can no longer change. */
  codeLocked: boolean;
}

@Component({
  selector: 'app-product-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit() ? 'Edit product' : 'New product' }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Code</mat-label>
          <input matInput formControlName="code" (blur)="upperCaseCode()" maxlength="24" />
          <mat-hint>Capital letters, digits and hyphens</mat-hint>
          @if (serverError('code'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.code.touched && form.controls.code.invalid) {
            <mat-error>Required, capitals, digits and hyphens only</mat-error>
          }
        </mat-form-field>

        @if (data.codeLocked) {
          <p class="locked">
            <mat-icon>lock</mat-icon>
            This code already appears on production entries or dispatches, so it cannot be
            changed. Deactivate this product and create a new one instead.
          </p>
        }

        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" maxlength="160" />
          @if (serverError('name'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.name.touched && form.controls.name.invalid) {
            <mat-error>Name is required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Capacity (ml)</mat-label>
          <input matInput type="number" formControlName="capacityMl" min="1" max="5000" />
          <mat-hint>Optional, 1 to 5000</mat-hint>
          @if (serverError('capacityMl'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Description</mat-label>
          <textarea matInput formControlName="description" rows="2" maxlength="500"></textarea>
        </mat-form-field>

        @if (!isEdit()) {
          <h3 class="prices__heading">Prices</h3>
          <p class="prices__hint">
            One rate per grade. Prices are changed later through the Prices action, which
            keeps the old rate in the history.
          </p>

          @if (serverError('prices'); as message) {
            <p class="prices__error">{{ message }}</p>
          }

          <div formArrayName="prices">
            @for (row of prices.controls; track $index; let i = $index) {
              <div class="prices__row" [formGroupName]="i">
                <mat-form-field appearance="outline">
                  <mat-label>Grade</mat-label>
                  <mat-select formControlName="grade">
                    @for (grade of grades(); track grade) {
                      <mat-option [value]="grade">{{ grade }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>

                <mat-form-field appearance="outline">
                  <mat-label>Rate (PKR)</mat-label>
                  <input matInput type="number" formControlName="unitRate" min="0.01" step="0.01" />
                  @if (serverError('prices[' + i + '].unitRate'); as message) {
                    <mat-error>{{ message }}</mat-error>
                  }
                </mat-form-field>

                <button matIconButton type="button" (click)="removePrice(i)" [disabled]="prices.length === 1">
                  <mat-icon>delete_outline</mat-icon>
                </button>
              </div>
            }
          </div>

          <button matButton type="button" (click)="addPrice()" [disabled]="prices.length >= grades().length">
            <mat-icon>add</mat-icon> Add grade
          </button>
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
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(520px, 80vw); }
    .prices__heading { margin: .75rem 0 .25rem; font-size: 1rem; }
    .prices__hint { opacity: .7; margin: 0 0 .5rem; font-size: .85rem; }
    .prices__error { color: var(--bad-ink); margin: 0 0 .5rem; font-size: .85rem; }
    .prices__row { display: grid; grid-template-columns: 1fr 1fr auto; gap: .5rem; align-items: center; }
    .locked { display: flex; gap: .5rem; align-items: flex-start; background: var(--warn-bg);
              border: 1px solid var(--warn-line); color: var(--warn-ink); padding: .6rem .75rem;
              border-radius: 8px; margin: 0 0 .75rem; font-size: .85rem; }
  `,
})
export class ProductFormComponent {
  readonly dialogRef = inject(MatDialogRef<ProductFormComponent, boolean>);
  readonly data = inject<ProductFormData>(MAT_DIALOG_DATA);

  private readonly products = inject(ProductsService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly isEdit = computed(() => this.data.existing !== null);
  readonly grades = computed<QualityGrade[]>(() => this.lookups.enabledGrades());

  /**
   * One key for the life of this dialog, so a retry after a validation failure - or after
   * a lost reply - reuses it and cannot create a second product.
   */
  private readonly idempotencyKey = newIdempotencyKey();

  readonly form = this.fb.nonNullable.group({
    code: [
      this.data.existing?.product.code ?? '',
      [Validators.required, Validators.pattern(/^[A-Z0-9-]+$/)],
    ],
    name: [this.data.existing?.product.name ?? '', Validators.required],
    capacityMl: [this.data.existing?.product.capacityMl ?? null as number | null],
    description: [this.data.existing?.product.description ?? ''],
    prices: this.fb.array(
      this.data.existing
        ? []
        : [this.priceGroup(this.lookups.enabledGrades()[0] ?? 'First')],
    ),
  });

  get prices(): FormArray {
    return this.form.controls.prices as FormArray;
  }

  constructor() {
    if (this.data.codeLocked) this.form.controls.code.disable();
  }

  private priceGroup(grade: QualityGrade) {
    return this.fb.nonNullable.group({
      grade: [grade, Validators.required],
      unitRate: [0, [Validators.required, Validators.min(0.01)]],
    });
  }

  addPrice(): void {
    const used = this.prices.controls.map((c) => c.value.grade);
    const next = this.grades().find((g) => !used.includes(g)) ?? this.grades()[0];

    this.prices.push(this.priceGroup(next));
  }

  removePrice(index: number): void {
    this.prices.removeAt(index);
  }

  upperCaseCode(): void {
    const control = this.form.controls.code;
    const upper = (control.value ?? '').trim().toUpperCase();

    // The server uppercases anyway; doing it here means the clerk sees what will be saved.
    if (upper !== control.value) control.setValue(upper);
  }

  serverError(field: string): string | undefined {
    return this.fieldErrors().get(field);
  }

  async save(): Promise<void> {
    this.upperCaseCode();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    const raw = this.form.getRawValue();

    const common = {
      code: raw.code,
      name: raw.name.trim(),
      capacityMl: raw.capacityMl ? Number(raw.capacityMl) : null,
      description: raw.description?.trim() || null,
    };

    try {
      if (this.data.existing) {
        await this.products.update(
          this.data.existing.product.id,
          common,
          this.data.existing.etag ?? '',
        );
      } else {
        await this.products.create(
          { ...common, prices: raw.prices as { grade: QualityGrade; unitRate: number }[] },
          this.idempotencyKey,
        );
      }

      this.dialogRef.close(true);
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));

      // A conflict or a business-rule refusal has no field to attach to, so it goes in
      // the banner where the clerk will actually read it.
      this.error.set(problem.errors?.length ? null : problem);
    } finally {
      this.busy.set(false);
    }
  }
}

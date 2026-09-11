import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ReasonCode, ReasonCodeType } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { humanise } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface ReasonCodeFormData {
  existing: ReasonCode | null;
}

const TYPES: ReasonCodeType[] = [
  'Breakage', 'StockAdjustment', 'SalesReturn', 'DispatchCancellation',
];

@Component({
  selector: 'app-reason-code-form',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatIconModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isEdit() ? 'Edit reason code' : 'New reason code' }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Used for</mat-label>
          <mat-select formControlName="type">
            @for (t of types; track t) { <mat-option [value]="t">{{ label(t) }}</mat-option> }
          </mat-select>
          @if (serverError('type'); as message) { <mat-error>{{ message }}</mat-error> }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Code</mat-label>
          <input matInput formControlName="code" (blur)="upperCaseCode()" maxlength="32" />
          <mat-hint>Short, capitals - it is what the movement records</mat-hint>
          @if (serverError('code'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.code.touched && form.controls.code.invalid) {
            <mat-error>Required, capitals, digits and underscores</mat-error>
          }
        </mat-form-field>

        @if (isEdit()) {
          <p class="locked">
            <mat-icon>lock</mat-icon>
            The code and what it applies to are fixed. Both are written into every movement
            that cites this reason, and changing either here would rewrite what those
            movements mean. Retire it and add a new one instead.
          </p>
        }

        <mat-form-field appearance="outline">
          <mat-label>What the clerk sees</mat-label>
          <input matInput formControlName="description" maxlength="200" />
          @if (serverError('description'); as message) {
            <mat-error>{{ message }}</mat-error>
          } @else if (form.controls.description.touched && form.controls.description.invalid) {
            <mat-error>Required</mat-error>
          }
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Order in the list</mat-label>
          <input matInput type="number" formControlName="sortOrder" min="0" max="999" />
          <mat-hint>Lowest first. Put the everyday reasons at the top.</mat-hint>
          @if (serverError('sortOrder'); as message) { <mat-error>{{ message }}</mat-error> }
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
    .form { display: flex; flex-direction: column; gap: .25rem; min-width: min(480px, 80vw); }
    .locked { display: flex; gap: .5rem; align-items: flex-start; background: var(--warn-bg);
              border: 1px solid var(--warn-line); color: var(--warn-ink); padding: .6rem .75rem;
              border-radius: 8px; margin: 0 0 .75rem; font-size: .85rem; }
  `,
})
export class ReasonCodeFormComponent {
  readonly dialogRef = inject(MatDialogRef<ReasonCodeFormComponent, boolean>);
  readonly data = inject<ReasonCodeFormData>(MAT_DIALOG_DATA);

  private readonly admin = inject(AdminService);
  private readonly http = inject(HttpClient);
  private readonly fb = inject(FormBuilder);

  readonly types = TYPES;
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly isEdit = computed(() => this.data.existing !== null);

  readonly form = this.fb.nonNullable.group({
    type: [this.data.existing?.type ?? ('Breakage' as ReasonCodeType), Validators.required],
    code: [
      this.data.existing?.code ?? '',
      [Validators.required, Validators.pattern(/^[A-Z0-9_]+$/)],
    ],
    description: [this.data.existing?.description ?? '', Validators.required],
    sortOrder: [this.data.existing?.sortOrder ?? 100, [Validators.required, Validators.min(0)]],
  });

  constructor() {
    if (this.data.existing) {
      this.form.controls.type.disable();
      this.form.controls.code.disable();
    }
  }

  label(type: ReasonCodeType): string {
    return humanise(type);
  }

  upperCaseCode(): void {
    const control = this.form.controls.code;
    const upper = (control.value ?? '').trim().toUpperCase().replace(/\s+/g, '_');

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

    try {
      if (this.data.existing) {
        await this.admin.updateReasonCode(this.data.existing.id, {
          description: raw.description.trim(),
          sortOrder: Number(raw.sortOrder),
        });
      } else {
        await firstValueFrom(
          this.http.post<ReasonCode>('/api/v1/reason-codes', {
            type: raw.type,
            code: raw.code,
            description: raw.description.trim(),
            sortOrder: Number(raw.sortOrder),
          }),
        );
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

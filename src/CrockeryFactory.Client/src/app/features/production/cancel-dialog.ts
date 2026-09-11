import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { ProblemDetails } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface CancelDialogData {
  title: string;
  /** What cancelling will do, in the clerk's terms. */
  consequence: string;
  /** Performs the cancellation; the dialog handles its errors. */
  cancel: (reason: string) => Promise<unknown>;
}

const MINIMUM_REASON = 10;

/**
 * Cancelling any document. The reason is what the owner reads when he asks why the
 * figures changed, so the minimum length is enforced here with a live counter rather
 * than left to fail on submit.
 */
@Component({
  selector: 'app-cancel-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />

      <p class="consequence">{{ data.consequence }}</p>

      <form [formGroup]="form">
        <mat-form-field appearance="outline">
          <mat-label>Reason</mat-label>
          <textarea matInput formControlName="reason" rows="3" maxlength="300" cdkFocusInitial></textarea>
          <mat-hint [class.short]="remaining() > 0">
            @if (remaining() > 0) {
              {{ remaining() }} more character{{ remaining() === 1 ? '' : 's' }} needed
            } @else {
              This is recorded against the document permanently
            }
          </mat-hint>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close(false)">Keep it</button>
      <button matButton="filled" color="warn" (click)="confirm()" [disabled]="busy() || remaining() > 0">
        {{ busy() ? 'Cancelling…' : 'Cancel document' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .consequence { margin: 0 0 1rem; }
    mat-form-field { width: min(480px, 80vw); }
    .short { color: #b3261e; }
  `,
})
export class CancelDialogComponent {
  readonly dialogRef = inject(MatDialogRef<CancelDialogComponent, boolean>);
  readonly data = inject<CancelDialogData>(MAT_DIALOG_DATA);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);

  readonly form = this.fb.nonNullable.group({
    reason: ['', [Validators.required, Validators.minLength(MINIMUM_REASON)]],
  });

  private readonly reasonLength = signal(0);
  readonly remaining = computed(() => Math.max(0, MINIMUM_REASON - this.reasonLength()));

  constructor() {
    this.form.controls.reason.valueChanges.subscribe((v) =>
      this.reasonLength.set(v.trim().length),
    );
  }

  async confirm(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.data.cancel(this.form.getRawValue().reason.trim());
      this.dialogRef.close(true);
    } catch (thrown) {
      // STOCK_INSUFFICIENT_FOR_REVERSAL and CANCELLATION_TOO_LATE land here, and their
      // detail says what to do instead.
      this.error.set(thrown as ProblemDetails);
    } finally {
      this.busy.set(false);
    }
  }
}

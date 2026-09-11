import { Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { StatementLine, StatementResponse } from '../../core/api.types';
import { SalesService } from '../../core/sales.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface StatementDialogData { customerId: string; customerName: string }

@Component({
  selector: 'app-statement-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatTableModule, MatFormFieldModule,
    MatInputModule, MatDatepickerModule, MatButtonModule, MatProgressBarModule,
    ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>Statement — {{ data.customerName }}</h2>

    <mat-dialog-content>
      <div class="range">
        <mat-form-field appearance="outline">
          <mat-label>From</mat-label>
          <input matInput [matDatepicker]="f" [formControl]="from" />
          <mat-datepicker-toggle matIconSuffix [for]="f" /><mat-datepicker #f />
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>To</mat-label>
          <input matInput [matDatepicker]="t" [formControl]="to" />
          <mat-datepicker-toggle matIconSuffix [for]="t" /><mat-datepicker #t />
        </mat-form-field>

        <button matButton="filled" color="primary" (click)="load()">Show</button>
      </div>

      <app-error-banner [problem]="error()" />
      @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

      @if (result(); as r) {
        <table mat-table [dataSource]="rowsWithOpening()">
          <ng-container matColumnDef="date">
            <th mat-header-cell *matHeaderCellDef>Date</th>
            <td mat-cell *matCellDef="let l">{{ l.date }}</td>
          </ng-container>

          <ng-container matColumnDef="documentType">
            <th mat-header-cell *matHeaderCellDef>Type</th>
            <td mat-cell *matCellDef="let l">{{ l.documentType }}</td>
          </ng-container>

          <ng-container matColumnDef="documentNumber">
            <th mat-header-cell *matHeaderCellDef>Document</th>
            <td mat-cell *matCellDef="let l"><code>{{ l.documentNumber }}</code></td>
          </ng-container>

          <ng-container matColumnDef="description">
            <th mat-header-cell *matHeaderCellDef>Description</th>
            <td mat-cell *matCellDef="let l">{{ l.description }}</td>
          </ng-container>

          <ng-container matColumnDef="debit">
            <th mat-header-cell *matHeaderCellDef class="num">Debit</th>
            <td mat-cell *matCellDef="let l" class="num">{{ l.debit === undefined ? '' : money(l.debit) }}</td>
          </ng-container>

          <ng-container matColumnDef="credit">
            <th mat-header-cell *matHeaderCellDef class="num">Credit</th>
            <td mat-cell *matCellDef="let l" class="num">{{ l.credit === undefined ? '' : money(l.credit) }}</td>
          </ng-container>

          <ng-container matColumnDef="runningBalance">
            <th mat-header-cell *matHeaderCellDef class="num">Balance</th>
            <td mat-cell *matCellDef="let l" class="num balance">{{ money(l.runningBalance) }}</td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"
              [class.row--cancelled]="isCancelled(row)"
              [class.row--opening]="row.documentType === 'Opening'"></tr>
        </table>

        <div class="closing">
          <span>Closing balance</span>
          <strong [class.advance]="r.closingBalance < 0">
            @if (r.closingBalance < 0) { {{ money(-r.closingBalance) }} in advance }
            @else { {{ money(r.closingBalance) }} }
          </strong>
        </div>

        <p class="note">
          Cancelled documents are shown at zero rather than removed, so the numbering has
          no gaps for the customer to ask about.
        </p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(940px, 94vw); }
    .range { display: flex; gap: .75rem; align-items: center; margin-bottom: .5rem; }
    .range mat-form-field { width: 180px; }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1rem; }
    .balance { font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .row--cancelled { opacity: .5; font-style: italic; }
    .row--opening { background: #f1f3f4; font-weight: 500; }
    .closing { display: flex; justify-content: space-between; padding: .9rem 1rem; margin-top: .5rem;
               background: #f1f3f4; border-radius: 8px; font-size: 1.05rem; }
    .advance { color: #137333; }
    .note { opacity: .6; font-size: .8rem; margin: .5rem 0 0; }
  `,
})
export class StatementDialogComponent {
  readonly dialogRef = inject(MatDialogRef<StatementDialogComponent>);
  readonly data = inject<StatementDialogData>(MAT_DIALOG_DATA);

  private readonly sales = inject(SalesService);

  readonly columns = ['date', 'documentType', 'documentNumber', 'description', 'debit', 'credit', 'runningBalance'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<StatementResponse | null>(null);

  readonly from = new FormControl<Date | null>(this.monthsAgo(3));
  readonly to = new FormControl<Date | null>(new Date());

  constructor() {
    void this.load();
  }

  private monthsAgo(n: number): Date {
    const d = new Date();
    d.setMonth(d.getMonth() - n);
    return d;
  }

  money(value: number): string { return money(value); }

  isCancelled(line: StatementLine): boolean { return line.description === 'Cancelled'; }

  /**
   * A synthetic opening row so the arithmetic reads top to bottom - the server returns
   * the opening balance as a field, not as a line.
   */
  readonly rowsWithOpening = computed<StatementLine[]>(() => {
    const r = this.result();
    if (!r) return [];

    return [
      {
        date: r.from,
        documentType: 'Opening',
        documentNumber: '',
        description: 'Balance brought forward',
        runningBalance: r.openingBalance,
      },
      ...r.lines,
    ];
  });

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.sales.statement(
          this.data.customerId,
          toIsoDate(this.from.value) ?? undefined,
          toIsoDate(this.to.value) ?? undefined,
        ),
      );
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }
}

import { Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { PagedResult, QualityGrade, StockMovementItem } from '../../core/api.types';
import { StockService } from '../../core/stock.service';
import { ProblemDetails } from '../../core/problem-details';
import { humanise, pageCount, quantity } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

export interface MovementsDialogData {
  productId: string;
  productCode: string;
  productName: string;
  grade: QualityGrade;
}

/**
 * The ledger for one product and grade.
 *
 * Running balance is the column that matters: it turns "the stock is wrong" into "the
 * stock went wrong on the fourteenth". The grade is fixed for the dialog because a
 * running balance across two grades describes nothing.
 */
@Component({
  selector: 'app-movements-dialog',
  imports: [
    MatDialogModule, MatTableModule, MatPaginatorModule,
    MatButtonModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.productCode }} — {{ data.grade }}</h2>

    <mat-dialog-content>
      <app-error-banner [problem]="error()" />
      @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="occurredOn">
          <th mat-header-cell *matHeaderCellDef>Date</th>
          <td mat-cell *matCellDef="let m">{{ m.occurredOn }}</td>
        </ng-container>

        <ng-container matColumnDef="movementType">
          <th mat-header-cell *matHeaderCellDef>Type</th>
          <td mat-cell *matCellDef="let m">{{ label(m.movementType) }}</td>
        </ng-container>

        <ng-container matColumnDef="referenceNumber">
          <th mat-header-cell *matHeaderCellDef>Document</th>
          <td mat-cell *matCellDef="let m"><code>{{ m.referenceNumber ?? '—' }}</code></td>
        </ng-container>

        <ng-container matColumnDef="in">
          <th mat-header-cell *matHeaderCellDef class="num">In</th>
          <td mat-cell *matCellDef="let m" class="num in">
            {{ m.quantity > 0 ? qty(m.quantity) : '' }}
          </td>
        </ng-container>

        <ng-container matColumnDef="out">
          <th mat-header-cell *matHeaderCellDef class="num">Out</th>
          <td mat-cell *matCellDef="let m" class="num out">
            {{ m.quantity < 0 ? qty(-m.quantity) : '' }}
          </td>
        </ng-container>

        <ng-container matColumnDef="runningBalance">
          <th mat-header-cell *matHeaderCellDef class="num">Balance</th>
          <td mat-cell *matCellDef="let m" class="num balance">{{ qty(m.runningBalance) }}</td>
        </ng-container>

        <ng-container matColumnDef="reason">
          <th mat-header-cell *matHeaderCellDef>Reason / notes</th>
          <td mat-cell *matCellDef="let m">
            {{ m.reasonDescription ?? m.notes ?? '—' }}
          </td>
        </ng-container>

        <ng-container matColumnDef="enteredBy">
          <th mat-header-cell *matHeaderCellDef>Entered by</th>
          <td mat-cell *matCellDef="let m">{{ m.enteredBy }}</td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">No movements for this product and grade.</p>
      }

      <mat-paginator
        [length]="result()?.totalCount ?? 0"
        [pageIndex]="(result()?.page ?? 1) - 1"
        [pageSize]="result()?.pageSize ?? 25"
        [pageSizeOptions]="[10, 25, 50, 100]"
        (page)="changePage($event)"
        showFirstLastButtons />

      @if (result(); as r) {
        <p class="summary">{{ r.totalCount }} movements · page {{ r.page }} of {{ pages() }}</p>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton (click)="dialogRef.close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(900px, 92vw); }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1rem; }
    .in { color: #137333; }
    .out { color: #c5221f; }
    .balance { font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary { opacity: .65; font-size: .85rem; margin: 0; }
  `,
})
export class MovementsDialogComponent {
  readonly dialogRef = inject(MatDialogRef<MovementsDialogComponent>);
  readonly data = inject<MovementsDialogData>(MAT_DIALOG_DATA);

  private readonly stock = inject(StockService);

  readonly columns = [
    'occurredOn', 'movementType', 'referenceNumber',
    'in', 'out', 'runningBalance', 'reason', 'enteredBy',
  ];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<StockMovementItem> | null>(null);

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly pages = computed(() => { const r = this.result(); return r ? pageCount(r) : 1; });

  private page = 1;
  private pageSize = 25;

  constructor() {
    void this.load();
  }

  qty(value: number): string { return quantity(value); }
  label(type: string): string { return humanise(type); }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.stock.movements(this.data.productId, {
          grade: this.data.grade,
          page: this.page,
          pageSize: this.pageSize,
        }),
      );
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  changePage(event: PageEvent): void {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    void this.load();
  }
}

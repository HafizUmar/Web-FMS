import { Component, computed, inject, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PagedResult, Payment } from '../../core/api.types';
import { SalesService } from '../../core/sales.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { humanise, money, pageCount, todayIso } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { PaymentFormComponent } from './payment-form';
import { CancelDialogComponent } from '../production/cancel-dialog';

@Component({
  selector: 'app-payments-page',
  imports: [
    MatTableModule, MatPaginatorModule, MatCheckboxModule, MatButtonModule,
    MatIconModule, MatTooltipModule, MatProgressBarModule,
    HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Payments</h1>
        <p class="page__sub">Money received against customer accounts.</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()" *appHasPermission="'CanRecordTransactions'">
        <mat-icon>payments</mat-icon> Record receipt
      </button>
    </header>

    <div class="filters">
      <mat-checkbox [checked]="includeCancelled()" (change)="toggleCancelled($event.checked)">
        Include cancelled
      </mat-checkbox>
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="paymentNumber">
          <th mat-header-cell *matHeaderCellDef>Receipt</th>
          <td mat-cell *matCellDef="let p"><code>{{ p.paymentNumber }}</code></td>
        </ng-container>

        <ng-container matColumnDef="paymentDate">
          <th mat-header-cell *matHeaderCellDef>Date</th>
          <td mat-cell *matCellDef="let p">{{ p.paymentDate }}</td>
        </ng-container>

        <ng-container matColumnDef="customerName">
          <th mat-header-cell *matHeaderCellDef>Customer</th>
          <td mat-cell *matCellDef="let p">{{ p.customerName }}</td>
        </ng-container>

        <ng-container matColumnDef="amount">
          <th mat-header-cell *matHeaderCellDef class="num">Amount</th>
          <td mat-cell *matCellDef="let p" class="num amount">{{ money(p.amount) }}</td>
        </ng-container>

        <ng-container matColumnDef="method">
          <th mat-header-cell *matHeaderCellDef>Method</th>
          <td mat-cell *matCellDef="let p">{{ label(p.method) }}</td>
        </ng-container>

        <ng-container matColumnDef="reference">
          <th mat-header-cell *matHeaderCellDef>Reference</th>
          <td mat-cell *matCellDef="let p">{{ p.reference ?? '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="enteredBy">
          <th mat-header-cell *matHeaderCellDef>Entered by</th>
          <td mat-cell *matCellDef="let p">{{ p.enteredBy }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let p">
            <span class="badge" [class.badge--off]="p.status === 'Cancelled'">{{ p.status }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let p" class="actions">
            @if (p.status === 'Active' && canCancel(p)) {
              <button matIconButton (click)="cancel(p)" matTooltip="Cancel this receipt">
                <mat-icon>undo</mat-icon>
              </button>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="row.status === 'Cancelled'"></tr>
      </table>

      @if (!loading() && rows().length === 0) { <p class="empty">No receipts yet.</p> }
    </div>

    <mat-paginator
      [length]="result()?.totalCount ?? 0"
      [pageIndex]="(result()?.page ?? 1) - 1"
      [pageSize]="result()?.pageSize ?? 25"
      [pageSizeOptions]="[10, 25, 50, 100, 200]"
      (page)="changePage($event)"
      showFirstLastButtons />

    @if (result(); as r) {
      <p class="summary">{{ r.totalCount }} receipts · page {{ r.page }} of {{ pages() }}</p>
    }
  `,
  styles: `
    .page__header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; }
    .filters { margin-bottom: .5rem; }
    .table-wrap { overflow-x: auto; background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.25rem; }
    .amount { font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .badge { font-size: .75rem; padding: .15rem .5rem; border-radius: 999px; background: #e6f4ea; color: #137333; }
    .badge--off { background: #fce8e6; color: #c5221f; }
    .row--off { opacity: .55; }
    .actions { width: 48px; text-align: right; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary { opacity: .65; font-size: .85rem; margin: .25rem 0 0; }
  `,
})
export class PaymentsPageComponent {
  private readonly sales = inject(SalesService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = [
    'paymentNumber', 'paymentDate', 'customerName', 'amount',
    'method', 'reference', 'enteredBy', 'status', 'actions',
  ];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<Payment> | null>(null);
  readonly includeCancelled = signal(false);

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly pages = computed(() => { const r = this.result(); return r ? pageCount(r) : 1; });

  private page = 1;
  private pageSize = 25;

  constructor() { void this.load(); }

  money(value: number): string { return money(value); }
  label(method: string): string { return humanise(method); }

  canCancel(payment: Payment): boolean {
    return this.auth.canCancel(payment.paymentDate, todayIso());
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.sales.listPayments({
          includeCancelled: this.includeCancelled(),
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

  toggleCancelled(checked: boolean): void {
    this.includeCancelled.set(checked);
    this.page = 1;
    void this.load();
  }

  async create(): Promise<void> {
    const ref = this.dialog.open(PaymentFormComponent);
    if ((await ref.afterClosed().toPromise()) === true) await this.load();
  }

  async cancel(payment: Payment): Promise<void> {
    const ref = this.dialog.open(CancelDialogComponent, {
      data: {
        title: `Cancel ${payment.paymentNumber}?`,
        // Cancelling a receipt increases what is owed, which is the change most likely to
        // be disputed, so the dialog says so plainly.
        consequence:
          `This adds ${money(payment.amount)} back to ${payment.customerName}'s balance — ` +
          'they will owe that much more.',
        cancel: (reason: string) => this.sales.cancelPayment(payment.id, { reason }),
      },
    });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(`${payment.paymentNumber} cancelled.`, 'Dismiss', { duration: 4000 });
      await this.load();
    }
  }
}

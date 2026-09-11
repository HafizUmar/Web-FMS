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
import { Dispatch, PagedResult } from '../../core/api.types';
import { SalesService } from '../../core/sales.service';
import { LookupsService } from '../../core/lookups.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, pageCount, todayIso } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { DispatchFormComponent } from './dispatch-form';
import { CancelDialogComponent } from '../production/cancel-dialog';

@Component({
  selector: 'app-dispatches-page',
  imports: [
    MatTableModule, MatPaginatorModule, MatCheckboxModule, MatButtonModule,
    MatIconModule, MatTooltipModule, MatProgressBarModule,
    HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Dispatches</h1>
        <p class="page__sub">Goods that left the factory, and what they were billed at.</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()" *appHasPermission="'CanRecordTransactions'">
        <mat-icon>local_shipping</mat-icon> New dispatch
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
      <table mat-table [dataSource]="rows()" multiTemplateDataRows>
        <ng-container matColumnDef="dispatchNumber">
          <th mat-header-cell *matHeaderCellDef>Dispatch</th>
          <td mat-cell *matCellDef="let d"><code>{{ d.dispatchNumber }}</code></td>
        </ng-container>

        <ng-container matColumnDef="dispatchDate">
          <th mat-header-cell *matHeaderCellDef>Date</th>
          <td mat-cell *matCellDef="let d">{{ d.dispatchDate }}</td>
        </ng-container>

        <ng-container matColumnDef="customerName">
          <th mat-header-cell *matHeaderCellDef>Customer</th>
          <td mat-cell *matCellDef="let d">{{ d.customerName }}</td>
        </ng-container>

        <ng-container matColumnDef="lineCount">
          <th mat-header-cell *matHeaderCellDef class="num">Lines</th>
          <td mat-cell *matCellDef="let d" class="num">{{ d.lines.length }}</td>
        </ng-container>

        <ng-container matColumnDef="totalAmount">
          <th mat-header-cell *matHeaderCellDef class="num">Total</th>
          <td mat-cell *matCellDef="let d" class="num amount">{{ money(d.totalAmount) }}</td>
        </ng-container>

        <ng-container matColumnDef="vehicleNumber">
          <th mat-header-cell *matHeaderCellDef>Vehicle</th>
          <td mat-cell *matCellDef="let d">{{ d.vehicleNumber ?? '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="enteredBy">
          <th mat-header-cell *matHeaderCellDef>Entered by</th>
          <td mat-cell *matCellDef="let d">{{ d.enteredBy }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let d">
            <span class="badge" [class.badge--off]="d.status === 'Cancelled'">{{ d.status }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let d" class="actions">
            <button matIconButton (click)="toggle(d)" [matTooltip]="expanded() === d.id ? 'Hide lines' : 'Show lines'">
              <mat-icon>{{ expanded() === d.id ? 'expand_less' : 'expand_more' }}</mat-icon>
            </button>

            <button matIconButton disabled matTooltip="Printing is not available yet">
              <mat-icon>print</mat-icon>
            </button>

            @if (d.status === 'Active' && canCancel(d)) {
              <button matIconButton (click)="cancel(d)" matTooltip="Cancel this dispatch">
                <mat-icon>undo</mat-icon>
              </button>
            }
          </td>
        </ng-container>

        <ng-container matColumnDef="lines">
          <td mat-cell *matCellDef="let d" [attr.colspan]="columns.length">
            @if (expanded() === d.id) {
              <div class="lines">
                @for (line of d.lines; track line.lineNumber) {
                  <div class="lines__row">
                    <span>{{ line.lineNumber }}</span>
                    <span><code>{{ line.productCode }}</code> {{ line.productName }}</span>
                    <span>{{ line.grade }}</span>
                    <span class="num">{{ line.quantity }}</span>
                    <span class="num">{{ money(line.unitRate) }}</span>
                    <span class="num">{{ money(line.lineAmount) }}</span>
                  </div>
                }
                <p class="lines__note">
                  Product code, name and rate are as they were when this dispatch was
                  raised — later changes to the catalogue do not alter it.
                </p>
              </div>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="row.status === 'Cancelled'"></tr>
        <tr mat-row *matRowDef="let row; columns: ['lines']" class="lines__host"></tr>
      </table>

      @if (!loading() && rows().length === 0) { <p class="empty">No dispatches yet.</p> }
    </div>

    <mat-paginator
      [length]="result()?.totalCount ?? 0"
      [pageIndex]="(result()?.page ?? 1) - 1"
      [pageSize]="result()?.pageSize ?? 25"
      [pageSizeOptions]="[10, 25, 50, 100, 200]"
      (page)="changePage($event)"
      showFirstLastButtons />

    @if (result(); as r) {
      <p class="summary">{{ r.totalCount }} dispatches · page {{ r.page }} of {{ pages() }}</p>
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
    td.num, th.num { padding-right: 1.1rem; }
    .amount { font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .badge { font-size: .75rem; padding: .15rem .5rem; border-radius: 999px; background: #e6f4ea; color: #137333; }
    .badge--off { background: #fce8e6; color: #c5221f; }
    .row--off { opacity: .55; }
    .actions { width: 150px; text-align: right; white-space: nowrap; }
    .lines__host td { padding: 0 !important; border-bottom: 0; }
    .lines { background: #fafafa; padding: .75rem 1.25rem; }
    .lines__row { display: grid; grid-template-columns: 40px 3fr 1fr 1fr 1fr 1.2fr; gap: .5rem; padding: .3rem 0; }
    .lines__note { opacity: .6; font-size: .8rem; margin: .5rem 0 0; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary { opacity: .65; font-size: .85rem; margin: .25rem 0 0; }
  `,
})
export class DispatchesPageComponent {
  private readonly sales = inject(SalesService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = [
    'dispatchNumber', 'dispatchDate', 'customerName', 'lineCount',
    'totalAmount', 'vehicleNumber', 'enteredBy', 'status', 'actions',
  ];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<Dispatch> | null>(null);
  readonly includeCancelled = signal(false);
  readonly expanded = signal<string | null>(null);

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly pages = computed(() => { const r = this.result(); return r ? pageCount(r) : 1; });

  private page = 1;
  private pageSize = 25;

  constructor() { void this.load(); }

  money(value: number): string { return money(value); }

  canCancel(dispatch: Dispatch): boolean {
    return this.auth.canCancel(dispatch.dispatchDate, todayIso());
  }

  toggle(dispatch: Dispatch): void {
    this.expanded.update((id) => (id === dispatch.id ? null : dispatch.id));
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.sales.listDispatches({
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
    const ref = this.dialog.open(DispatchFormComponent, { maxWidth: '96vw' });

    if ((await ref.afterClosed().toPromise()) === true) await this.load();
  }

  async cancel(dispatch: Dispatch): Promise<void> {
    const ref = this.dialog.open(CancelDialogComponent, {
      data: {
        title: `Cancel ${dispatch.dispatchNumber}?`,
        consequence:
          `This returns the goods to stock and reduces ${dispatch.customerName}'s balance by ` +
          `${money(dispatch.totalAmount)}. A printed dispatch is never edited — it is ` +
          'cancelled and re-entered, and the cancellation stays visible.',
        cancel: (reason: string) => this.sales.cancelDispatch(dispatch.id, { reason }),
      },
    });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(`${dispatch.dispatchNumber} cancelled.`, 'Dismiss', { duration: 4000 });
      await this.lookups.reloadProducts();
      await this.load();
    }
  }
}

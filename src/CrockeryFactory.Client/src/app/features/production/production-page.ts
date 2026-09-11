import { Component, computed, inject, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { PagedResult, ProductionEntry } from '../../core/api.types';
import { ProductionService } from '../../core/production.service';
import { LookupsService } from '../../core/lookups.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { pageCount, quantity, todayIso } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ProductionFormComponent } from './production-form';
import { CancelDialogComponent } from './cancel-dialog';

@Component({
  selector: 'app-production-page',
  imports: [
    MatTableModule, MatPaginatorModule, MatButtonModule, MatIconModule,
    MatCheckboxModule, MatProgressBarModule, MatTooltipModule,
    HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Production</h1>
        <p class="page__sub">What came off the kiln, and what was lost.</p>
      </div>

      <button matButton="filled" color="primary" (click)="record()" *appHasPermission="'CanRecordTransactions'">
        <mat-icon>add</mat-icon> Record production
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
        <ng-container matColumnDef="entryNumber">
          <th mat-header-cell *matHeaderCellDef>Entry</th>
          <td mat-cell *matCellDef="let e"><code>{{ e.entryNumber }}</code></td>
        </ng-container>

        <ng-container matColumnDef="entryDate">
          <th mat-header-cell *matHeaderCellDef>Date</th>
          <td mat-cell *matCellDef="let e">{{ e.entryDate }}</td>
        </ng-container>

        <ng-container matColumnDef="product">
          <th mat-header-cell *matHeaderCellDef>Product</th>
          <td mat-cell *matCellDef="let e">
            <code>{{ e.productCode }}</code> {{ e.productName }}
          </td>
        </ng-container>

        <ng-container matColumnDef="good">
          <th mat-header-cell *matHeaderCellDef class="num">Good</th>
          <td mat-cell *matCellDef="let e" class="num">{{ qty(e.quantityGood) }}</td>
        </ng-container>

        <ng-container matColumnDef="seconds">
          <th mat-header-cell *matHeaderCellDef class="num">Seconds</th>
          <td mat-cell *matCellDef="let e" class="num">{{ qty(e.quantitySeconds) }}</td>
        </ng-container>

        <ng-container matColumnDef="broken">
          <th mat-header-cell *matHeaderCellDef class="num">Broken</th>
          <td mat-cell *matCellDef="let e" class="num">{{ qty(e.quantityBroken) }}</td>
        </ng-container>

        <ng-container matColumnDef="totalFired">
          <th mat-header-cell *matHeaderCellDef class="num">Fired</th>
          <td mat-cell *matCellDef="let e" class="num">{{ qty(e.totalFired) }}</td>
        </ng-container>

        <ng-container matColumnDef="loss">
          <th mat-header-cell *matHeaderCellDef class="num">Loss</th>
          <td mat-cell *matCellDef="let e" class="num" [class.loss--high]="e.lossPercentage > lossWarningAt()">
            {{ e.lossPercentage }}%
          </td>
        </ng-container>

        <ng-container matColumnDef="reason">
          <th mat-header-cell *matHeaderCellDef>Breakage</th>
          <td mat-cell *matCellDef="let e">{{ e.breakageReason ?? '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="enteredBy">
          <th mat-header-cell *matHeaderCellDef>Entered by</th>
          <td mat-cell *matCellDef="let e">{{ e.enteredBy }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let e">
            <span class="badge" [class.badge--off]="e.status === 'Cancelled'">{{ e.status }}</span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let e" class="actions">
            @if (e.status === 'Active' && canCancel(e)) {
              <button matIconButton (click)="cancelEntry(e)" matTooltip="Cancel this entry">
                <mat-icon>undo</mat-icon>
              </button>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="row.status === 'Cancelled'"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">Nothing recorded yet.</p>
      }
    </div>

    <mat-paginator
      [length]="result()?.totalCount ?? 0"
      [pageIndex]="(result()?.page ?? 1) - 1"
      [pageSize]="result()?.pageSize ?? 25"
      [pageSizeOptions]="[10, 25, 50, 100, 200]"
      (page)="changePage($event)"
      showFirstLastButtons />

    @if (result(); as r) {
      <p class="summary">Showing {{ from() }}–{{ to() }} of {{ r.totalCount }} · page {{ r.page }} of {{ pages() }}</p>
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
    .loss--high { color: #b26a00; font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .badge { font-size: .75rem; padding: .15rem .5rem; border-radius: 999px; background: #e6f4ea; color: #137333; }
    .badge--off { background: #fce8e6; color: #c5221f; }
    .row--off { opacity: .55; }
    .actions { width: 48px; text-align: right; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary { opacity: .65; font-size: .85rem; margin: .25rem 0 0; }
  `,
})
export class ProductionPageComponent {
  private readonly production = inject(ProductionService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = [
    'entryNumber', 'entryDate', 'product', 'good', 'seconds', 'broken',
    'totalFired', 'loss', 'reason', 'enteredBy', 'status', 'actions',
  ];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<ProductionEntry> | null>(null);
  readonly includeCancelled = signal(false);

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly lossWarningAt = computed(() =>
    this.lookups.settingNumber('Production.LossWarningPercent', 25),
  );

  readonly pages = computed(() => { const r = this.result(); return r ? pageCount(r) : 1; });
  readonly from = computed(() => {
    const r = this.result();
    return !r || r.totalCount === 0 ? 0 : (r.page - 1) * r.pageSize + 1;
  });
  readonly to = computed(() => {
    const r = this.result();
    return r ? Math.min(r.page * r.pageSize, r.totalCount) : 0;
  });

  private page = 1;
  private pageSize = 25;

  constructor() {
    void this.load();
  }

  qty(value: number): string { return quantity(value); }

  /**
   * PR-07: a clerk may cancel today's entry, the owner any of them. Duplicated from the
   * server because permissions are per-user and this rule is per-record; the 403 is still
   * handled if the two ever disagree.
   */
  canCancel(entry: ProductionEntry): boolean {
    return this.auth.canCancel(entry.entryDate, todayIso());
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const result = await this.production.list({
        includeCancelled: this.includeCancelled(),
        page: this.page,
        pageSize: this.pageSize,
      });

      if (result.items.length === 0 && result.totalCount > 0 && this.page > 1) {
        this.page = 1;
        await this.load();
        return;
      }

      this.result.set(result);
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

  async record(): Promise<void> {
    const ref = this.dialog.open(ProductionFormComponent, { autoFocus: 'first-tabbable' });

    if ((await ref.afterClosed().toPromise()) === true) {
      await this.load();
    }
  }

  async cancelEntry(entry: ProductionEntry): Promise<void> {
    const ref = this.dialog.open(CancelDialogComponent, {
      data: {
        title: `Cancel ${entry.entryNumber}?`,
        consequence:
          `This reverses ${quantity(entry.quantityGood)} at First and ` +
          `${quantity(entry.quantitySeconds)} at Second out of stock. The entry and its ` +
          'original movements stay in the ledger — both are visible afterwards.',
        cancel: (reason: string) => this.production.cancel(entry.id, { reason }),
      },
    });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(`${entry.entryNumber} cancelled.`, 'Dismiss', { duration: 4000 });
      await this.lookups.reloadProducts();
      await this.load();
    }
  }
}

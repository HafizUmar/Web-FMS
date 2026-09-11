import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PagedResult, ProductListItem, QualityGrade } from '../../core/api.types';
import { ProductsService } from '../../core/products.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, pageCount, quantity } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog';
import { ProductFormComponent, ProductFormData } from './product-form';
import { PricesFormComponent } from './prices-form';

@Component({
  selector: 'app-products-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatPaginatorModule, MatFormFieldModule,
    MatInputModule, MatCheckboxModule, MatButtonModule, MatIconModule, MatMenuModule,
    MatChipsModule, MatProgressBarModule, HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Products</h1>
        <p class="page__sub">The catalogue, with current prices and what is in the godown.</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()" *appHasPermission="'CanManageCatalogue'">
        <mat-icon>add</mat-icon> New product
      </button>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__search">
        <mat-label>Search</mat-label>
        <input matInput [formControl]="search" placeholder="Code or name" />
        <mat-icon matIconSuffix>search</mat-icon>
      </mat-form-field>

      <mat-checkbox [checked]="includeInactive()" (change)="toggleInactive($event.checked)">
        Include inactive
      </mat-checkbox>
    </div>

    <app-error-banner [problem]="error()" />

    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="code">
          <th mat-header-cell *matHeaderCellDef>Code</th>
          <td mat-cell *matCellDef="let p"><code>{{ p.code }}</code></td>
        </ng-container>

        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let p">{{ p.name }}</td>
        </ng-container>

        <ng-container matColumnDef="capacity">
          <th mat-header-cell *matHeaderCellDef class="num">Capacity</th>
          <td mat-cell *matCellDef="let p" class="num">
            {{ p.capacityMl ? p.capacityMl + ' ml' : '—' }}
          </td>
        </ng-container>

        <ng-container matColumnDef="priceFirst">
          <th mat-header-cell *matHeaderCellDef class="num">Rate (First)</th>
          <td mat-cell *matCellDef="let p" class="num">{{ rate(p, 'First') }}</td>
        </ng-container>

        <ng-container matColumnDef="priceSecond">
          <th mat-header-cell *matHeaderCellDef class="num">Rate (Second)</th>
          <td mat-cell *matCellDef="let p" class="num">{{ rate(p, 'Second') }}</td>
        </ng-container>

        <ng-container matColumnDef="stockFirst">
          <th mat-header-cell *matHeaderCellDef class="num">Stock (First)</th>
          <td mat-cell *matCellDef="let p" class="num">{{ stock(p, 'First') }}</td>
        </ng-container>

        <ng-container matColumnDef="stockSecond">
          <th mat-header-cell *matHeaderCellDef class="num">Stock (Second)</th>
          <td mat-cell *matCellDef="let p" class="num">{{ stock(p, 'Second') }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let p">
            <span class="badge" [class.badge--off]="!p.isActive">
              {{ p.isActive ? 'Active' : 'Inactive' }}
            </span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let p" class="actions">
            @if (canManage()) {
              <button matIconButton [matMenuTriggerFor]="menu" aria-label="Actions">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #menu="matMenu">
                <button mat-menu-item (click)="edit(p)">
                  <mat-icon>edit</mat-icon><span>Edit</span>
                </button>
                @if (canSetPrices()) {
                  <button mat-menu-item (click)="editPrices(p)">
                    <mat-icon>sell</mat-icon><span>Prices</span>
                  </button>
                }
                @if (p.isActive) {
                  <button mat-menu-item (click)="deactivate(p)">
                    <mat-icon>block</mat-icon><span>Deactivate</span>
                  </button>
                }
              </mat-menu>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="!row.isActive"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">
          @if (search.value) { No product matches “{{ search.value }}”. }
          @else { No products yet. }
        </p>
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
      <p class="summary">
        Showing {{ from() }}–{{ to() }} of {{ r.totalCount }} · page {{ r.page }} of {{ pages() }}
      </p>
    }
  `,
  styles: `
    .page__header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; }
    .filters { display: flex; gap: 1.5rem; align-items: center; margin-bottom: .5rem; }
    .filters__search { width: 320px; }
    .table-wrap { overflow-x: auto; background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.25rem; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .badge { font-size: .75rem; padding: .15rem .5rem; border-radius: 999px; background: #e6f4ea; color: #137333; }
    .badge--off { background: #f1f3f4; color: #5f6368; }
    .row--off { opacity: .55; }
    .actions { width: 48px; text-align: right; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary { opacity: .65; font-size: .85rem; margin: .25rem 0 0; }
  `,
})
export class ProductsPageComponent {
  private readonly products = inject(ProductsService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = [
    'code', 'name', 'capacity',
    'priceFirst', 'priceSecond',
    'stockFirst', 'stockSecond',
    'status', 'actions',
  ];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<ProductListItem> | null>(null);
  readonly includeInactive = signal(false);

  readonly search = new FormControl('', { nonNullable: true });

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly canManage = computed(() => this.auth.has('CanManageCatalogue'));
  readonly canSetPrices = computed(() => this.auth.has('CanSetPrices'));

  /** The API returns no totalPages, so it is derived here. */
  readonly pages = computed(() => {
    const r = this.result();
    return r ? pageCount(r) : 1;
  });

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
    // Debounced so a search does not fire a request per keystroke on factory WiFi.
    this.search.valueChanges
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe(() => {
        this.page = 1;
        void this.load();
      });

    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const result = await this.products.list({
        search: this.search.value,
        includeInactive: this.includeInactive(),
        page: this.page,
        pageSize: this.pageSize,
      });

      // A filter change can leave the requested page past the end; go back rather than
      // showing an empty table under a paginator that says there are rows.
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

  toggleInactive(checked: boolean): void {
    this.includeInactive.set(checked);
    this.page = 1;
    void this.load();
  }

  rate(product: ProductListItem, grade: QualityGrade): string {
    return money(product.prices.find((p) => p.grade === grade)?.unitRate);
  }

  stock(product: ProductListItem, grade: QualityGrade): string {
    return quantity(product.stock.find((s) => s.grade === grade)?.quantity ?? 0);
  }

  async create(): Promise<void> {
    const data: ProductFormData = { existing: null, codeLocked: false };

    const saved = await this.openForm(data);
    if (saved) {
      this.snackBar.open('Product created.', 'Dismiss', { duration: 4000 });
      await this.load();
    }
  }

  async edit(row: ProductListItem): Promise<void> {
    try {
      // Re-read to get the ETag: an update without the version the server currently
      // holds is refused, and a version read at list time may already be stale.
      const existing = await this.products.get(row.id);

      // Any stock movement means the code is fixed. Cheapest available signal.
      const codeLocked = row.stock.some((s) => s.quantity !== 0);

      const saved = await this.openForm({ existing, codeLocked });
      if (saved) {
        this.snackBar.open('Product updated.', 'Dismiss', { duration: 4000 });
        await this.load();
      }
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }

  private async openForm(data: ProductFormData): Promise<boolean> {
    const ref = this.dialog.open(ProductFormComponent, { data, autoFocus: 'first-tabbable' });
    return (await ref.afterClosed().toPromise()) === true;
  }

  async editPrices(row: ProductListItem): Promise<void> {
    try {
      const { product } = await this.products.get(row.id);
      const ref = this.dialog.open(PricesFormComponent, { data: product });

      if ((await ref.afterClosed().toPromise()) === true) {
        this.snackBar.open('Prices updated.', 'Dismiss', { duration: 4000 });
        await this.load();
      }
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }

  async deactivate(row: ProductListItem): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: `Deactivate ${row.code}?`,
        message:
          'It will no longer appear on new dispatches or production entries. ' +
          'Existing records are unaffected.',
        confirmLabel: 'Deactivate',
        destructive: true,
      },
    });

    if ((await ref.afterClosed().toPromise()) !== true) return;

    this.error.set(null);

    try {
      await this.products.deactivate(row.id);
      this.snackBar.open(`${row.code} deactivated.`, 'Dismiss', { duration: 4000 });
      await this.load();
    } catch (problem) {
      // PRODUCT_HAS_STOCK lands here, and its detail names the quantities still held.
      this.error.set(problem as ProblemDetails);
    }
  }
}

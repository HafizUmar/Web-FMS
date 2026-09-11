import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Customer, OutstandingResponse, OutstandingRow, PagedResult } from '../../core/api.types';
import { SalesService } from '../../core/sales.service';
import { AuthService } from '../../core/auth.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, pageCount } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog';
import { CustomerFormComponent, CustomerFormData } from './customer-form';
import { StatementDialogComponent } from './statement-dialog';
import { PaymentFormComponent } from './payment-form';

@Component({
  selector: 'app-customers-page',
  imports: [
    ReactiveFormsModule, MatTabsModule, MatTableModule, MatPaginatorModule,
    MatFormFieldModule, MatInputModule, MatCheckboxModule, MatButtonModule,
    MatIconModule, MatMenuModule, MatTooltipModule, MatProgressBarModule,
    HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Customers</h1>
        <p class="page__sub">Accounts, and what each of them owes.</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()" *appHasPermission="'CanRecordTransactions'">
        <mat-icon>person_add</mat-icon> New customer
      </button>
    </header>

    <app-error-banner [problem]="error()" />

    <mat-tab-group (selectedIndexChange)="onTab($event)">
      <!-- ------------------------------------------------------ outstanding -->
      <mat-tab label="Outstanding">
        @if (loadingOutstanding()) { <mat-progress-bar mode="indeterminate" /> }

        @if (outstanding(); as o) {
          <p class="asof">Largest debt first, as at {{ o.asOf }}.</p>

          <div class="table-wrap">
            <table mat-table [dataSource]="o.rows">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Customer</th>
                <td mat-cell *matCellDef="let r">
                  <div><strong>{{ r.name }}</strong></div>
                  <small><code>{{ r.code }}</code> {{ r.city ?? '' }}</small>
                </td>
              </ng-container>

              <ng-container matColumnDef="phone">
                <th mat-header-cell *matHeaderCellDef>Phone</th>
                <td mat-cell *matCellDef="let r">{{ r.phone ?? '—' }}</td>
              </ng-container>

              <ng-container matColumnDef="totalDispatched">
                <th mat-header-cell *matHeaderCellDef class="num">Dispatched</th>
                <td mat-cell *matCellDef="let r" class="num">{{ money(r.totalDispatched) }}</td>
              </ng-container>

              <ng-container matColumnDef="totalPaid">
                <th mat-header-cell *matHeaderCellDef class="num">Paid</th>
                <td mat-cell *matCellDef="let r" class="num">{{ money(r.totalPaid) }}</td>
              </ng-container>

              <ng-container matColumnDef="outstanding">
                <th mat-header-cell *matHeaderCellDef class="num">Outstanding</th>
                <td mat-cell *matCellDef="let r" class="num owing">
                  @if (r.outstanding < 0) {
                    <span class="advance">{{ money(-r.outstanding) }} in advance</span>
                  } @else {
                    {{ money(r.outstanding) }}
                  }
                </td>
              </ng-container>

              <ng-container matColumnDef="lastPaymentDate">
                <th mat-header-cell *matHeaderCellDef>Last payment</th>
                <td mat-cell *matCellDef="let r"
                    [class.stale]="r.lastPaymentDate && r.daysSinceLastPayment > 30"
                    [class.very-stale]="r.lastPaymentDate && r.daysSinceLastPayment > 60">
                  @if (r.lastPaymentDate) {
                    {{ r.lastPaymentDate }} <small>({{ r.daysSinceLastPayment }}d)</small>
                  } @else { Never }
                </td>
              </ng-container>

              <ng-container matColumnDef="actions">
                <th mat-header-cell *matHeaderCellDef></th>
                <td mat-cell *matCellDef="let r" class="actions">
                  <button matIconButton (click)="statement(r.customerId, r.name)" matTooltip="Statement">
                    <mat-icon>receipt_long</mat-icon>
                  </button>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="outstandingColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: outstandingColumns"></tr>
            </table>
          </div>

          <div class="total">
            <span>{{ o.rows.length }} account{{ o.rows.length === 1 ? '' : 's' }}</span>
            <strong>{{ money(o.totalOutstanding) }} outstanding</strong>
          </div>
        }
      </mat-tab>

      <!-- --------------------------------------------------------- all -->
      <mat-tab label="All customers">
        <div class="filters">
          <mat-form-field appearance="outline" class="filters__search">
            <mat-label>Search</mat-label>
            <input matInput [formControl]="search" placeholder="Code, name, city or phone" />
            <mat-icon matIconSuffix>search</mat-icon>
          </mat-form-field>

          <mat-checkbox [formControl]="includeInactive">Include inactive</mat-checkbox>
        </div>

        @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

        <div class="table-wrap">
          <table mat-table [dataSource]="rows()">
            <ng-container matColumnDef="code">
              <th mat-header-cell *matHeaderCellDef>Code</th>
              <td mat-cell *matCellDef="let c"><code>{{ c.code }}</code></td>
            </ng-container>

            <ng-container matColumnDef="name">
              <th mat-header-cell *matHeaderCellDef>Name</th>
              <td mat-cell *matCellDef="let c">{{ c.name }}</td>
            </ng-container>

            <ng-container matColumnDef="city">
              <th mat-header-cell *matHeaderCellDef>City</th>
              <td mat-cell *matCellDef="let c">{{ c.city ?? '—' }}</td>
            </ng-container>

            <ng-container matColumnDef="phone">
              <th mat-header-cell *matHeaderCellDef>Phone</th>
              <td mat-cell *matCellDef="let c">{{ c.phone ?? '—' }}</td>
            </ng-container>

            <ng-container matColumnDef="openingBalance">
              <th mat-header-cell *matHeaderCellDef class="num">Opening</th>
              <td mat-cell *matCellDef="let c" class="num">{{ money(c.openingBalance) }}</td>
            </ng-container>

            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Status</th>
              <td mat-cell *matCellDef="let c">
                <span class="badge" [class.badge--off]="!c.isActive">
                  {{ c.isActive ? 'Active' : 'Inactive' }}
                </span>
              </td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef></th>
              <td mat-cell *matCellDef="let c" class="actions">
                <button matIconButton [matMenuTriggerFor]="menu"><mat-icon>more_vert</mat-icon></button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item (click)="statement(c.id, c.name)">
                    <mat-icon>receipt_long</mat-icon><span>Statement</span>
                  </button>
                  @if (canRecord()) {
                    <button mat-menu-item (click)="edit(c)">
                      <mat-icon>edit</mat-icon><span>Edit</span>
                    </button>
                    <button mat-menu-item (click)="receipt(c)">
                      <mat-icon>payments</mat-icon><span>Record receipt</span>
                    </button>
                    @if (c.isActive) {
                      <button mat-menu-item (click)="deactivate(c)">
                        <mat-icon>block</mat-icon><span>Deactivate</span>
                      </button>
                    }
                  }
                </mat-menu>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="!row.isActive"></tr>
          </table>

          @if (!loading() && rows().length === 0) { <p class="empty">No customers match.</p> }
        </div>

        <mat-paginator
          [length]="result()?.totalCount ?? 0"
          [pageIndex]="(result()?.page ?? 1) - 1"
          [pageSize]="result()?.pageSize ?? 25"
          [pageSizeOptions]="[10, 25, 50, 100, 200]"
          (page)="changePage($event)"
          showFirstLastButtons />

        @if (result(); as r) {
          <p class="summary">{{ r.totalCount }} customers · page {{ r.page }} of {{ pages() }}</p>
        }
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `
    .page__header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; }
    .filters { display: flex; gap: 1.5rem; align-items: center; margin: 1rem 0 .5rem; }
    .filters__search { width: 320px; }
    .asof { opacity: .7; margin: 1rem 0 .5rem; }
    .table-wrap { overflow-x: auto; background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.25rem; }
    .owing { font-weight: 500; }
    .advance { color: #137333; font-weight: 400; }
    .stale { color: #b26a00; }
    .very-stale { color: #c5221f; font-weight: 500; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .badge { font-size: .75rem; padding: .15rem .5rem; border-radius: 999px; background: #e6f4ea; color: #137333; }
    .badge--off { background: #f1f3f4; color: #5f6368; }
    .row--off { opacity: .55; }
    .actions { width: 48px; text-align: right; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .total { display: flex; justify-content: space-between; padding: .9rem 1.25rem; margin-top: .5rem;
             background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); }
    .summary { opacity: .65; font-size: .85rem; margin: .25rem 0 0; }
  `,
})
export class CustomersPageComponent {
  private readonly sales = inject(SalesService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly outstandingColumns = ['name', 'phone', 'totalDispatched', 'totalPaid', 'outstanding', 'lastPaymentDate', 'actions'];
  readonly columns = ['code', 'name', 'city', 'phone', 'openingBalance', 'status', 'actions'];

  readonly loading = signal(false);
  readonly loadingOutstanding = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<Customer> | null>(null);
  readonly outstanding = signal<OutstandingResponse | null>(null);

  readonly search = new FormControl('', { nonNullable: true });
  readonly includeInactive = new FormControl(false, { nonNullable: true });

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly canRecord = computed(() => this.auth.has('CanRecordTransactions'));
  readonly pages = computed(() => { const r = this.result(); return r ? pageCount(r) : 1; });

  private page = 1;
  private pageSize = 25;

  constructor() {
    this.search.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => {
      this.page = 1;
      void this.load();
    });
    this.includeInactive.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.page = 1;
      void this.load();
    });

    void this.loadOutstanding();
  }

  money(value: number): string { return money(value); }

  onTab(index: number): void {
    if (index === 0) void this.loadOutstanding();
    else if (!this.result()) void this.load();
  }

  async loadOutstanding(): Promise<void> {
    this.loadingOutstanding.set(true);
    this.error.set(null);

    try {
      // Server order is largest debt first and is left alone - that order is the report.
      this.outstanding.set(await this.sales.outstanding());
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loadingOutstanding.set(false);
    }
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.sales.listCustomers({
          search: this.search.value,
          includeInactive: this.includeInactive.value,
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

  statement(customerId: string, customerName: string): void {
    this.dialog.open(StatementDialogComponent, { data: { customerId, customerName } });
  }

  async create(): Promise<void> {
    const data: CustomerFormData = { existing: null, openingBalanceLocked: false };
    const ref = this.dialog.open(CustomerFormComponent, { data });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open('Customer created.', 'Dismiss', { duration: 4000 });
      await this.refresh();
    }
  }

  async edit(customer: Customer): Promise<void> {
    try {
      const existing = await this.sales.getCustomer(customer.id);

      // The opening balance locks the moment anything is posted. The outstanding report
      // already knows, so no extra request is needed.
      const row = this.outstanding()?.rows.find((r) => r.customerId === customer.id);
      const locked = !!row && (row.totalDispatched !== 0 || row.totalPaid !== 0);

      const ref = this.dialog.open(CustomerFormComponent, {
        data: { existing, openingBalanceLocked: locked } satisfies CustomerFormData,
      });

      if ((await ref.afterClosed().toPromise()) === true) {
        this.snackBar.open('Customer updated.', 'Dismiss', { duration: 4000 });
        await this.refresh();
      }
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }

  async receipt(customer: Customer): Promise<void> {
    const ref = this.dialog.open(PaymentFormComponent, { data: { customerId: customer.id } });

    if ((await ref.afterClosed().toPromise()) === true) await this.refresh();
  }

  async deactivate(customer: Customer): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: `Deactivate ${customer.name}?`,
        message:
          'No new dispatches can be raised for them. They can still make payments against ' +
          'what they already owe, and their history is unaffected.',
        confirmLabel: 'Deactivate',
        destructive: true,
      },
    });

    if ((await ref.afterClosed().toPromise()) !== true) return;

    try {
      await this.sales.deactivateCustomer(customer.id);
      this.snackBar.open(`${customer.name} deactivated.`, 'Dismiss', { duration: 4000 });
      await this.refresh();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }

  private async refresh(): Promise<void> {
    await Promise.all([this.load(), this.loadOutstanding()]);
  }
}

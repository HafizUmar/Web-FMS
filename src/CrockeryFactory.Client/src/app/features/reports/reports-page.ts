import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { merge } from 'rxjs';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import {
  DailyStockResponse, OutstandingResponse, ProductionGroupBy, ProductionSummaryRow,
  SalesGroupBy, SalesSummaryResponse,
} from '../../core/api.types';
import { ReportsService } from '../../core/reports.service';
import { ProductionService } from '../../core/production.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, quantity, todayIso, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/**
 * RP-01, RP-02, RP-04 and RP-06 behind one tab group.
 *
 * Each tab fetches only when it is first opened, and again when its own filters change.
 * Opening the page therefore costs one request, not four - these reports scan the whole
 * ledger, and three of them would be read by nobody.
 *
 * Every report is a table rather than a chart: the reader is reconciling against a
 * register or chasing a specific customer, and both need the figure, not its shape.
 */
@Component({
  selector: 'app-reports-page',
  imports: [
    NgTemplateOutlet, ReactiveFormsModule, RouterLink, MatTabsModule, MatTableModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatDatepickerModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Reports</h1>
        <p class="page__sub">Stock, debt, kiln output and sales, for any period.</p>
      </div>
    </header>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <mat-tab-group [selectedIndex]="tab()" (selectedIndexChange)="openTab($event)" animationDuration="0ms">
      <!-- RP-01 -->
      <mat-tab label="Daily stock">
        <div class="tab">
          <div class="filters">
            <mat-form-field appearance="outline" class="filters__date">
              <mat-label>Date</mat-label>
              <input matInput [matDatepicker]="dailyPicker" [formControl]="dailyDate" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="dailyPicker" />
              <mat-datepicker #dailyPicker />
            </mat-form-field>

            <span class="filters__spacer"></span>
            <ng-container *ngTemplateOutlet="exportButtons" />
          </div>

          @if (daily(); as d) {
            <p class="lede">
              Opening plus what the kiln delivered, less what went out, plus adjustments,
              gives the closing figure. Every line adds up on its own.
            </p>

            <div class="table-wrap">
              <table mat-table [dataSource]="d.rows">
                <ng-container matColumnDef="productCode">
                  <th mat-header-cell *matHeaderCellDef>Code</th>
                  <td mat-cell *matCellDef="let r"><code>{{ r.productCode }}</code></td>
                  <td mat-footer-cell *matFooterCellDef><strong>All products</strong></td>
                </ng-container>

                <ng-container matColumnDef="productName">
                  <th mat-header-cell *matHeaderCellDef>Product</th>
                  <td mat-cell *matCellDef="let r">{{ r.productName }}</td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="grade">
                  <th mat-header-cell *matHeaderCellDef>Grade</th>
                  <td mat-cell *matCellDef="let r">{{ r.grade }}</td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="opening">
                  <th mat-header-cell *matHeaderCellDef class="num">Opening</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.opening) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">{{ qty(d.totalOpening) }}</td>
                </ng-container>

                <ng-container matColumnDef="received">
                  <th mat-header-cell *matHeaderCellDef class="num">Received</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.received) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">{{ qty(d.totalReceived) }}</td>
                </ng-container>

                <ng-container matColumnDef="dispatched">
                  <th mat-header-cell *matHeaderCellDef class="num">Dispatched</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.dispatched) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">{{ qty(d.totalDispatched) }}</td>
                </ng-container>

                <ng-container matColumnDef="adjusted">
                  <th mat-header-cell *matHeaderCellDef class="num">Adjusted</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ signed(r.adjusted) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">{{ signed(d.totalAdjusted) }}</td>
                </ng-container>

                <ng-container matColumnDef="closing">
                  <th mat-header-cell *matHeaderCellDef class="num">Closing</th>
                  <td mat-cell *matCellDef="let r" class="num figure closing">{{ qty(r.closing) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure closing">{{ qty(d.totalClosing) }}</td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="dailyColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: dailyColumns"></tr>
                <tr mat-footer-row *matFooterRowDef="dailyColumns" class="footer-row"></tr>
              </table>

              @if (d.rows.length === 0) {
                <p class="empty">Nothing moved on {{ d.date }}, and nothing was in stock.</p>
              }
            </div>
          }
        </div>
      </mat-tab>

      <!-- RP-02 -->
      <mat-tab label="Outstanding">
        <div class="tab">
          <div class="filters">
            <p class="lede lede--inline">
              Largest debt first. The same figures as the Customers page, gathered for printing.
            </p>
            <span class="filters__spacer"></span>
            <ng-container *ngTemplateOutlet="exportButtons" />
          </div>

          @if (outstanding(); as o) {
            <div class="table-wrap">
              <table mat-table [dataSource]="o.rows">
                <ng-container matColumnDef="code">
                  <th mat-header-cell *matHeaderCellDef>Code</th>
                  <td mat-cell *matCellDef="let r"><code>{{ r.code }}</code></td>
                  <td mat-footer-cell *matFooterCellDef><strong>{{ o.rows.length }} customers</strong></td>
                </ng-container>

                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef>Customer</th>
                  <td mat-cell *matCellDef="let r">
                    <a [routerLink]="['/customers']" [queryParams]="{ search: r.code }">{{ r.name }}</a>
                    @if (r.city) { <small class="muted"> · {{ r.city }}</small> }
                  </td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="totalDispatched">
                  <th mat-header-cell *matHeaderCellDef class="num">Dispatched</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ money(r.totalDispatched) }}</td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="totalPaid">
                  <th mat-header-cell *matHeaderCellDef class="num">Paid</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ money(r.totalPaid) }}</td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="outstanding">
                  <th mat-header-cell *matHeaderCellDef class="num">Outstanding</th>
                  <td mat-cell *matCellDef="let r" class="num figure closing">{{ money(r.outstanding) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure closing">
                    {{ money(o.totalOutstanding) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="daysSinceLastPayment">
                  <th mat-header-cell *matHeaderCellDef class="num">Since last payment</th>
                  <td mat-cell *matCellDef="let r" class="num">
                    @if (r.lastPaymentDate) {
                      <span [class.is-stale]="r.daysSinceLastPayment > 60">
                        @if (r.daysSinceLastPayment > 60) { <mat-icon class="inline">report_problem</mat-icon> }
                        {{ r.daysSinceLastPayment }} days
                      </span>
                    } @else {
                      <span class="muted">never paid</span>
                    }
                  </td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="outstandingColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: outstandingColumns"></tr>
                <tr mat-footer-row *matFooterRowDef="outstandingColumns" class="footer-row"></tr>
              </table>

              @if (o.rows.length === 0) {
                <p class="empty">Nobody owes anything. Every account is settled.</p>
              }
            </div>
            <p class="asof">As at {{ o.asOf }}</p>
          }
        </div>
      </mat-tab>

      <!-- RP-04 -->
      <mat-tab label="Production">
        <div class="tab">
          <div class="filters">
            <mat-form-field appearance="outline" class="filters__date">
              <mat-label>From</mat-label>
              <input matInput [matDatepicker]="prodFrom" [formControl]="productionFrom" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="prodFrom" />
              <mat-datepicker #prodFrom />
            </mat-form-field>

            <mat-form-field appearance="outline" class="filters__date">
              <mat-label>To</mat-label>
              <input matInput [matDatepicker]="prodTo" [formControl]="productionTo" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="prodTo" />
              <mat-datepicker #prodTo />
            </mat-form-field>

            <mat-form-field appearance="outline" class="filters__group">
              <mat-label>Group by</mat-label>
              <mat-select [formControl]="productionGroupBy">
                <mat-option value="Product">Product</mat-option>
                <mat-option value="Day">Day</mat-option>
                <mat-option value="Month">Month</mat-option>
              </mat-select>
            </mat-form-field>

            <span class="filters__spacer"></span>
            <ng-container *ngTemplateOutlet="exportButtons" />
          </div>

          @if (production(); as rows) {
            <div class="table-wrap">
              <table mat-table [dataSource]="rows">
                <ng-container matColumnDef="groupLabel">
                  <th mat-header-cell *matHeaderCellDef>{{ productionGroupBy.value }}</th>
                  <td mat-cell *matCellDef="let r">{{ r.groupLabel }}</td>
                  <td mat-footer-cell *matFooterCellDef><strong>All {{ rows.length }}</strong></td>
                </ng-container>

                <ng-container matColumnDef="entryCount">
                  <th mat-header-cell *matHeaderCellDef class="num">Entries</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.entryCount) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ qty(productionTotals().entryCount) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="totalFired">
                  <th mat-header-cell *matHeaderCellDef class="num">Fired</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.totalFired) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ qty(productionTotals().totalFired) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="totalGood">
                  <th mat-header-cell *matHeaderCellDef class="num">Good</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.totalGood) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ qty(productionTotals().totalGood) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="totalSeconds">
                  <th mat-header-cell *matHeaderCellDef class="num">Seconds</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.totalSeconds) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ qty(productionTotals().totalSeconds) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="totalBroken">
                  <th mat-header-cell *matHeaderCellDef class="num">Broken</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.totalBroken) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ qty(productionTotals().totalBroken) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="lossPercentage">
                  <th mat-header-cell *matHeaderCellDef class="num">Loss</th>
                  <td mat-cell *matCellDef="let r" class="num">
                    <span class="loss" [class.is-high]="r.lossPercentage > lossThreshold()">
                      @if (r.lossPercentage > lossThreshold()) {
                        <mat-icon class="inline">report_problem</mat-icon>
                      }
                      <span class="figure">{{ percent(r.lossPercentage) }}</span>
                    </span>
                  </td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ percent(productionTotals().lossPercentage) }}
                  </td>
                </ng-container>

                <ng-container matColumnDef="secondsPercentage">
                  <th mat-header-cell *matHeaderCellDef class="num">Seconds rate</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ percent(r.secondsPercentage) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">
                    {{ percent(productionTotals().secondsPercentage) }}
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="productionColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: productionColumns"></tr>
                <tr mat-footer-row *matFooterRowDef="productionColumns" class="footer-row"></tr>
              </table>

              @if (rows.length === 0) {
                <p class="empty">No production entries in this period.</p>
              }
            </div>

            <p class="asof">
              Loss above {{ lossThreshold() }}% is flagged. Broken pieces never enter stock,
              so fired is good plus seconds plus broken.
            </p>
          }
        </div>
      </mat-tab>

      <!-- RP-06 -->
      <mat-tab label="Sales">
        <div class="tab">
          <div class="filters">
            <mat-form-field appearance="outline" class="filters__date">
              <mat-label>From</mat-label>
              <input matInput [matDatepicker]="salesFromPicker" [formControl]="salesFrom" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="salesFromPicker" />
              <mat-datepicker #salesFromPicker />
            </mat-form-field>

            <mat-form-field appearance="outline" class="filters__date">
              <mat-label>To</mat-label>
              <input matInput [matDatepicker]="salesToPicker" [formControl]="salesTo" [max]="today" />
              <mat-datepicker-toggle matIconSuffix [for]="salesToPicker" />
              <mat-datepicker #salesToPicker />
            </mat-form-field>

            <mat-form-field appearance="outline" class="filters__group">
              <mat-label>Group by</mat-label>
              <mat-select [formControl]="salesGroupBy">
                <mat-option value="Customer">Customer</mat-option>
                <mat-option value="Product">Product</mat-option>
                <mat-option value="Month">Month</mat-option>
              </mat-select>
            </mat-form-field>

            <span class="filters__spacer"></span>
            <ng-container *ngTemplateOutlet="exportButtons" />
          </div>

          @if (sales(); as s) {
            <div class="table-wrap">
              <table mat-table [dataSource]="s.rows">
                <ng-container matColumnDef="groupLabel">
                  <th mat-header-cell *matHeaderCellDef>{{ s.groupBy }}</th>
                  <td mat-cell *matCellDef="let r">{{ r.groupLabel }}</td>
                  <td mat-footer-cell *matFooterCellDef><strong>Total</strong></td>
                </ng-container>

                <ng-container matColumnDef="dispatchCount">
                  <th mat-header-cell *matHeaderCellDef class="num">Dispatches</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.dispatchCount) }}</td>
                  <td mat-footer-cell *matFooterCellDef></td>
                </ng-container>

                <ng-container matColumnDef="totalQuantity">
                  <th mat-header-cell *matHeaderCellDef class="num">Units</th>
                  <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.totalQuantity) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure">{{ qty(s.totalQuantity) }}</td>
                </ng-container>

                <ng-container matColumnDef="totalAmount">
                  <th mat-header-cell *matHeaderCellDef class="num">Amount</th>
                  <td mat-cell *matCellDef="let r" class="num figure closing">{{ money(r.totalAmount) }}</td>
                  <td mat-footer-cell *matFooterCellDef class="num figure closing">
                    {{ money(s.totalAmount) }}
                  </td>
                </ng-container>

                <tr mat-header-row *matHeaderRowDef="salesColumns"></tr>
                <tr mat-row *matRowDef="let row; columns: salesColumns"></tr>
                <tr mat-footer-row *matFooterRowDef="salesColumns" class="footer-row"></tr>
              </table>

              @if (s.rows.length === 0) {
                <p class="empty">Nothing was dispatched in this period.</p>
              }
            </div>

            <p class="asof">
              {{ s.from }} to {{ s.to }}. Cancelled dispatches are excluded.
              A dispatch with several lines counts once under Customer and Month, and once
              per product under Product.
            </p>
          }
        </div>
      </mat-tab>
    </mat-tab-group>

    <!--
      One definition, used by every tab.

      The reason sits beside the buttons rather than in a tooltip on them: a disabled
      Material button takes no pointer events, so a tooltip explaining why it is disabled
      is the one tooltip nobody can ever read. The sentence is the server's own 501
      detail, not a copy of it kept here.
    -->
    <ng-template #exportButtons>
      <div class="exports">
        @if (!exports().available) {
          <span class="exports__reason" role="note">{{ exports().reason }}</span>
        }
        <button matButton [disabled]="!exports().available" (click)="download('pdf')">
          <mat-icon>picture_as_pdf</mat-icon> PDF
        </button>
        <button matButton [disabled]="!exports().available" (click)="download('xlsx')">
          <mat-icon>table_view</mat-icon> Excel
        </button>
      </div>
    </ng-template>
  `,
  styles: `

    mat-tab-group { background: var(--surface); border-radius: 8px; border: 1px solid var(--line); }
    .tab { padding: 1.25rem; }

    .filters { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; }
    .filters__date { width: 180px; }
    .filters__group { width: 170px; }
    .filters__spacer { flex: 1 1 auto; }
    .exports { display: flex; gap: .5rem; align-items: center; }
    .exports__reason { display: inline-flex; align-items: center; gap: .3rem;
                       max-width: 52ch; opacity: .7; text-align: right; font-size: .8rem; line-height: 1.25; }

    .lede { margin: 0 0 1rem; opacity: .75; max-width: 62ch; }
    .lede--inline { margin: 0; }
    .asof { margin: .75rem 0 0; opacity: .65; font-size: .875rem; max-width: 70ch; }

    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num, td.num.mat-mdc-footer-cell { padding-right: 1.25rem; }

    /* Columns of numbers are compared down the page, so the digits have to line up. */
    .closing { font-weight: 500; }

    .footer-row { background: var(--surface-sunken); }
    .footer-row td { font-weight: 500; border-top: 2px solid var(--line-strong); }

    .loss { display: inline-flex; align-items: center; gap: .25rem; justify-content: flex-end; }
    .loss.is-high, .is-stale { color: var(--warn-ink); font-weight: 500; }
    .is-stale { display: inline-flex; align-items: center; gap: .25rem; }

    .muted { opacity: .6; }
  `,
})
export class ReportsPageComponent {
  private readonly reports = inject(ReportsService);
  private readonly productionApi = inject(ProductionService);
  private readonly lookups = inject(LookupsService);

  readonly dailyColumns = [
    'productCode', 'productName', 'grade',
    'opening', 'received', 'dispatched', 'adjusted', 'closing',
  ];
  readonly outstandingColumns = [
    'code', 'name', 'totalDispatched', 'totalPaid', 'outstanding', 'daysSinceLastPayment',
  ];
  readonly productionColumns = [
    'groupLabel', 'entryCount', 'totalFired', 'totalGood', 'totalSeconds', 'totalBroken',
    'lossPercentage', 'secondsPercentage',
  ];
  readonly salesColumns = ['groupLabel', 'dispatchCount', 'totalQuantity', 'totalAmount'];

  readonly today = new Date();
  readonly tab = signal(0);
  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);

  readonly daily = signal<DailyStockResponse | null>(null);
  readonly outstanding = signal<OutstandingResponse | null>(null);
  readonly production = signal<ProductionSummaryRow[] | null>(null);
  readonly sales = signal<SalesSummaryResponse | null>(null);

  readonly exports = signal({ available: false, reason: 'Checking…' });

  readonly dailyDate = new FormControl<Date>(new Date(), { nonNullable: true });
  readonly productionFrom = new FormControl<Date | null>(monthStart());
  readonly productionTo = new FormControl<Date | null>(new Date());
  readonly productionGroupBy = new FormControl<ProductionGroupBy>('Product', { nonNullable: true });
  readonly salesFrom = new FormControl<Date | null>(monthStart());
  readonly salesTo = new FormControl<Date | null>(new Date());
  readonly salesGroupBy = new FormControl<SalesGroupBy>('Customer', { nonNullable: true });

  /**
   * The footer row. Both percentages are recomputed from the summed pieces rather than
   * averaged down the column: a product with forty pieces fired must not move the
   * factory's loss rate as far as one with nine thousand.
   */
  readonly productionTotals = computed(() => {
    const rows = this.production() ?? [];
    const sum = (pick: (r: ProductionSummaryRow) => number) =>
      rows.reduce((total, row) => total + pick(row), 0);

    const totalFired = sum((r) => r.totalFired);
    const totalBroken = sum((r) => r.totalBroken);
    const totalSeconds = sum((r) => r.totalSeconds);

    return {
      entryCount: sum((r) => r.entryCount),
      totalFired,
      totalGood: sum((r) => r.totalGood),
      totalSeconds,
      totalBroken,
      lossPercentage: totalFired === 0 ? 0 : (totalBroken / totalFired) * 100,
      secondsPercentage: totalFired === 0 ? 0 : (totalSeconds / totalFired) * 100,
    };
  });

  readonly lossThreshold = computed(() =>
    this.lookups.settingNumber('Production.LossWarningPercent', 25),
  );

  constructor() {
    this.dailyDate.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => void this.loadDaily());
    merge(this.productionFrom.valueChanges, this.productionTo.valueChanges, this.productionGroupBy.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.loadProduction());

    merge(this.salesFrom.valueChanges, this.salesTo.valueChanges, this.salesGroupBy.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.loadSales());

    void this.loadDaily();
    void this.reports.exportAvailability().then((state) => this.exports.set(state));
  }

  qty(value: number): string { return quantity(value); }
  money(value: number): string { return money(value); }

  /** An adjustment can go either way, and "-40" reads very differently from "40". */
  signed(value: number): string {
    if (value === 0) return '—';
    return value > 0 ? `+${quantity(value)}` : quantity(value);
  }

  percent(value: number): string {
    return `${value.toFixed(1)}%`;
  }

  /**
   * Tabs load on first open rather than all at once. Re-opening a tab keeps what is
   * already there: these are period reports, and re-reading the ledger to show the same
   * numbers back is a cost with no answer attached.
   */
  openTab(index: number): void {
    this.tab.set(index);
    this.error.set(null);

    if (index === 1 && this.outstanding() === null) void this.loadOutstanding();
    if (index === 2 && this.production() === null) void this.loadProduction();
    if (index === 3 && this.sales() === null) void this.loadSales();
  }

  download(format: 'pdf' | 'xlsx'): void {
    // Unreachable while the buttons are disabled; here so that enabling them the day the
    // renderer lands is a server change only.
    const endpoints = ['daily-stock', 'outstanding', 'production-summary', 'sales-summary'];
    window.open(`/api/v1/reports/${endpoints[this.tab()]}?format=${format}`, '_blank');
  }

  private async run<T>(work: () => Promise<T>, set: (value: T) => void): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      set(await work());
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  private loadDaily(): Promise<void> {
    const date = toIsoDate(this.dailyDate.value) ?? todayIso();
    return this.run(() => this.reports.dailyStock(date), (value) => this.daily.set(value));
  }

  private loadOutstanding(): Promise<void> {
    return this.run(() => this.reports.outstanding(), (value) => this.outstanding.set(value));
  }

  private loadProduction(): Promise<void> {
    return this.run(
      () => this.productionApi.summary(
        toIsoDate(this.productionFrom.value) ?? undefined,
        toIsoDate(this.productionTo.value) ?? undefined,
        undefined,
        this.productionGroupBy.value,
      ),
      (value) => this.production.set(value),
    );
  }

  private loadSales(): Promise<void> {
    return this.run(
      () => this.reports.salesSummary(
        toIsoDate(this.salesFrom.value) ?? undefined,
        toIsoDate(this.salesTo.value) ?? undefined,
        this.salesGroupBy.value,
      ),
      (value) => this.sales.set(value),
    );
  }
}

/** The first of the current month - the period a factory actually asks these reports for. */
function monthStart(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), 1);
}

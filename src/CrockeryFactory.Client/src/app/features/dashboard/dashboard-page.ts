import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { DashboardResponse } from '../../core/api.types';
import { ReportsService } from '../../core/reports.service';
import { AuthService } from '../../core/auth.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, parseUtc, quantity } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/**
 * RP-07. Deliberately not a chart: every figure here is a single current magnitude or
 * a short ranked list, which is what stat tiles and tables are for. A chart would add
 * decoration without adding an answer.
 *
 * The outstanding total is the one hero figure - it is the number the owner opens this
 * screen for, and the report that justifies the system to him.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    RouterLink, MatIconModule, MatButtonModule, MatProgressBarModule,
    MatTableModule, ErrorBannerComponent,
  ],
  template: `
    @if (denied()) {
      <div class="notice notice--warn" role="alert">
        <mat-icon>lock</mat-icon> You do not have access to that page.
      </div>
    }

    <header class="head">
      <div>
        <h1>Welcome, {{ auth.fullName() }}</h1>
        <p class="head__sub">{{ factoryName() }} · signed in as {{ auth.roles().join(', ') }}</p>
      </div>
      <button matButton (click)="refresh()" [disabled]="loading()">
        <mat-icon>refresh</mat-icon> Refresh
      </button>
    </header>

    <app-error-banner [problem]="error()" />
    @if (loading() && !data()) { <mat-progress-bar mode="indeterminate" /> }

    @if (data(); as d) {
      <!-- Hero: the figure this screen exists for -->
      <section class="hero">
        <p class="hero__label">Outstanding across all customers</p>
        <p class="hero__value" [class.hero__value--credit]="d.totalOutstanding < 0">
          @if (d.totalOutstanding < 0) {
            {{ money(-d.totalOutstanding) }}
          } @else {
            {{ money(d.totalOutstanding) }}
          }
        </p>
        <p class="hero__note">
          @if (d.totalOutstanding < 0) {
            held in advance across {{ d.customersWithBalance }} account{{ d.customersWithBalance === 1 ? '' : 's' }}
          } @else {
            owed by {{ d.customersWithBalance }} account{{ d.customersWithBalance === 1 ? '' : 's' }}
          }
        </p>
      </section>

      <section class="tiles">
        <div class="tile">
          <p class="tile__label">Units in stock</p>
          <p class="tile__value">{{ qty(d.totalUnitsInStock) }}</p>
          <p class="tile__note">{{ money(d.stockValue) }} at current rates</p>
        </div>

        <div class="tile">
          <p class="tile__label">Produced this month</p>
          <p class="tile__value">{{ qty(d.unitsProducedThisMonth) }}</p>
          <p class="tile__note">good and seconds, excluding breakages</p>
        </div>

        <div class="tile">
          <p class="tile__label">Loss this month</p>
          <p class="tile__value">{{ d.lossPercentageThisMonth }}%</p>
          <p class="tile__note tile__note--status" [class.is-warning]="lossIsHigh()">
            @if (lossIsHigh()) {
              <mat-icon>report_problem</mat-icon> above the {{ lossThreshold() }}% threshold
            } @else {
              <mat-icon>check_circle</mat-icon> within the {{ lossThreshold() }}% threshold
            }
          </p>
        </div>

        <div class="tile">
          <p class="tile__label">Sales this month</p>
          <p class="tile__value">{{ money(d.salesThisMonth) }}</p>
          <p class="tile__note">active dispatches only</p>
        </div>

        <div class="tile">
          <p class="tile__label">Payments this month</p>
          <p class="tile__value">{{ money(d.paymentsThisMonth) }}</p>
          <p class="tile__note">received against accounts</p>
        </div>
      </section>

      <section class="panels">
        <!-- Top debtors -->
        <article class="panel">
          <header class="panel__head">
            <h2>Largest balances</h2>
            <a matButton routerLink="/customers">See all</a>
          </header>

          @if (d.topDebtors.length === 0) {
            <p class="panel__empty">Nobody owes anything.</p>
          } @else {
            <table mat-table [dataSource]="d.topDebtors">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Customer</th>
                <td mat-cell *matCellDef="let r">
                  <div>{{ r.name }}</div>
                  <small>{{ r.city ?? '' }}</small>
                </td>
              </ng-container>

              <ng-container matColumnDef="outstanding">
                <th mat-header-cell *matHeaderCellDef class="num">Outstanding</th>
                <td mat-cell *matCellDef="let r" class="num figure">
                  @if (r.outstanding < 0) {
                    <span class="credit">{{ money(-r.outstanding) }} in advance</span>
                  } @else { {{ money(r.outstanding) }} }
                </td>
              </ng-container>

              <ng-container matColumnDef="lastPaymentDate">
                <th mat-header-cell *matHeaderCellDef class="num">Last paid</th>
                <td mat-cell *matCellDef="let r" class="num">
                  @if (r.lastPaymentDate) { {{ r.daysSinceLastPayment }}d ago } @else { Never }
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="debtorColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: debtorColumns"></tr>
            </table>
          }
        </article>

        <!-- Low stock -->
        <article class="panel">
          <header class="panel__head">
            <h2>Running low</h2>
            <a matButton routerLink="/stock">See all</a>
          </header>

          @if (d.lowStockProducts.length === 0) {
            <p class="panel__empty">Nothing is running low.</p>
          } @else {
            <table mat-table [dataSource]="d.lowStockProducts">
              <ng-container matColumnDef="product">
                <th mat-header-cell *matHeaderCellDef>Product</th>
                <td mat-cell *matCellDef="let l">
                  <div>{{ l.productName }}</div>
                  <small><code>{{ l.productCode }}</code> · {{ l.grade }}</small>
                </td>
              </ng-container>

              <ng-container matColumnDef="quantity">
                <th mat-header-cell *matHeaderCellDef class="num">On hand</th>
                <td mat-cell *matCellDef="let l" class="num figure">{{ qty(l.quantity) }}</td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="lowStockColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: lowStockColumns"></tr>
            </table>
          }
        </article>
      </section>

      <p class="asof">
        Figures as at {{ generatedAt() }}. The server caches this for a minute, so it is
        not second-by-second live.
      </p>
    }
  `,
  styles: `
    h1 { font-size: 1.5rem; margin: 0 0 .25rem; }
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .head__sub { opacity: .7; margin: 0 0 1.25rem; }

    /* Hero - exactly one per view, in the same sans as everything else, and with
       proportional figures: tabular-nums at this size reads loose. */
    .hero {
      background: #fff; border: 1px solid rgba(0,0,0,.08); border-radius: 12px;
      padding: 1.25rem 1.5rem; margin-bottom: 1rem;
    }
    .hero__label { margin: 0; font-size: .85rem; opacity: .7; }
    .hero__value { margin: .2rem 0 0; font-size: 3rem; font-weight: 600; line-height: 1.1; }
    .hero__value--credit { color: #137333; }
    .hero__note { margin: .2rem 0 0; opacity: .65; font-size: .9rem; }

    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 1rem; }
    .tile {
      background: #fff; border: 1px solid rgba(0,0,0,.08); border-radius: 12px; padding: 1rem 1.15rem;
    }
    .tile__label { margin: 0; font-size: .82rem; opacity: .7; }
    .tile__value { margin: .25rem 0 0; font-size: 1.65rem; font-weight: 600; line-height: 1.15; }
    .tile__note { margin: .3rem 0 0; font-size: .78rem; opacity: .6; }

    /* Status is never colour alone - an icon and words carry it too. */
    .tile__note--status { display: flex; align-items: center; gap: .3rem; opacity: .8; }
    .tile__note--status mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .tile__note--status.is-warning { color: #b26a00; opacity: 1; }

    .panels { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 1rem; margin-top: 1rem; }
    .panel { background: #fff; border: 1px solid rgba(0,0,0,.08); border-radius: 12px; overflow: hidden; }
    .panel__head { display: flex; justify-content: space-between; align-items: center; padding: .85rem 1.15rem .25rem; }
    .panel__head h2 { margin: 0; font-size: 1rem; }
    .panel__empty { padding: 1.5rem; text-align: center; opacity: .6; margin: 0; }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.15rem; }
    /* tabular-nums only in columns, where digits must line up */
    .figure { font-variant-numeric: tabular-nums; font-weight: 500; }
    .credit { color: #137333; font-weight: 400; }
    small { opacity: .6; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .asof { opacity: .6; font-size: .8rem; margin-top: 1rem; }
    .notice { display: flex; gap: .5rem; align-items: center; border-radius: 8px; padding: .75rem 1rem; margin-bottom: 1rem; }
    .notice--warn { background: #fff4e5; border: 1px solid #ffd9a0; color: #663c00; }
  `,
})
export class DashboardPageComponent {
  readonly auth = inject(AuthService);
  private readonly reports = inject(ReportsService);
  private readonly lookups = inject(LookupsService);
  private readonly route = inject(ActivatedRoute);

  readonly debtorColumns = ['name', 'outstanding', 'lastPaymentDate'];
  readonly lowStockColumns = ['product', 'quantity'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly data = signal<DashboardResponse | null>(null);

  readonly factoryName = computed(() => this.lookups.setting('Factory.Name') || 'Crockery Factory');
  readonly lossThreshold = computed(() =>
    this.lookups.settingNumber('Production.LossWarningPercent', 25),
  );
  readonly lossIsHigh = computed(
    () => (this.data()?.lossPercentageThisMonth ?? 0) > this.lossThreshold(),
  );

  readonly generatedAt = computed(() => {
    const parsed = parseUtc(this.data()?.generatedAt);
    return parsed ? parsed.toLocaleString() : '';
  });

  constructor() {
    void this.refresh();
  }

  denied(): boolean { return this.route.snapshot.queryParamMap.has('denied'); }

  money(value: number): string { return money(value); }
  qty(value: number): string { return quantity(value); }

  async refresh(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.data.set(await this.reports.dashboard());
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }
}

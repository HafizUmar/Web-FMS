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
import { I18nService } from '../../core/i18n/i18n.service';

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
        <mat-icon>lock</mat-icon> {{ t('dash.no_access') }}
      </div>
    }

    <header class="head">
      <div>
        <h1>{{ t('dash.welcome', { name: auth.fullName() }) }}</h1>
        <p class="head__sub">{{ factoryName() }} · {{ t('dash.signed_in_as', { roles: auth.roles().join(', ') }) }}</p>
      </div>
      <button matButton (click)="refresh()" [disabled]="loading()">
        <mat-icon>refresh</mat-icon> {{ t('common.refresh') }}
      </button>
    </header>

    <app-error-banner [problem]="error()" />
    @if (loading() && !data()) { <mat-progress-bar mode="indeterminate" /> }

    @if (data(); as d) {
      <!-- Hero: the figure this screen exists for -->
      <section class="hero">
        <span class="hero__icon" aria-hidden="true">
          <mat-icon>receipt_long</mat-icon>
        </span>
        <p class="hero__label">{{ t('dash.outstanding') }}</p>
        <p class="hero__value" [class.hero__value--credit]="d.totalOutstanding < 0">
          @if (d.totalOutstanding < 0) {
            {{ money(-d.totalOutstanding) }}
          } @else {
            {{ money(d.totalOutstanding) }}
          }
        </p>
        <p class="hero__note">
          @if (d.totalOutstanding < 0) {
            {{ t('dash.held_advance', { count: d.customersWithBalance }) }}
          } @else {
            {{ t('dash.owed_by', { count: d.customersWithBalance }) }}
          }
        </p>
      </section>

      <section class="tiles">
        <div class="tile tile--stock">
          <span class="tile__icon" aria-hidden="true"><mat-icon>inventory_2</mat-icon></span>
          <p class="tile__label">{{ t('dash.units_in_stock') }}</p>
          <p class="tile__value">{{ qty(d.totalUnitsInStock) }}</p>
          <p class="tile__note">{{ t('dash.at_current_rates', { value: money(d.stockValue) }) }}</p>
        </div>

        <div class="tile tile--kiln">
          <span class="tile__icon" aria-hidden="true"><mat-icon>local_fire_department</mat-icon></span>
          <p class="tile__label">{{ t('dash.produced') }}</p>
          <p class="tile__value">{{ qty(d.unitsProducedThisMonth) }}</p>
          <p class="tile__note">{{ t('dash.produced_note') }}</p>
        </div>

        <div class="tile" [class.tile--alert]="lossIsHigh()" [class.tile--ok]="!lossIsHigh()">
          <span class="tile__icon" aria-hidden="true"><mat-icon>trending_down</mat-icon></span>
          <p class="tile__label">{{ t('dash.loss') }}</p>
          <p class="tile__value">{{ d.lossPercentageThisMonth }}%</p>
          <p class="tile__note tile__note--status" [class.is-warning]="lossIsHigh()">
            @if (lossIsHigh()) {
              <mat-icon>report_problem</mat-icon> {{ t('dash.above_threshold', { pct: lossThreshold() }) }}
            } @else {
              <mat-icon>check_circle</mat-icon> {{ t('dash.within_threshold', { pct: lossThreshold() }) }}
            }
          </p>
        </div>

        <div class="tile tile--sales">
          <span class="tile__icon" aria-hidden="true"><mat-icon>local_shipping</mat-icon></span>
          <p class="tile__label">{{ t('dash.sales') }}</p>
          <p class="tile__value">{{ money(d.salesThisMonth) }}</p>
          <p class="tile__note">{{ t('dash.sales_note') }}</p>
        </div>

        <div class="tile tile--paid">
          <span class="tile__icon" aria-hidden="true"><mat-icon>payments</mat-icon></span>
          <p class="tile__label">{{ t('dash.payments') }}</p>
          <p class="tile__value">{{ money(d.paymentsThisMonth) }}</p>
          <p class="tile__note">{{ t('dash.payments_note') }}</p>
        </div>
      </section>

      <section class="panels">
        <!-- Top debtors -->
        <article class="panel">
          <header class="panel__head">
            <h2>{{ t('dash.largest_balances') }}</h2>
            <a matButton routerLink="/customers">{{ t('dash.see_all') }}</a>
          </header>

          @if (d.topDebtors.length === 0) {
            <p class="panel__empty">{{ t('dash.nobody_owes') }}</p>
          } @else {
            <table mat-table [dataSource]="d.topDebtors">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>{{ t('dash.customer') }}</th>
                <td mat-cell *matCellDef="let r">
                  <div>{{ r.name }}</div>
                  <small>{{ r.city ?? '' }}</small>
                </td>
              </ng-container>

              <ng-container matColumnDef="outstanding">
                <th mat-header-cell *matHeaderCellDef class="num">{{ t('dash.outstanding_col') }}</th>
                <td mat-cell *matCellDef="let r" class="num figure">
                  @if (r.outstanding < 0) {
                    <span class="credit">{{ t('dash.in_advance', { amount: money(-r.outstanding) }) }}</span>
                  } @else { {{ money(r.outstanding) }} }
                </td>
              </ng-container>

              <ng-container matColumnDef="lastPaymentDate">
                <th mat-header-cell *matHeaderCellDef class="num">{{ t('dash.last_paid') }}</th>
                <td mat-cell *matCellDef="let r" class="num">
                  @if (r.lastPaymentDate) {
                    {{ t('dash.days_ago', { days: r.daysSinceLastPayment }) }}
                  } @else { {{ t('common.never') }} }
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
            <h2>{{ t('dash.running_low') }}</h2>
            <a matButton routerLink="/stock">{{ t('dash.see_all') }}</a>
          </header>

          @if (d.lowStockProducts.length === 0) {
            <p class="panel__empty">{{ t('dash.nothing_low') }}</p>
          } @else {
            <table mat-table [dataSource]="d.lowStockProducts">
              <ng-container matColumnDef="product">
                <th mat-header-cell *matHeaderCellDef>{{ t('dash.product') }}</th>
                <td mat-cell *matCellDef="let l">
                  <div>{{ l.productName }}</div>
                  <small><code>{{ l.productCode }}</code> · {{ gradeLabel(l.grade) }}</small>
                </td>
              </ng-container>

              <ng-container matColumnDef="quantity">
                <th mat-header-cell *matHeaderCellDef class="num">{{ t('dash.on_hand') }}</th>
                <td mat-cell *matCellDef="let l" class="num figure">{{ qty(l.quantity) }}</td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="lowStockColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: lowStockColumns"></tr>
            </table>
          }
        </article>
      </section>

      <p class="asof">{{ t('dash.as_at', { when: generatedAt() }) }}</p>
    }
  `,
  styles: `
    h1 { font-size: 1.6rem; font-weight: 600; letter-spacing: -.02em; margin: 0 0 .2rem; }
    .head { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .head__sub { color: var(--ink-3); margin: 0 0 1.5rem; }

    /*
      Hero - exactly one per view. The gradient is what makes it read as the headline
      of the screen rather than the first of six equal cards; the figure itself stays in
      the same sans as everything else, with proportional digits, because tabular-nums
      at 3rem reads loose.
    */
    .hero {
      position: relative;
      overflow: hidden;
      background: var(--chrome);
      color: #fff;
      border-radius: var(--radius);
      padding: 1.6rem 1.75rem;
      margin-bottom: 1rem;
      box-shadow: var(--shadow-2);
    }

    .hero::after {
      content: '';
      position: absolute;
      top: -160px; right: -110px;
      width: 400px; height: 400px;
      border-radius: 50%;
      background: radial-gradient(circle, rgba(240, 140, 26, .34), transparent 62%);
      pointer-events: none;
    }

    .hero > * { position: relative; z-index: 1; }

    .hero__icon {
      display: grid;
      place-items: center;
      width: 40px; height: 40px;
      border-radius: 12px;
      margin-bottom: .7rem;
      background: rgba(255, 255, 255, .16);
      border: 1px solid rgba(255, 255, 255, .22);
      color: var(--ember-300);
    }

    .hero__label { margin: 0; font-size: .85rem; opacity: .82; letter-spacing: .01em; }
    .hero__value { margin: .15rem 0 0; font-size: 3.1rem; font-weight: 600; line-height: 1.05; letter-spacing: -.03em; }
    .hero__value--credit { color: #8ff0bb; }
    .hero__note { margin: .3rem 0 0; opacity: .78; font-size: .9rem; }

    /* ---- Tiles ---- */
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 1rem; }

    .tile {
      position: relative;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--radius);
      padding: 1.15rem 1.25rem;
      box-shadow: var(--shadow-1);
      transition: transform .14s ease, box-shadow .14s ease;
      --tile-accent: var(--brand-500);
      --tile-accent-bg: var(--brand-50);
    }

    .tile:hover { transform: translateY(-2px); box-shadow: var(--shadow-2); }

    /* A hairline of the tile's own colour along the top edge. Enough to tell the five
       apart at a glance; not enough to compete with the figures. */
    .tile::before {
      content: '';
      position: absolute;
      inset: 0 0 auto;
      height: 3px;
      border-radius: var(--radius) var(--radius) 0 0;
      background: var(--tile-accent);
      opacity: .85;
    }

    .tile__icon {
      display: grid;
      place-items: center;
      width: 34px; height: 34px;
      border-radius: 10px;
      margin-bottom: .55rem;
      background: var(--tile-accent-bg);
      color: var(--tile-accent);
    }
    .tile__icon mat-icon { font-size: 19px; width: 19px; height: 19px; }

    .tile--stock { --tile-accent: #2f7ad6; --tile-accent-bg: var(--info-bg); }
    .tile--kiln  { --tile-accent: var(--ember-600); --tile-accent-bg: var(--ember-100); }
    .tile--sales { --tile-accent: var(--brand-500); --tile-accent-bg: var(--brand-50); }
    .tile--paid  { --tile-accent: #0d8f92; --tile-accent-bg: #e2f6f6; }
    .tile--ok    { --tile-accent: #17916a; --tile-accent-bg: var(--ok-bg); }
    .tile--alert { --tile-accent: var(--warn-ink); --tile-accent-bg: var(--warn-bg); }

    .tile__label { margin: 0; font-size: .8rem; color: var(--ink-3); }
    .tile__value {
      margin: .2rem 0 0;
      font-size: 1.7rem;
      font-weight: 600;
      line-height: 1.15;
      letter-spacing: -.02em;
      color: var(--ink);
    }
    .tile__note { margin: .35rem 0 0; font-size: .78rem; color: var(--ink-3); }

    /* Status is never colour alone - an icon and words carry it too. */
    .tile__note--status { display: flex; align-items: center; gap: .3rem; }
    .tile__note--status mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .tile__note--status.is-warning { color: var(--warn-ink); font-weight: 500; }

    /* ---- Panels ---- */
    .panels { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: 1rem; margin-top: 1rem; }

    .panel {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--radius);
      box-shadow: var(--shadow-1);
      overflow: hidden;
    }

    .panel__head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: .9rem 1.15rem;
      border-bottom: 1px solid var(--line);
    }
    .panel__head h2 { margin: 0; font-size: .95rem; font-weight: 600; color: var(--ink); }
    .panel__empty { padding: 2rem 1.5rem; text-align: center; color: var(--ink-3); margin: 0; }

    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.15rem; }
    /* tabular-nums only in columns, where digits must line up */
    .credit { color: var(--ok-ink); font-weight: 500; }
    small { color: var(--ink-3); }
    .asof { color: var(--ink-3); font-size: .8rem; margin-top: 1.25rem; }

    .notice {
      display: flex; gap: .5rem; align-items: center;
      border-radius: var(--radius-sm); padding: .75rem 1rem; margin-bottom: 1rem;
    }
    .notice--warn { background: var(--warn-bg); border: 1px solid var(--warn-line); color: var(--warn-ink); }
  `,
})
export class DashboardPageComponent {
  protected readonly t = inject(I18nService).t;
  protected readonly gradeLabel = inject(I18nService).grade;
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

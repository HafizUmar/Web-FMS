import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';
import { PayrollPreview, PayrollRun } from '../../core/api.types';
import { StaffService, weekContaining } from '../../core/staff.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, quantity, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { CancelDialogComponent } from '../production/cancel-dialog';

/**
 * A week of attendance turned into what each worker is owed.
 *
 * The preview is the whole screen: the same calculation the server will save, shown before
 * anything is written, so what the owner approves is what the worker is handed. Creating
 * the run freezes it.
 */
@Component({
  selector: 'app-payroll-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatFormFieldModule, MatInputModule,
    MatDatepickerModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>{{ t('pr.title') }}</h1>
        <p class="page__sub">{{ t('pr.subtitle') }}</p>
      </div>

      <button
        matButton="filled"
        color="primary"
        (click)="create()"
        [disabled]="busy() || !canCreate()">
        <mat-icon>payments</mat-icon>
        {{ busy() ? t('common.saving') : t('pr.run') }}
      </button>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>{{ t('common.from') }}</mat-label>
        <input matInput [matDatepicker]="fromPicker" [formControl]="from" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="fromPicker" />
        <mat-datepicker #fromPicker />
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>{{ t('common.to') }}</mat-label>
        <input matInput [matDatepicker]="toPicker" [formControl]="to" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="toPicker" />
        <mat-datepicker #toPicker />
      </mat-form-field>

      <button matButton (click)="setWeek(0)">{{ t('pr.this_week') }}</button>
      <button matButton (click)="setWeek(-7)">{{ t('pr.last_week') }}</button>
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    @if (preview(); as p) {
      @if (p.existingRunNumber) {
        <div class="notice notice--warn" role="alert">
          <mat-icon>info</mat-icon> {{ t('pr.already_run') }}
          <code class="ltr">{{ p.existingRunNumber }}</code>
        </div>
      } @else if (p.lines.length > 0) {
        <div class="notice notice--info" role="status">
          <mat-icon>visibility</mat-icon> {{ t('pr.preview_note') }}
        </div>
      }

      <div class="table-wrap">
        <table mat-table [dataSource]="p.lines">
          <ng-container matColumnDef="name">
            <th mat-header-cell *matHeaderCellDef>{{ t('pr.employee') }}</th>
            <td mat-cell *matCellDef="let l">
              {{ l.name }} <small class="muted ltr">{{ l.code }}</small>
            </td>
            <td mat-footer-cell *matFooterCellDef>
              <strong>{{ t('pr.workers', { count: p.employeeCount }) }}</strong>
            </td>
          </ng-container>

          <ng-container matColumnDef="fullDays">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.full_days') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">{{ qty(l.fullDays) }}</td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="halfDays">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.half_days') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">{{ l.halfDays || '—' }}</td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="overtimeHours">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.ot_hours') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">{{ l.overtimeHours || '—' }}</td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="dailyRate">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.rate') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">{{ money(l.dailyRate) }}</td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="wageAmount">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.wage_amount') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">{{ money(l.wageAmount) }}</td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="overtimeAmount">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.ot_amount') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure">
              {{ l.overtimeAmount ? money(l.overtimeAmount) : '—' }}
            </td>
            <td mat-footer-cell *matFooterCellDef></td>
          </ng-container>

          <ng-container matColumnDef="netAmount">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('pr.net') }}</th>
            <td mat-cell *matCellDef="let l" class="num figure closing">{{ money(l.netAmount) }}</td>
            <td mat-footer-cell *matFooterCellDef class="num figure closing">
              {{ money(p.totalAmount) }}
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
          <tr mat-footer-row *matFooterRowDef="columns" class="footer-row"></tr>
        </table>

        @if (p.lines.length === 0 && !loading()) {
          <p class="empty">{{ t('pr.empty_preview') }}</p>
        }
      </div>

      <p class="asof">{{ t('pr.snapshot_note') }}</p>
    }

    @if (runs().length > 0) {
      <h2 class="runs__title">{{ t('pr.runs') }}</h2>
      <div class="table-wrap">
        <table mat-table [dataSource]="runs()">
          <ng-container matColumnDef="runNumber">
            <th mat-header-cell *matHeaderCellDef>{{ t('pr.number') }}</th>
            <td mat-cell *matCellDef="let r"><code class="ltr">{{ r.runNumber }}</code></td>
          </ng-container>

          <ng-container matColumnDef="period">
            <th mat-header-cell *matHeaderCellDef>{{ t('pr.week') }}</th>
            <td mat-cell *matCellDef="let r" class="ltr">{{ r.periodStart }} → {{ r.periodEnd }}</td>
          </ng-container>

          <ng-container matColumnDef="employeeCount">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('emp.title') }}</th>
            <td mat-cell *matCellDef="let r" class="num figure">{{ qty(r.employeeCount) }}</td>
          </ng-container>

          <ng-container matColumnDef="totalAmount">
            <th mat-header-cell *matHeaderCellDef class="num">{{ t('common.total') }}</th>
            <td mat-cell *matCellDef="let r" class="num figure closing">{{ money(r.totalAmount) }}</td>
          </ng-container>

          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef>{{ t('common.status') }}</th>
            <td mat-cell *matCellDef="let r">
              <span class="status" [class.status--off]="r.status === 'Cancelled'">
                <mat-icon class="inline">{{ r.status === 'Cancelled' ? 'block' : 'check_circle' }}</mat-icon>
                {{ r.status === 'Cancelled' ? t('common.cancelled') : t('common.active') }}
              </span>
            </td>
          </ng-container>

          <ng-container matColumnDef="actions">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let r" class="actions">
              @if (r.status !== 'Cancelled') {
                <button matIconButton (click)="cancel(r)" [matTooltip]="t('pr.cancel')">
                  <mat-icon>undo</mat-icon>
                </button>
              }
            </td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="runColumns"></tr>
          <tr mat-row *matRowDef="let row; columns: runColumns"
              [class.row--off]="row.status === 'Cancelled'"></tr>
        </table>
      </div>
    }
  `,
  styles: `
    .filters { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; margin-bottom: .75rem; }
    .filters__date { width: 180px; }
    table { width: 100%; }
    .num { text-align: end; }
    td.num, th.num { padding-inline-end: 1.25rem; }
    .closing { font-weight: 500; }
    .footer-row { background: var(--surface-sunken); }
    .footer-row td { font-weight: 500; border-top: 2px solid var(--line-strong); }
    .notice {
      display: flex; gap: .5rem; align-items: center;
      border-radius: var(--radius-sm); padding: .7rem 1rem; margin-bottom: .9rem;
    }
    .notice--warn { background: var(--warn-bg); border: 1px solid var(--warn-line); color: var(--warn-ink); }
    .notice--info { background: var(--info-bg); border: 1px solid var(--info-line); color: var(--info-ink); }
    .asof { margin: .75rem 0 0; color: var(--ink-3); font-size: .85rem; max-width: 78ch; }
    .runs__title { margin: 2rem 0 .75rem; font-size: 1.1rem; }
    .row--off { opacity: .6; }
    .actions { text-align: end; }
    .muted { color: var(--ink-3); }
  `,
})
export class PayrollPageComponent {
  private readonly staff = inject(StaffService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly t = inject(I18nService).t;

  readonly columns = [
    'name', 'fullDays', 'halfDays', 'overtimeHours', 'dailyRate',
    'wageAmount', 'overtimeAmount', 'netAmount',
  ];
  readonly runColumns = ['runNumber', 'period', 'employeeCount', 'totalAmount', 'status', 'actions'];

  readonly today = new Date();
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly preview = signal<PayrollPreview | null>(null);
  readonly runs = signal<PayrollRun[]>([]);

  private readonly initial = weekContaining(new Date());
  readonly from = new FormControl<Date>(new Date(this.initial.start), { nonNullable: true });
  readonly to = new FormControl<Date>(new Date(this.initial.end), { nonNullable: true });

  readonly canCreate = computed(() => {
    const p = this.preview();
    return !!p && p.lines.length > 0 && !p.existingRunNumber;
  });

  constructor() {
    merge(this.from.valueChanges, this.to.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.load());

    void this.load();
    void this.loadRuns();
  }

  money(value: number): string { return money(value); }
  qty(value: number): string { return quantity(value); }

  /** Jump the whole period, rather than making somebody pick two dates by hand. */
  setWeek(offsetDays: number): void {
    const anchor = new Date();
    anchor.setDate(anchor.getDate() + offsetDays);

    const week = weekContaining(anchor);

    this.from.setValue(new Date(week.start), { emitEvent: false });
    this.to.setValue(new Date(week.end), { emitEvent: false });

    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.preview.set(await this.staff.preview(
        toIsoDate(this.from.value) ?? undefined,
        toIsoDate(this.to.value) ?? undefined,
      ));
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadRuns(): Promise<void> {
    try {
      this.runs.set(await this.staff.payrollRuns(20));
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }

  async create(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);

    try {
      await this.staff.createPayroll(
        toIsoDate(this.from.value) ?? '', toIsoDate(this.to.value) ?? '');

      this.snackBar.open(this.t('pr.created'), this.t('common.close'), { duration: 5000 });
      await this.load();
      await this.loadRuns();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.busy.set(false);
    }
  }

  async cancel(run: PayrollRun): Promise<void> {
    const ref = this.dialog.open(CancelDialogComponent, {
      data: { documentNumber: run.runNumber, documentKind: this.t('pr.title') },
    });

    const reason = await ref.afterClosed().toPromise();
    if (!reason) return;

    try {
      await this.staff.cancelPayroll(run.id, reason);
      await this.load();
      await this.loadRuns();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }
}

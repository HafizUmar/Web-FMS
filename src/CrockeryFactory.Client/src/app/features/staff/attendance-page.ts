import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AttendanceSheet, AttendanceStatus } from '../../core/api.types';
import { StaffService, MarkLine } from '../../core/staff.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, quantity, todayIso, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/** The row as the clerk is editing it, before anything is sent. */
interface Draft {
  employeeId: string;
  code: string;
  name: string;
  designation?: string;
  dailyRate?: number;
  status: AttendanceStatus | null;
  overtimeHours: number;
}

/**
 * The day's sheet.
 *
 * Built for the thirty seconds it actually gets. A clerk stands at the gate with a crew in
 * front of them, so the whole roll is on screen at once, "everyone present" fills it in one
 * tap, and the exceptions are then corrected by hand. Nothing is saved until Save, so a
 * mis-tap costs nothing.
 */
@Component({
  selector: 'app-attendance-page',
  imports: [
    ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatDatepickerModule,
    MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressBarModule,
    ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>{{ t('att.title') }}</h1>
        <p class="page__sub">{{ t('att.subtitle') }}</p>
      </div>

      <div class="head-actions">
        <button matButton (click)="markAllPresent()" [disabled]="drafts().length === 0">
          <mat-icon>done_all</mat-icon> {{ t('att.mark_all_present') }}
        </button>
        <button
          matButton="filled"
          color="primary"
          (click)="save()"
          [disabled]="busy() || !dirty()">
          <mat-icon>save</mat-icon>
          {{ busy() ? t('common.saving') : t('att.save_sheet') }}
        </button>
      </div>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>{{ t('common.date') }}</mat-label>
        <input matInput [matDatepicker]="picker" [formControl]="date" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="picker" />
        <mat-datepicker #picker />
      </mat-form-field>

      @if (sheet(); as s) {
        <p class="summary" role="status">
          {{ t('att.summary', { present: s.presentCount, half: s.halfDayCount, absent: s.absentCount }) }}
        </p>
      }

      <span class="filters__spacer"></span>

      @if (total() > 0) {
        <p class="day-total">
          <span class="day-total__label">{{ t('att.day_total') }}</span>
          <strong class="figure">{{ money(total()) }}</strong>
        </p>
      }
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      @if (drafts().length === 0 && !loading()) {
        <p class="empty">{{ t('att.empty') }}</p>
      } @else {
        <table class="sheet">
          <thead>
            <tr>
              <th>{{ t('emp.code') }}</th>
              <th>{{ t('emp.name') }}</th>
              <th class="num">{{ t('emp.daily_rate') }}</th>
              <th class="status-col">{{ t('common.status') }}</th>
              <th class="num ot-col">{{ t('att.overtime_hours') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of drafts(); track row.employeeId) {
              <tr [class.is-unmarked]="row.status === null">
                <td><code>{{ row.code }}</code></td>
                <td>
                  {{ row.name }}
                  @if (row.designation) { <small class="muted"> · {{ row.designation }}</small> }
                </td>
                <td class="num figure">{{ row.dailyRate ? money(row.dailyRate) : '—' }}</td>
                <td class="status-col">
                  <!--
                    Three buttons rather than a dropdown: marking a crew is the one thing
                    this screen does, and a select costs two taps and a hidden list where
                    a toggle costs one and shows the answer.
                  -->
                  <mat-button-toggle-group
                    [value]="row.status"
                    (change)="setStatus(row.employeeId, $event.value)"
                    hideSingleSelectionIndicator>
                    <mat-button-toggle value="Present" class="tog tog--present">
                      {{ t('att.present') }}
                    </mat-button-toggle>
                    <mat-button-toggle value="HalfDay" class="tog tog--half">
                      {{ t('att.half_day') }}
                    </mat-button-toggle>
                    <mat-button-toggle value="Absent" class="tog tog--absent">
                      {{ t('att.absent') }}
                    </mat-button-toggle>
                  </mat-button-toggle-group>
                </td>
                <td class="num ot-col">
                  <input
                    class="ot"
                    type="number"
                    min="0"
                    max="16"
                    step="0.5"
                    [value]="row.overtimeHours"
                    (input)="setOvertime(row.employeeId, $any($event.target).value)"
                    [attr.aria-label]="t('att.overtime_hours') + ' ' + row.name" />
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: `
    .head-actions { display: flex; gap: .6rem; }
    .filters { display: flex; gap: 1.25rem; align-items: center; flex-wrap: wrap; margin-bottom: .75rem; }
    .filters__date { width: 200px; }
    .filters__spacer { flex: 1 1 auto; }
    .summary { margin: 0; color: var(--ink-3); }

    .day-total { display: flex; align-items: baseline; gap: .6rem; margin: 0; }
    .day-total__label { color: var(--ink-3); font-size: .85rem; }
    .day-total strong { font-size: 1.15rem; }

    table.sheet { width: 100%; border-collapse: collapse; }
    .sheet thead th {
      text-align: start;
      padding: .7rem 1rem;
      background: var(--surface-sunken);
      border-bottom: 1px solid var(--line);
      color: var(--ink-3);
      font-size: .74rem;
      font-weight: 600;
      letter-spacing: .06em;
      text-transform: uppercase;
    }
    .sheet tbody td { padding: .5rem 1rem; border-bottom: 1px solid var(--line); color: var(--ink-2); }
    .sheet tbody tr:hover { background: var(--surface-hover); }

    /* Not yet marked is a state worth seeing at a glance - it is the row the clerk has
       not reached yet, not a worker who was absent. */
    .is-unmarked { background: var(--warn-bg); }
    .is-unmarked:hover { background: var(--warn-bg); }

    .status-col { width: 320px; }
    .ot-col { width: 130px; }
    .num { text-align: end; }

    .ot {
      width: 76px;
      padding: .35rem .5rem;
      border: 1px solid var(--line-strong);
      border-radius: 8px;
      background: var(--surface);
      color: var(--ink);
      text-align: end;
      font: inherit;
      font-variant-numeric: tabular-nums;
    }

    .tog { font-size: .8rem; }
    .muted { color: var(--ink-3); }
  `,
})
export class AttendancePageComponent {
  private readonly staff = inject(StaffService);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly t = inject(I18nService).t;

  readonly today = new Date();
  readonly date = new FormControl<Date>(new Date(), { nonNullable: true });

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly sheet = signal<AttendanceSheet | null>(null);
  readonly drafts = signal<Draft[]>([]);

  /** Set once the clerk changes anything, so Save cannot post an untouched sheet. */
  readonly dirty = signal(false);

  /**
   * What the day costs, from the rows as they stand. Shown live because it is the number
   * the owner asks about, and seeing it move as the sheet is filled catches a mis-tap on
   * a rate that a total at the end of the week would not.
   */
  readonly total = computed(() =>
    this.drafts().reduce((sum, row) => {
      const rate = row.dailyRate ?? 0;
      const day = row.status === 'Present' ? rate : row.status === 'HalfDay' ? rate / 2 : 0;

      return sum + day;
    }, 0),
  );

  constructor() {
    this.date.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => void this.load());
    void this.load();
  }

  money(value: number): string { return money(value); }
  qty(value: number): string { return quantity(value); }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const sheet = await this.staff.sheet(toIsoDate(this.date.value) ?? todayIso());

      this.sheet.set(sheet);
      this.drafts.set(sheet.lines.map((line) => ({
        employeeId: line.employeeId,
        code: line.code,
        name: line.name,
        designation: line.designation,
        dailyRate: line.dailyRate,
        status: line.status ?? null,
        overtimeHours: line.overtimeHours,
      })));
      this.dirty.set(false);
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  setStatus(employeeId: string, status: AttendanceStatus): void {
    this.drafts.update((rows) =>
      rows.map((row) => (row.employeeId === employeeId ? { ...row, status } : row)));
    this.dirty.set(true);
  }

  setOvertime(employeeId: string, raw: string): void {
    const hours = Math.max(0, Math.min(16, Number(raw) || 0));

    this.drafts.update((rows) =>
      rows.map((row) => (row.employeeId === employeeId ? { ...row, overtimeHours: hours } : row)));
    this.dirty.set(true);
  }

  /** The common case by a wide margin: most days most of the crew turns up. */
  markAllPresent(): void {
    this.drafts.update((rows) => rows.map((row) => ({ ...row, status: 'Present' as const })));
    this.dirty.set(true);
  }

  async save(): Promise<void> {
    const lines: MarkLine[] = this.drafts()
      // An unmarked row is not an absence. Sending it as one would record a day off for
      // somebody the clerk simply had not reached yet.
      .filter((row): row is Draft & { status: AttendanceStatus } => row.status !== null)
      .map((row) => ({
        employeeId: row.employeeId,
        status: row.status,
        overtimeHours: row.overtimeHours,
      }));

    if (lines.length === 0) return;

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.staff.mark(toIsoDate(this.date.value) ?? todayIso(), lines);
      this.snackBar.open(this.t('att.saved'), this.t('common.close'), { duration: 4000 });
      await this.load();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.busy.set(false);
    }
  }
}

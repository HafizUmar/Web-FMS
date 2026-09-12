import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Employee } from '../../core/api.types';
import { StaffService } from '../../core/staff.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, todayIso } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog';
import { EmployeeFormComponent } from './employee-form';
import { WageRateDialogComponent } from './wage-rate-dialog';

@Component({
  selector: 'app-employees-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatCheckboxModule, MatButtonModule,
    MatIconModule, MatTooltipModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>{{ t('emp.title') }}</h1>
        <p class="page__sub">{{ t('emp.subtitle') }}</p>
      </div>

      <button matButton="filled" color="primary" (click)="create()">
        <mat-icon>person_add</mat-icon> {{ t('emp.new') }}
      </button>
    </header>

    <div class="filters">
      <mat-checkbox [formControl]="showLeft">{{ t('emp.show_left') }}</mat-checkbox>
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="code">
          <th mat-header-cell *matHeaderCellDef>{{ t('emp.code') }}</th>
          <td mat-cell *matCellDef="let e"><code>{{ e.code }}</code></td>
        </ng-container>

        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>{{ t('emp.name') }}</th>
          <td mat-cell *matCellDef="let e">
            {{ e.name }}
            @if (e.fatherName) { <small class="muted"> · {{ e.fatherName }}</small> }
          </td>
        </ng-container>

        <ng-container matColumnDef="designation">
          <th mat-header-cell *matHeaderCellDef>{{ t('emp.designation') }}</th>
          <td mat-cell *matCellDef="let e">{{ e.designation || '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="phone">
          <th mat-header-cell *matHeaderCellDef>{{ t('emp.phone') }}</th>
          <td mat-cell *matCellDef="let e">{{ e.phone || '—' }}</td>
        </ng-container>

        <ng-container matColumnDef="dailyRate">
          <th mat-header-cell *matHeaderCellDef class="num">{{ t('emp.daily_rate') }}</th>
          <td mat-cell *matCellDef="let e" class="num figure">
            {{ e.currentDailyRate ? money(e.currentDailyRate) : '—' }}
          </td>
        </ng-container>

        <ng-container matColumnDef="joinedOn">
          <th mat-header-cell *matHeaderCellDef>{{ t('emp.joined') }}</th>
          <td mat-cell *matCellDef="let e" class="ltr">{{ e.joinedOn }}</td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>{{ t('common.status') }}</th>
          <td mat-cell *matCellDef="let e">
            <span class="status" [class.status--off]="!e.isActive">
              <mat-icon class="inline">{{ e.isActive ? 'check_circle' : 'logout' }}</mat-icon>
              {{ e.isActive ? t('emp.active') : t('emp.inactive') }}
            </span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let e" class="actions">
            <button matIconButton (click)="edit(e)" [matTooltip]="t('common.edit')">
              <mat-icon>edit</mat-icon>
            </button>
            <button matIconButton (click)="changeWage(e)" [matTooltip]="t('emp.new_rate')">
              <mat-icon>payments</mat-icon>
            </button>
            @if (e.isActive) {
              <button matIconButton (click)="markLeft(e)" [matTooltip]="t('emp.deactivate')">
                <mat-icon>person_off</mat-icon>
              </button>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="!row.isActive"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">{{ t('emp.empty') }}</p>
      }
    </div>
  `,
  styles: `
    .filters { margin-bottom: .75rem; }
    table { width: 100%; }
    .num { text-align: end; padding-inline-end: 1.25rem; }
    .row--off { opacity: .6; }
    .actions { text-align: end; white-space: nowrap; }
    .muted { color: var(--ink-3); }
  `,
})
export class EmployeesPageComponent {
  private readonly staff = inject(StaffService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly t = inject(I18nService).t;

  readonly columns = ['code', 'name', 'designation', 'phone', 'dailyRate', 'joinedOn', 'status', 'actions'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly all = signal<Employee[]>([]);

  readonly showLeft = new FormControl(false, { nonNullable: true });
  private readonly includeLeft = signal(false);

  readonly rows = computed(() =>
    this.includeLeft() ? this.all() : this.all().filter((e) => e.isActive));

  constructor() {
    this.showLeft.valueChanges.pipe(takeUntilDestroyed()).subscribe((v) => this.includeLeft.set(v));
    void this.load();
  }

  money(value: number): string { return money(value); }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      // Everyone including those who left, filtered in the browser: a factory roll is
      // tens of rows, and a round trip per checkbox would be slower than the filter.
      this.all.set(await this.staff.employees(true));
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> { await this.openForm(null); }
  async edit(employee: Employee): Promise<void> { await this.openForm(employee); }

  private async openForm(existing: Employee | null): Promise<void> {
    const ref = this.dialog.open(EmployeeFormComponent, { data: { existing } });

    if ((await ref.afterClosed().toPromise()) === true) await this.load();
  }

  async changeWage(employee: Employee): Promise<void> {
    const ref = this.dialog.open(WageRateDialogComponent, { data: { employee } });

    if ((await ref.afterClosed().toPromise()) === true) await this.load();
  }

  async markLeft(employee: Employee): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: this.t('emp.deactivate'),
        message: `${employee.name} — ${this.t('emp.left_warning')}`,
        confirmLabel: this.t('emp.deactivate'),
        destructive: true,
      },
    });

    if ((await ref.afterClosed().toPromise()) !== true) return;

    try {
      await this.staff.markAsLeft(employee.id, todayIso());
      this.snackBar.open(this.t('emp.left_done'), this.t('common.close'), { duration: 4000 });
      await this.load();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }
}

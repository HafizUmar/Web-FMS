import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReasonCode, ReasonCodeType } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { humanise } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog';
import { ReasonCodeFormComponent } from './reason-code-form';

const TYPES: ReasonCodeType[] = [
  'Breakage', 'StockAdjustment', 'SalesReturn', 'DispatchCancellation',
];

/**
 * The codes a clerk picks from when something is broken, adjusted or cancelled.
 *
 * The code itself is never editable. It is written into every movement that cited it, and
 * changing it here would silently rewrite the meaning of years of history; only the
 * wording a clerk reads and the order they appear in can change.
 */
@Component({
  selector: 'app-reason-codes-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatButtonModule, MatIconModule,
    MatTooltipModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Reason codes</h1>
        <p class="page__sub">
          What a clerk chooses from when pieces break, stock is corrected or a document is
          cancelled.
        </p>
      </div>

      <button matButton="filled" color="primary" (click)="create()">
        <mat-icon>add</mat-icon> New reason
      </button>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__type">
        <mat-label>Used for</mat-label>
        <mat-select [formControl]="type">
          <mat-option [value]="null">All</mat-option>
          @for (t of types; track t) { <mat-option [value]="t">{{ label(t) }}</mat-option> }
        </mat-select>
      </mat-form-field>

      <mat-checkbox [formControl]="showInactive">Show retired codes</mat-checkbox>
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="type">
          <th mat-header-cell *matHeaderCellDef>Used for</th>
          <td mat-cell *matCellDef="let r">{{ label(r.type) }}</td>
        </ng-container>

        <ng-container matColumnDef="code">
          <th mat-header-cell *matHeaderCellDef>Code</th>
          <td mat-cell *matCellDef="let r"><code>{{ r.code }}</code></td>
        </ng-container>

        <ng-container matColumnDef="description">
          <th mat-header-cell *matHeaderCellDef>What the clerk sees</th>
          <td mat-cell *matCellDef="let r">{{ r.description }}</td>
        </ng-container>

        <ng-container matColumnDef="sortOrder">
          <th mat-header-cell *matHeaderCellDef class="num">Order</th>
          <td mat-cell *matCellDef="let r" class="num figure">{{ r.sortOrder }}</td>
        </ng-container>

        <ng-container matColumnDef="isActive">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let r">
            <span class="status" [class.status--off]="!r.isActive">
              <mat-icon class="inline">{{ r.isActive ? 'check_circle' : 'block' }}</mat-icon>
              {{ r.isActive ? 'In use' : 'Retired' }}
            </span>
          </td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let r" class="actions">
            <button matIconButton (click)="edit(r)" matTooltip="Edit wording and order">
              <mat-icon>edit</mat-icon>
            </button>
            @if (r.isActive) {
              <button matIconButton (click)="retire(r)" matTooltip="Retire">
                <mat-icon>archive</mat-icon>
              </button>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [class.row--off]="!row.isActive"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">No reason codes match these filters.</p>
      }
    </div>

    <p class="foot">
      A retired code disappears from the pickers but stays readable on every document that
      already cites it.
    </p>
  `,
  styles: `
    .page__header { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; max-width: 70ch; }
    .filters { display: flex; gap: 1.5rem; align-items: center; margin-bottom: .75rem; }
    .filters__type { width: 220px; }
    .table-wrap { background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); overflow-x: auto; }
    table { width: 100%; }
    .num { text-align: right; padding-right: 1.25rem; }
    .figure { font-variant-numeric: tabular-nums; }
    .status { display: inline-flex; align-items: center; gap: .3rem; }
    .status--off { color: #8a4b00; }
    mat-icon.inline { font-size: 18px; width: 18px; height: 18px; }
    .row--off { opacity: .6; }
    .actions { text-align: right; white-space: nowrap; width: 110px; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .foot { margin: .75rem 0 0; opacity: .65; font-size: .875rem; max-width: 70ch; }
  `,
})
export class ReasonCodesPageComponent {
  private readonly admin = inject(AdminService);
  private readonly lookups = inject(LookupsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly types = TYPES;
  readonly columns = ['type', 'code', 'description', 'sortOrder', 'isActive', 'actions'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly all = signal<ReasonCode[]>([]);

  readonly type = new FormControl<ReasonCodeType | null>(null);
  readonly showInactive = new FormControl(false, { nonNullable: true });

  private readonly typeValue = signal<ReasonCodeType | null>(null);
  private readonly inactiveValue = signal(false);

  readonly rows = computed(() => {
    const type = this.typeValue();
    const includeInactive = this.inactiveValue();

    return this.all()
      .filter((r) => (type === null || r.type === type) && (includeInactive || r.isActive))
      .sort((a, b) =>
        a.type.localeCompare(b.type) || a.sortOrder - b.sortOrder || a.code.localeCompare(b.code));
  });

  constructor() {
    this.type.valueChanges.pipe(takeUntilDestroyed()).subscribe((v) => this.typeValue.set(v));
    this.showInactive.valueChanges.pipe(takeUntilDestroyed()).subscribe((v) => this.inactiveValue.set(v));

    void this.load();
  }

  label(type: ReasonCodeType): string {
    return humanise(type);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      // Fetched once with everything, then filtered in the browser: there are eighteen of
      // these, and a round trip per checkbox would be slower than the filter.
      this.all.set(await this.admin.reasonCodes(undefined, true));
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  async create(): Promise<void> {
    await this.openForm(null, 'Reason code created.');
  }

  async edit(reason: ReasonCode): Promise<void> {
    await this.openForm(reason, 'Reason code updated.');
  }

  private async openForm(existing: ReasonCode | null, done: string): Promise<void> {
    const ref = this.dialog.open(ReasonCodeFormComponent, { data: { existing } });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open(done, 'Dismiss', { duration: 4000 });
      await this.load();
      // The pickers on the production and stock screens read these from the cache.
      await this.lookups.reloadReasonCodes();
    }
  }

  async retire(reason: ReasonCode): Promise<void> {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Retire this reason?',
        message:
          `"${reason.description}" will no longer appear when a clerk records a ` +
          `${this.label(reason.type).toLowerCase()}. Everything already recorded against ` +
          'it keeps reading exactly as it does now.',
        confirmLabel: 'Retire',
        destructive: true,
      },
    });

    if ((await ref.afterClosed().toPromise()) !== true) return;

    try {
      await this.admin.deactivateReasonCode(reason.id);
      this.snackBar.open('Reason code retired.', 'Dismiss', { duration: 4000 });
      await this.load();
      await this.lookups.reloadReasonCodes();
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    }
  }
}

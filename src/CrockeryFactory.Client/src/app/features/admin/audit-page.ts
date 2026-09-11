import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';
import { AppUserResponse, AuditEntryResponse, PagedResult } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { ProblemDetails } from '../../core/problem-details';
import { humanise, pageCount, parseUtc, toIsoDate } from '../../core/formatting';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/** One changed field, as it will be read. */
interface FieldChange {
  field: string;
  from: string;
  to: string;
  kind: 'added' | 'removed' | 'changed';
}

const ENTITIES = [
  'Product', 'ProductPrice', 'StockAdjustment', 'ProductionEntry',
  'Customer', 'Dispatch', 'Payment', 'ReasonCode', 'FactorySetting', 'AppUser',
];

/**
 * SE-13. Read-only, and deliberately so: an audit trail somebody can edit is not one.
 *
 * The server stores oldValues/newValues as the JSON it wrote at the time, as a string.
 * Showing that raw would be unreadable, so it is parsed and shown as a field-by-field
 * difference - which is the question anyone opening this screen is actually asking.
 */
@Component({
  selector: 'app-audit-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatPaginatorModule, MatFormFieldModule,
    MatInputModule, MatSelectModule, MatDatepickerModule, MatButtonModule,
    MatIconModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Audit</h1>
        <p class="page__sub">
          Who changed what, and when. Nothing on this screen can be edited or removed.
        </p>
      </div>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__entity">
        <mat-label>Record type</mat-label>
        <mat-select [formControl]="entityName">
          <mat-option [value]="null">All</mat-option>
          @for (e of entities; track e) { <mat-option [value]="e">{{ humanise(e) }}</mat-option> }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__user">
        <mat-label>Changed by</mat-label>
        <mat-select [formControl]="userId">
          <mat-option [value]="null">Anyone</mat-option>
          @for (u of users(); track u.id) { <mat-option [value]="u.id">{{ u.fullName }}</mat-option> }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>From</mat-label>
        <input matInput [matDatepicker]="fromPicker" [formControl]="from" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="fromPicker" />
        <mat-datepicker #fromPicker />
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>To</mat-label>
        <input matInput [matDatepicker]="toPicker" [formControl]="to" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="toPicker" />
        <mat-datepicker #toPicker />
      </mat-form-field>

      <button matButton (click)="clear()">Clear</button>
    </div>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()" multiTemplateDataRows>
        <ng-container matColumnDef="occurredAt">
          <th mat-header-cell *matHeaderCellDef>When</th>
          <td mat-cell *matCellDef="let e">{{ when(e.occurredAt) }}</td>
        </ng-container>

        <ng-container matColumnDef="userName">
          <th mat-header-cell *matHeaderCellDef>Who</th>
          <td mat-cell *matCellDef="let e">{{ e.userName }}</td>
        </ng-container>

        <ng-container matColumnDef="entityName">
          <th mat-header-cell *matHeaderCellDef>Record</th>
          <td mat-cell *matCellDef="let e">{{ humanise(e.entityName) }}</td>
        </ng-container>

        <ng-container matColumnDef="action">
          <th mat-header-cell *matHeaderCellDef>Action</th>
          <td mat-cell *matCellDef="let e">
            <span class="action" [class]="'action--' + e.action.toLowerCase()">
              <mat-icon class="inline">{{ icon(e.action) }}</mat-icon> {{ humanise(e.action) }}
            </span>
          </td>
        </ng-container>

        <ng-container matColumnDef="summary">
          <th mat-header-cell *matHeaderCellDef>What changed</th>
          <td mat-cell *matCellDef="let e">{{ summary(e) }}</td>
        </ng-container>

        <ng-container matColumnDef="expand">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let e" class="actions">
            <button matIconButton (click)="toggle(e)" [attr.aria-label]="'Show detail'">
              <mat-icon>{{ expanded() === e.id ? 'expand_less' : 'expand_more' }}</mat-icon>
            </button>
          </td>
        </ng-container>

        <ng-container matColumnDef="detail">
          <td mat-cell *matCellDef="let e" [attr.colspan]="columns.length" class="detail-cell">
            @if (expanded() === e.id) {
              <div class="detail">
                @if (changes(e).length === 0) {
                  <p class="detail__none">
                    Nothing to compare - this entry records that the action happened, not a
                    field-by-field change.
                  </p>
                } @else {
                  <table class="diff">
                    <tr>
                      <th>Field</th><th>Before</th><th></th><th>After</th>
                    </tr>
                    @for (change of changes(e); track change.field) {
                      <tr>
                        <td class="diff__field">{{ fieldLabel(change.field) }}</td>
                        <td class="diff__from">{{ change.from }}</td>
                        <td class="diff__arrow"><mat-icon class="inline">arrow_forward</mat-icon></td>
                        <td class="diff__to">{{ change.to }}</td>
                      </tr>
                    }
                  </table>
                }
                <p class="detail__id">Record id <code>{{ e.entityId }}</code></p>
              </div>
            }
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
        <tr mat-row *matRowDef="let row; columns: ['detail']" class="detail-row"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">Nothing recorded for these filters.</p>
      }
    </div>

    <mat-paginator
      [length]="result()?.totalCount ?? 0"
      [pageIndex]="(result()?.page ?? 1) - 1"
      [pageSize]="result()?.pageSize ?? 50"
      [pageSizeOptions]="[25, 50, 100, 200]"
      (page)="changePage($event)"
      showFirstLastButtons />

    @if (result(); as r) {
      <p class="summary-line">{{ r.totalCount }} entries · page {{ r.page }} of {{ pages() }}</p>
    }
  `,
  styles: `
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; }
    .filters { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; margin-bottom: .5rem; }
    .filters__entity { width: 200px; }
    .filters__user { width: 200px; }
    .filters__date { width: 170px; }
    .table-wrap { background: #fff; border-radius: 8px; border: 1px solid rgba(0,0,0,.08); overflow-x: auto; }
    table.mat-mdc-table { width: 100%; }
    .action { display: inline-flex; align-items: center; gap: .3rem; }
    .action--delete, .action--deactivate, .action--cancel { color: #8a4b00; }
    mat-icon.inline { font-size: 18px; width: 18px; height: 18px; }
    .actions { text-align: right; width: 56px; }
    /* multiTemplateDataRows renders this row for every entry; a collapsed one must
       take no space, or the table gains a blank line between every record. */
    .detail-row { height: 0; }
    .detail-row td { border-bottom: none; padding: 0; }
    .detail-cell { padding: 0 !important; }
    .detail { padding: .75rem 1.5rem 1.25rem; background: #fafafa; }
    .detail__none { margin: 0; opacity: .7; font-size: .875rem; }
    .detail__id { margin: .75rem 0 0; opacity: .55; font-size: .75rem; }
    .diff { border-collapse: collapse; font-size: .875rem; }
    .diff th { text-align: left; font-weight: 500; opacity: .6; padding: .15rem 1rem .35rem 0; font-size: .75rem;
               text-transform: uppercase; letter-spacing: .04em; }
    .diff td { padding: .2rem 1rem .2rem 0; vertical-align: top; }
    .diff__field { font-weight: 500; white-space: nowrap; }
    .diff__from { color: #8a4b00; text-decoration: line-through; max-width: 34ch; }
    .diff__to { color: #1a6b2f; max-width: 34ch; }
    .diff__arrow { opacity: .4; }
    code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .empty { padding: 2rem; text-align: center; opacity: .6; }
    .summary-line { margin: .25rem 0 0; opacity: .65; font-size: .875rem; }
  `,
})
export class AuditPageComponent {
  private readonly admin = inject(AdminService);

  readonly entities = ENTITIES;
  readonly columns = ['occurredAt', 'userName', 'entityName', 'action', 'summary', 'expand'];

  readonly today = new Date();
  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<PagedResult<AuditEntryResponse> | null>(null);
  readonly users = signal<AppUserResponse[]>([]);
  readonly expanded = signal<string | null>(null);

  readonly entityName = new FormControl<string | null>(null);
  readonly userId = new FormControl<string | null>(null);
  readonly from = new FormControl<Date | null>(null);
  readonly to = new FormControl<Date | null>(null);

  private page = 1;
  private pageSize = 50;

  readonly rows = computed(() => this.result()?.items ?? []);
  readonly pages = computed(() => {
    const r = this.result();
    return r ? pageCount(r) : 1;
  });

  constructor() {
    merge(
      this.entityName.valueChanges, this.userId.valueChanges,
      this.from.valueChanges, this.to.valueChanges,
    ).pipe(takeUntilDestroyed()).subscribe(() => {
      this.page = 1;
      void this.load();
    });

    void this.load();
    void this.loadUsers();
  }

  humanise(value: string): string {
    return humanise(value);
  }

  when(value: string): string {
    const parsed = parseUtc(value);
    return parsed ? parsed.toLocaleString() : '';
  }

  icon(action: string): string {
    switch (action) {
      case 'Create': return 'add_circle';
      case 'Update': return 'edit';
      case 'Cancel': return 'cancel';
      case 'Deactivate': return 'block';
      case 'ResetPassword': return 'key';
      default: return 'history';
    }
  }

  /** A setting key like Production.LossWarningPercent is already its own name. */
  fieldLabel(field: string): string {
    return field.includes('.') ? field : humanise(field);
  }

  /** The one-line version for the row: how many fields, and the first of them. */
  summary(entry: AuditEntryResponse): string {
    const changes = this.changes(entry);

    if (changes.length === 0) return '—';
    if (changes.length === 1) return `${this.fieldLabel(changes[0].field)}: ${changes[0].to || 'cleared'}`;

    return `${this.fieldLabel(changes[0].field)} and ${changes.length - 1} other ` +
      `field${changes.length === 2 ? '' : 's'}`;
  }

  /**
   * Both sides are JSON the server wrote, so they are parsed rather than string-compared.
   * A value the server never wrote is absent from the object, not null - which is why
   * "added" and "removed" are separate from "changed".
   */
  changes(entry: AuditEntryResponse): FieldChange[] {
    // A settings save records one array of {Key, From, To} rather than a before and an
    // after object - it is already a difference. Read as two objects it would come out as
    // "0" and "1" holding raw JSON, which is exactly what this screen exists to avoid.
    const asList = this.asChangeList(entry.newValues);
    if (asList) return asList;

    const before = this.parse(entry.oldValues);
    const after = this.parse(entry.newValues);

    const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])].sort();
    const result: FieldChange[] = [];

    for (const key of keys) {
      const from = this.render(before[key]);
      const to = this.render(after[key]);

      if (from === to) continue;

      result.push({
        field: key,
        from: from || '—',
        to: to || '—',
        kind: !(key in before) ? 'added' : !(key in after) ? 'removed' : 'changed',
      });
    }

    return result;
  }

  private asChangeList(value: string | undefined): FieldChange[] | null {
    if (!value) return null;

    let parsed: unknown;
    try {
      parsed = JSON.parse(value);
    } catch {
      return null;
    }

    if (!Array.isArray(parsed) || parsed.length === 0) return null;

    const rows = parsed as Record<string, unknown>[];
    if (!rows.every((row) => row && typeof row === 'object' && 'Key' in row)) return null;

    return rows.map((row) => ({
      field: String(row['Key']),
      from: this.render(row['From']) || '—',
      to: this.render(row['To']) || '—',
      kind: 'changed' as const,
    }));
  }

  private parse(value: string | undefined): Record<string, unknown> {
    if (!value) return {};

    try {
      const parsed = JSON.parse(value);
      return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : {};
    } catch {
      // Malformed JSON in an audit row is not worth throwing over: the entry still says
      // who did what and when, which is most of the value.
      return {};
    }
  }

  private render(value: unknown): string {
    if (value === undefined || value === null) return '';
    if (Array.isArray(value)) return value.map((v) => this.render(v)).join(', ');
    if (typeof value === 'object') return JSON.stringify(value);

    return String(value);
  }

  toggle(entry: AuditEntryResponse): void {
    this.expanded.update((current) => (current === entry.id ? null : entry.id));
  }

  changePage(event: PageEvent): void {
    this.page = event.pageIndex + 1;
    this.pageSize = event.pageSize;
    void this.load();
  }

  clear(): void {
    this.entityName.setValue(null, { emitEvent: false });
    this.userId.setValue(null, { emitEvent: false });
    this.from.setValue(null, { emitEvent: false });
    this.to.setValue(null, { emitEvent: false });
    this.page = 1;
    void this.load();
  }

  private async loadUsers(): Promise<void> {
    try {
      this.users.set(await this.admin.users());
    } catch {
      // Reading the audit needs CanViewAudit; listing users needs CanManageUsers. An
      // auditor who is not an administrator still gets the log, just without the
      // by-person filter.
      this.users.set([]);
    }
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    this.expanded.set(null);

    try {
      this.result.set(
        await this.admin.audit({
          entityName: this.entityName.value ?? undefined,
          userId: this.userId.value ?? undefined,
          from: toIsoDate(this.from.value) ?? undefined,
          to: toIsoDate(this.to.value) ?? undefined,
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
}

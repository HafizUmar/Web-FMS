import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { QualityGrade, StockLine, StockResponse } from '../../core/api.types';
import { StockService } from '../../core/stock.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { money, quantity, toIsoDate } from '../../core/formatting';
import { HasPermissionDirective } from '../../core/has-permission.directive';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { AdjustmentFormComponent } from './adjustment-form';
import { MovementsDialogComponent } from './movements-dialog';
import { I18nService } from '../../core/i18n/i18n.service';

@Component({
  selector: 'app-stock-page',
  imports: [
    ReactiveFormsModule, MatTableModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatDatepickerModule, MatButtonModule,
    MatIconModule, MatTooltipModule, MatProgressBarModule,
    HasPermissionDirective, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>{{ t('stock.title') }}</h1>
        <p class="page__sub">{{ t('stock.subtitle') }}</p>
      </div>

      <button matButton="filled" color="primary" (click)="adjust()" *appHasPermission="'CanAdjustStock'">
        <mat-icon>tune</mat-icon> {{ t('stock.adjust') }}
      </button>
    </header>

    <div class="filters">
      <mat-form-field appearance="outline" class="filters__search">
        <mat-label>{{ t('common.search') }}</mat-label>
        <input matInput [formControl]="search" placeholder="Code or name" />
        <mat-icon matIconSuffix>search</mat-icon>
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__grade">
        <mat-label>{{ t('stock.grade') }}</mat-label>
        <mat-select [formControl]="grade">
          <mat-option [value]="null">{{ t('stock.all_grades') }}</mat-option>
          @for (g of grades(); track g) { <mat-option [value]="g">{{ gradeLabel(g) }}</mat-option> }
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="filters__date">
        <mat-label>{{ t('stock.as_at') }}</mat-label>
        <input matInput [matDatepicker]="picker" [formControl]="asOf" [max]="today" />
        <mat-datepicker-toggle matIconSuffix [for]="picker" />
        <mat-datepicker #picker />
        <mat-hint>{{ t('stock.blank_live') }}</mat-hint>
      </mat-form-field>

      <mat-checkbox [formControl]="onlyInStock">{{ t('stock.only_in_stock') }}</mat-checkbox>
    </div>

    @if (asOf.value) {
      <div class="historical" role="status">
        <mat-icon>history</mat-icon>
        Showing the position as at {{ asOfLabel() }}, summed from the movement ledger.
        Clear the date for the live figure.
      </div>
    }

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    <div class="table-wrap">
      <table mat-table [dataSource]="rows()">
        <ng-container matColumnDef="productCode">
          <th mat-header-cell *matHeaderCellDef>{{ t('stock.code') }}</th>
          <td mat-cell *matCellDef="let l"><code>{{ l.productCode }}</code></td>
        </ng-container>

        <ng-container matColumnDef="productName">
          <th mat-header-cell *matHeaderCellDef>{{ t('stock.name') }}</th>
          <td mat-cell *matCellDef="let l">{{ l.productName }}</td>
        </ng-container>

        <ng-container matColumnDef="grade">
          <th mat-header-cell *matHeaderCellDef>{{ t('stock.grade') }}</th>
          <td mat-cell *matCellDef="let l">{{ gradeLabel(l.grade) }}</td>
        </ng-container>

        <ng-container matColumnDef="quantity">
          <th mat-header-cell *matHeaderCellDef class="num">{{ t('stock.quantity') }}</th>
          <td mat-cell *matCellDef="let l" class="num qty">{{ qty(l.quantity) }}</td>
        </ng-container>

        <ng-container matColumnDef="unitRate">
          <th mat-header-cell *matHeaderCellDef class="num">{{ t('stock.unit_rate') }}</th>
          <td mat-cell *matCellDef="let l" class="num">{{ rate(l) }}</td>
        </ng-container>

        <ng-container matColumnDef="stockValue">
          <th mat-header-cell *matHeaderCellDef class="num">{{ t('stock.value') }}</th>
          <td mat-cell *matCellDef="let l" class="num">{{ value(l) }}</td>
        </ng-container>

        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let l" class="actions">
            <button matIconButton (click)="viewMovements(l)" [matTooltip]="t('stock.movements')">
              <mat-icon>receipt_long</mat-icon>
            </button>
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns"></tr>
      </table>

      @if (!loading() && rows().length === 0) {
        <p class="empty">{{ t('stock.empty') }}</p>
      }
    </div>

    @if (result(); as r) {
      <div class="totals">
        <span><strong>{{ qty(r.totalUnits) }}</strong> {{ t('common.units') }}</span>
        <span><strong>{{ money(r.totalValue) }}</strong> {{ t('stock.total_value') }}</span>
        <span class="totals__note">{{ t('stock.unpriced_note') }}</span>
      </div>
    }
  `,
  styles: `
    .filters { display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; margin-bottom: .5rem; }
    .filters__search { width: 260px; }
    .filters__grade { width: 150px; }
    .filters__date { width: 210px; }
    .historical {
      display: flex; gap: .5rem; align-items: center;
      background: var(--info-bg); border: 1px solid var(--info-line); color: var(--info-ink);
      border-radius: 8px; padding: .6rem 1rem; margin-bottom: 1rem;
    }
    table { width: 100%; }
    .num { text-align: right; }
    td.num, th.num { padding-right: 1.25rem; }
    .qty { font-weight: 500; }
    .actions { width: 48px; text-align: right; }
    .totals { display: flex; gap: 2rem; padding: .9rem 1.25rem; margin-top: .5rem;
              background: var(--surface); border-radius: 8px; border: 1px solid var(--line); }
    .totals__note { margin-left: auto; opacity: .6; font-size: .85rem; }
  `,
})
export class StockPageComponent {
  protected readonly t = inject(I18nService).t;
  protected readonly gradeLabel = inject(I18nService).grade;
  private readonly stock = inject(StockService);
  private readonly lookups = inject(LookupsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly columns = ['productCode', 'productName', 'grade', 'quantity', 'unitRate', 'stockValue', 'actions'];

  readonly loading = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  readonly result = signal<StockResponse | null>(null);

  readonly today = new Date();
  readonly search = new FormControl('', { nonNullable: true });
  readonly grade = new FormControl<QualityGrade | null>(null);
  readonly asOf = new FormControl<Date | null>(null);
  readonly onlyInStock = new FormControl(false, { nonNullable: true });

  readonly rows = computed(() => this.result()?.lines ?? []);
  readonly grades = computed<QualityGrade[]>(() => this.lookups.enabledGrades());
  readonly asOfLabel = computed(() => this.result()?.asOf ?? '');

  constructor() {
    this.search.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => void this.load());
    this.grade.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => void this.load());
    this.asOf.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => void this.load());
    this.onlyInStock.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => void this.load());

    void this.load();
  }

  qty(value: number): string { return quantity(value); }
  money(value: number): string { return money(value); }

  /** Both are omitted from the response when a product has no price on file. */
  rate(line: StockLine): string { return line.unitRate === undefined ? '—' : money(line.unitRate); }
  value(line: StockLine): string { return line.stockValue === undefined ? '—' : money(line.stockValue); }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      this.result.set(
        await this.stock.get({
          search: this.search.value,
          grade: this.grade.value ?? undefined,
          onlyInStock: this.onlyInStock.value,
          asOf: toIsoDate(this.asOf.value) ?? undefined,
        }),
      );
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  viewMovements(line: StockLine): void {
    this.dialog.open(MovementsDialogComponent, {
      data: {
        productId: line.productId,
        productCode: line.productCode,
        productName: line.productName,
        grade: line.grade,
      },
    });
  }

  async adjust(line?: StockLine): Promise<void> {
    const ref = this.dialog.open(AdjustmentFormComponent, {
      data: { productId: line?.productId, grade: line?.grade },
    });

    if ((await ref.afterClosed().toPromise()) === true) {
      this.snackBar.open('Stock adjusted.', 'Dismiss', { duration: 4000 });
      await this.load();
    }
  }
}

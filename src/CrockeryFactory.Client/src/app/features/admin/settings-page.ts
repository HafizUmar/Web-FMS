import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FactorySetting, QualityGrade, RebuildResult } from '../../core/api.types';
import { AdminService } from '../../core/admin.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails, fieldErrorMap } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';

/** How each key is presented. Anything the server returns that is not listed still appears. */
interface FieldSpec {
  key: string;
  label: string;
  kind: 'text' | 'number' | 'grades';
  suffix?: string;
  min?: number;
  max?: number;
}

const GROUPS: { title: string; note: string; fields: FieldSpec[] }[] = [
  {
    title: 'Factory',
    note: 'Printed at the top of every document.',
    fields: [
      { key: 'Factory.Name', label: 'Factory name', kind: 'text' },
      { key: 'Factory.Address', label: 'Address', kind: 'text' },
      { key: 'Factory.Phone', label: 'Phone', kind: 'text' },
    ],
  },
  {
    title: 'Document numbering',
    note:
      'The letter each document number starts with, as in D-2609-0001. Change this to ' +
      'continue a series the factory already keeps in a register. Numbers already issued ' +
      'are never rewritten.',
    fields: [
      { key: 'Document.Prefix.Production', label: 'Production entries', kind: 'text' },
      { key: 'Document.Prefix.Dispatch', label: 'Dispatches', kind: 'text' },
      { key: 'Document.Prefix.Payment', label: 'Payment receipts', kind: 'text' },
      { key: 'Document.Prefix.Adjustment', label: 'Stock adjustments', kind: 'text' },
    ],
  },
  {
    title: 'Backdating',
    note:
      'How far back a document may be dated. A clerk entering yesterday\'s work is normal; ' +
      'one entering last month\'s is usually a mistake, and after this many days the server ' +
      'refuses it outright.',
    fields: [
      { key: 'Backdate.Days.Production', label: 'Production entries', kind: 'number', suffix: 'days', min: 0, max: 365 },
      { key: 'Backdate.Days.Dispatch', label: 'Dispatches', kind: 'number', suffix: 'days', min: 0, max: 365 },
      { key: 'Backdate.Days.Payment', label: 'Payments', kind: 'number', suffix: 'days', min: 0, max: 365 },
      { key: 'Backdate.Days.Adjustment', label: 'Stock adjustments', kind: 'number', suffix: 'days', min: 0, max: 365 },
    ],
  },
  {
    title: 'Rules',
    note: 'The thresholds the system warns or refuses on.',
    fields: [
      { key: 'Grades.Enabled', label: 'Grades in use', kind: 'grades' },
      { key: 'Production.LossWarningPercent', label: 'Loss warning above', kind: 'number', suffix: '%', min: 0, max: 100 },
      { key: 'Dispatch.Cancellation.MaxDays', label: 'Dispatch cancellable within', kind: 'number', suffix: 'days', min: 0, max: 3650 },
    ],
  },
];

/** The settings API speaks in the numeric QualityGrade values, not the names. */
const GRADE_VALUES: { value: string; label: QualityGrade }[] = [
  { value: '1', label: 'First' },
  { value: '2', label: 'Second' },
  { value: '3', label: 'Third' },
];

@Component({
  selector: 'app-settings-page',
  imports: [
    ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatCheckboxModule,
    MatButtonModule, MatIconModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <header class="page__header">
      <div>
        <h1>Settings</h1>
        <p class="page__sub">
          The numbers the rules are built on. Changing one takes effect on the next
          document, never on the ones already written.
        </p>
      </div>
    </header>

    <app-error-banner [problem]="error()" />
    @if (loading()) { <mat-progress-bar mode="indeterminate" /> }

    @if (form) {
      <form [formGroup]="form!">
        @for (group of groups; track group.title) {
          <section class="group">
            <h2>{{ group.title }}</h2>
            <p class="group__note">{{ group.note }}</p>

            <div class="group__fields">
              @for (field of group.fields; track field.key) {
                @if (known(field.key)) {
                  @if (field.kind === 'grades') {
                    <div class="grades">
                      <span class="grades__label">{{ field.label }}</span>
                      @for (grade of gradeValues; track grade.value) {
                        <mat-checkbox
                          [checked]="gradeEnabled(grade.value)"
                          (change)="toggleGrade(grade.value, $event.checked)">
                          {{ grade.label }}
                        </mat-checkbox>
                      }
                      <p class="grades__hint">
                        A grade that is not ticked is refused on every entry with
                        GRADE_NOT_ENABLED. Stock already held in it stays where it is.
                      </p>
                      @if (serverError(field.key); as message) {
                        <p class="field__error">{{ message }}</p>
                      }
                    </div>
                  } @else {
                    <mat-form-field appearance="outline">
                      <mat-label>{{ field.label }}</mat-label>
                      <!--
                        formControlName is a property binding, so it leaves no attribute
                        in the DOM. data-setting gives the key a handle that scripts and
                        tests can find the field by.
                      -->
                      <input
                        matInput
                        [formControlName]="field.key"
                        [attr.data-setting]="field.key"
                        [type]="field.kind === 'number' ? 'number' : 'text'"
                        [min]="field.min ?? null"
                        [max]="field.max ?? null" />
                      @if (field.suffix) { <span matTextSuffix>{{ field.suffix }}</span> }
                      @if (serverError(field.key); as message) {
                        <mat-error>{{ message }}</mat-error>
                      } @else if (control(field.key)?.invalid) {
                        <mat-error>{{ rangeHint(field) }}</mat-error>
                      }
                      <mat-hint>{{ describe(field.key) }}</mat-hint>
                    </mat-form-field>
                  }
                }
              }
            </div>
          </section>
        }

        @if (unlisted().length > 0) {
          <!--
            Anything the server added that this screen does not know how to lay out still
            has to be editable, or a new setting becomes unreachable until the UI catches up.
          -->
          <section class="group">
            <h2>Other</h2>
            <p class="group__note">Settings this screen has no special layout for.</p>
            <div class="group__fields">
              @for (setting of unlisted(); track setting.key) {
                <mat-form-field appearance="outline">
                  <mat-label>{{ setting.key }}</mat-label>
                  <input matInput [formControlName]="setting.key" [attr.data-setting]="setting.key" />
                  <mat-hint>{{ setting.description }}</mat-hint>
                </mat-form-field>
              }
            </div>
          </section>
        }

        <div class="bar">
          <span class="bar__state">
            @if (dirtyKeys().length === 0) {
              Nothing changed
            } @else {
              {{ dirtyKeys().length }} setting{{ dirtyKeys().length === 1 ? '' : 's' }} changed
            }
          </span>
          <button matButton (click)="reset()" [disabled]="dirtyKeys().length === 0 || busy()">
            Discard
          </button>
          <button
            matButton="filled"
            color="primary"
            (click)="save()"
            [disabled]="dirtyKeys().length === 0 || busy() || form!.invalid">
            {{ busy() ? 'Saving…' : 'Save changes' }}
          </button>
        </div>
      </form>
    }

    <!--
      Not a setting, but it belongs to whoever holds CanManageSettings and nowhere else in
      the app fits it.
    -->
    <section class="group maintenance">
      <h2>Maintenance</h2>
      <p class="group__note">
        Stock balances are a cache kept alongside the movement ledger. The ledger is the
        record; this rebuilds the cache from it. Safe to run at any time - it can only ever
        make the balances agree with the movements.
      </p>

      <button matButton="filled" (click)="rebuild()" [disabled]="rebuilding()">
        <mat-icon>sync</mat-icon>
        {{ rebuilding() ? 'Rebuilding…' : 'Rebuild stock balances' }}
      </button>

      @if (rebuildResult(); as r) {
        <div class="rebuild" [class.rebuild--changed]="changedCount(r) > 0" role="status">
          <mat-icon>{{ changedCount(r) > 0 ? 'build' : 'check_circle' }}</mat-icon>
          <div>
            @if (changedCount(r) === 0) {
              <strong>Everything already agreed.</strong>
              <p>
                {{ r.balancesExamined }} balances checked against the ledger, none out of step.
              </p>
            } @else {
              <strong>{{ changedCount(r) }} of {{ r.balancesExamined }} balances corrected.</strong>
              <p>
                {{ r.balancesCorrected }} adjusted, {{ r.balancesInserted }} added,
                {{ r.balancesRemoved }} removed.
              </p>
              @if (r.corrections.length > 0) {
                <ul class="rebuild__list">
                  @for (line of r.corrections.slice(0, 12); track line) { <li>{{ line }}</li> }
                </ul>
                @if (r.corrections.length > 12) {
                  <p class="rebuild__more">
                    and {{ r.corrections.length - 12 }} more - the full list is in the audit log.
                  </p>
                }
              }
            }
          </div>
        </div>
      }
    </section>
  `,
  styles: `
    .page__header h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
    .page__sub { margin: 0 0 1rem; opacity: .7; max-width: 70ch; }
    .group { background: #fff; border: 1px solid rgba(0,0,0,.08); border-radius: 8px;
             padding: 1.25rem; margin-bottom: 1rem; }
    .group h2 { margin: 0 0 .25rem; font-size: 1.05rem; }
    .group__note { margin: 0 0 1rem; opacity: .7; font-size: .875rem; max-width: 72ch; }
    .group__fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: .5rem 1rem; }
    .grades { grid-column: 1 / -1; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; }
    .grades__label { font-weight: 500; }
    .grades__hint { flex-basis: 100%; margin: 0; opacity: .65; font-size: .8rem; max-width: 70ch; }
    .field__error { flex-basis: 100%; margin: 0; color: #b3261e; font-size: .8rem; }
    .bar { position: sticky; bottom: 0; display: flex; align-items: center; gap: .75rem;
           background: #fff; border: 1px solid rgba(0,0,0,.08); border-radius: 8px;
           padding: .75rem 1.25rem; }
    .bar__state { margin-right: auto; opacity: .7; font-size: .875rem; }
    .maintenance { margin-top: 1rem; }
    .rebuild { display: flex; gap: .75rem; align-items: flex-start; margin-top: 1rem;
               background: #e8f5e9; border: 1px solid #b7dfbb; color: #14532d;
               border-radius: 8px; padding: .75rem 1rem; }
    .rebuild--changed { background: #fff4e5; border-color: #ffd9a0; color: #663c00; }
    .rebuild p { margin: .2rem 0 0; font-size: .875rem; }
    .rebuild__list { margin: .5rem 0 0; padding-left: 1.1rem; font-size: .8rem; }
    .rebuild__more { opacity: .75; }
  `,
})
export class SettingsPageComponent {
  private readonly admin = inject(AdminService);
  private readonly lookups = inject(LookupsService);
  private readonly fb = inject(FormBuilder);
  private readonly snackBar = inject(MatSnackBar);

  readonly groups = GROUPS;
  readonly gradeValues = GRADE_VALUES;

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<ProblemDetails | null>(null);
  private readonly fieldErrors = signal(new Map<string, string>());

  readonly settings = signal<FactorySetting[]>([]);
  form: FormGroup | null = null;

  /** Recomputed on every keystroke, so the save bar tells the truth as it is typed. */
  private readonly formVersion = signal(0);

  readonly dirtyKeys = computed(() => {
    this.formVersion();
    if (!this.form) return [];

    return this.settings()
      .filter((s) => String(this.form!.controls[s.key]?.value ?? '') !== s.value)
      .map((s) => s.key);
  });

  private readonly laidOut = new Set(GROUPS.flatMap((g) => g.fields.map((f) => f.key)));

  readonly unlisted = computed(() => this.settings().filter((s) => !this.laidOut.has(s.key)));

  readonly rebuilding = signal(false);
  readonly rebuildResult = signal<RebuildResult | null>(null);

  constructor() {
    void this.load();
  }

  /** Anything the rebuild had to touch. Zero is the answer everyone wants. */
  changedCount(result: RebuildResult): number {
    return result.balancesCorrected + result.balancesInserted + result.balancesRemoved;
  }

  async rebuild(): Promise<void> {
    this.rebuilding.set(true);
    this.error.set(null);
    this.rebuildResult.set(null);

    try {
      this.rebuildResult.set(await this.admin.rebuildStockBalances());
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.rebuilding.set(false);
    }
  }

  known(key: string): boolean {
    return this.settings().some((s) => s.key === key);
  }

  control(key: string) {
    return this.form?.controls[key] ?? null;
  }

  describe(key: string): string {
    return this.settings().find((s) => s.key === key)?.description ?? '';
  }

  serverError(key: string): string | undefined {
    // The server reports these as values.<key>.
    return this.fieldErrors().get(`values.${key}`);
  }

  rangeHint(field: FieldSpec): string {
    if (field.kind !== 'number') return 'Required';
    return `A number between ${field.min ?? 0} and ${field.max ?? 100}`;
  }

  gradeEnabled(value: string): boolean {
    const raw = String(this.form?.controls['Grades.Enabled']?.value ?? '');
    return raw.split(',').map((v) => v.trim()).includes(value);
  }

  toggleGrade(value: string, checked: boolean): void {
    const control = this.form?.controls['Grades.Enabled'];
    if (!control) return;

    const current = String(control.value ?? '').split(',').map((v) => v.trim()).filter(Boolean);
    const next = checked
      ? [...current, value]
      : current.filter((v) => v !== value);

    // Sorted so that ticking Second then First stores "1,2" rather than "2,1" - the same
    // configuration should not read as a change.
    control.setValue(next.sort().join(','));
    control.markAsDirty();
    this.formVersion.update((v) => v + 1);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      const settings = await this.admin.settings();
      this.settings.set(settings);
      this.build(settings);
    } catch (problem) {
      this.error.set(problem as ProblemDetails);
    } finally {
      this.loading.set(false);
    }
  }

  private build(settings: FactorySetting[]): void {
    const specs = new Map(GROUPS.flatMap((g) => g.fields).map((f) => [f.key, f]));

    const group: Record<string, unknown[]> = {};

    for (const setting of settings) {
      const spec = specs.get(setting.key);
      const validators = spec?.kind === 'number'
        ? [Validators.required, Validators.min(spec.min ?? 0), Validators.max(spec.max ?? 100)]
        : [];

      group[setting.key] = [setting.value, validators];
    }

    this.form = this.fb.group(group);
    this.form.valueChanges.subscribe(() => this.formVersion.update((v) => v + 1));
    this.formVersion.update((v) => v + 1);
  }

  reset(): void {
    this.build(this.settings());
    this.fieldErrors.set(new Map());
    this.error.set(null);
  }

  async save(): Promise<void> {
    const changed = this.dirtyKeys();
    if (changed.length === 0 || !this.form) return;

    this.busy.set(true);
    this.error.set(null);
    this.fieldErrors.set(new Map());

    // Only what actually changed. The server would skip the rest anyway, but sending the
    // whole form makes the audit entry claim fourteen settings were touched.
    const values: Record<string, string> = {};
    for (const key of changed) values[key] = String(this.form.controls[key].value ?? '');

    try {
      const saved = await this.admin.saveSettings(values);
      this.settings.set(saved);
      this.build(saved);

      // Grades, thresholds and the factory name are all read from the cached lookups, so
      // the rest of the app would keep showing the old values until a reload without this.
      await this.lookups.reloadSettings();

      this.snackBar.open(
        `Saved. ${changed.length === 1 ? 'One setting' : changed.length + ' settings'} updated.`,
        'Dismiss',
        { duration: 4000 },
      );
    } catch (thrown) {
      const problem = thrown as ProblemDetails;
      this.fieldErrors.set(fieldErrorMap(problem));
      this.error.set(problem.errors?.length ? null : problem);
    } finally {
      this.busy.set(false);
    }
  }
}

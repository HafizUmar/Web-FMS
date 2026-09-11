import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { FactorySetting, QualityGrade, ReasonCode, ReasonCodeType } from './api.types';

/**
 * Reason codes and factory settings.
 *
 * Both are needed by nearly every form and change a few times a year, so they are loaded
 * once after sign-in and held. Settings are reloaded explicitly after an administrator
 * saves them rather than left to expire: a clerk told a grade is not enabled, who waits
 * for the owner to enable it and is told the same thing for another minute, concludes the
 * system is broken.
 */
@Injectable({ providedIn: 'root' })
export class LookupsService {
  private readonly http = inject(HttpClient);

  private readonly reasonCodes = signal<ReasonCode[]>([]);
  private readonly settings = signal<FactorySetting[]>([]);

  readonly allReasonCodes = this.reasonCodes.asReadonly();
  readonly allSettings = this.settings.asReadonly();

  async load(): Promise<void> {
    const [codes, settings] = await Promise.all([
      firstValueFrom(this.http.get<ReasonCode[]>('/api/v1/reason-codes')),
      this.loadSettingsIfPermitted(),
    ]);

    this.reasonCodes.set(codes);
    this.settings.set(settings);
  }

  /**
   * Settings need CanManageSettings, which a clerk does not have. A 403 here is expected
   * and must not stop the application starting.
   */
  private async loadSettingsIfPermitted(): Promise<FactorySetting[]> {
    try {
      return await firstValueFrom(this.http.get<FactorySetting[]>('/api/v1/settings'));
    } catch {
      return [];
    }
  }

  async reloadSettings(): Promise<void> {
    this.settings.set(await this.loadSettingsIfPermitted());
  }

  /** Server order is sortOrder then code, which is how the factory wants them listed. */
  reasonsFor(type: ReasonCodeType): ReasonCode[] {
    return this.reasonCodes().filter((r) => r.type === type && r.isActive);
  }

  setting(key: string): string | undefined {
    return this.settings().find((s) => s.key === key)?.value;
  }

  settingNumber(key: string, fallback: number): number {
    const parsed = Number(this.setting(key));
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  /**
   * Which grades this factory sorts to. Defaults to First and Second when the setting
   * cannot be read - a clerk without CanManageSettings still has to pick a grade, and
   * offering none would make every form unusable.
   */
  enabledGrades(): QualityGrade[] {
    const raw = this.setting('Grades.Enabled');
    if (!raw) return ['First', 'Second'];

    const byNumber: Record<string, QualityGrade> = { '1': 'First', '2': 'Second', '3': 'Third' };

    const grades = raw
      .split(',')
      .map((part) => byNumber[part.trim()])
      .filter((g): g is QualityGrade => !!g);

    return grades.length > 0 ? grades : ['First', 'Second'];
  }
}

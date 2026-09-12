import { Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ProblemDetails } from '../../core/problem-details';
import { I18nService } from '../../core/i18n/i18n.service';
import { STRINGS, StringKey } from '../../core/i18n/strings';

/**
 * Shows a server error the way the backend intends: `detail` verbatim, because it is
 * written for a clerk and names the product, the numbers or the date involved.
 *
 * A 500 is the exception - the server deliberately returns no detail for one, so only
 * the trace id is offered, which is what a support call quotes.
 */
@Component({
  selector: 'app-error-banner',
  imports: [MatIconModule],
  template: `
    @if (problem(); as p) {
      <div class="banner" [class.banner--warn]="p.status === 422" role="alert">
        <mat-icon>{{ p.status === 422 ? 'report_problem' : 'error_outline' }}</mat-icon>
        <div class="banner__body">
          <strong>{{ title() }}</strong>
          <p>{{ message() }}</p>
          @if (showTrace()) {
            <small>{{ t('err.reference', { id: p.traceId ?? '' }) }}</small>
          }
        </div>
      </div>
    }
  `,
  styles: `
    .banner {
      display: flex;
      gap: .75rem;
      padding: .875rem 1rem;
      border-radius: 8px;
      background: var(--bad-bg);
      border: 1px solid var(--bad-line);
      color: var(--bad-ink);
      margin-bottom: 1rem;
    }
    .banner--warn { background: var(--warn-bg); border-color: var(--warn-line); color: var(--warn-ink); }
    .banner__body p { margin: .25rem 0 0; }
    .banner__body small { display: block; margin-top: .35rem; opacity: .75; }
  `,
})
export class ErrorBannerComponent {
  private readonly i18n = inject(I18nService);
  protected readonly t = this.i18n.t;

  readonly problem = input<ProblemDetails | null>(null);

  /**
   * `detail` is written for a clerk, but it is written in English on the server. `code`
   * is the stable identity, so the common ones are translated here and anything without
   * a translation falls back to the server's own sentence rather than to nothing.
   *
   * This is a client-side half of the job: an error the server words for a specific
   * product or quantity still arrives in English. Translating those properly means
   * translating them where they are written, in the API.
   */
  protected readonly message = computed(() => {
    const p = this.problem();
    if (!p) return '';

    const key = `err.${p.code}` as StringKey;
    return key in STRINGS ? this.i18n.t(key) : p.detail;
  });

  /**
   * The title follows the message's language. Leaving the server's English title above a
   * translated body produced a banner that was half in each language, which reads worse
   * than either one alone.
   */
  protected readonly title = computed(() => {
    const p = this.problem();
    if (!p) return '';

    if (p.code === 'NETWORK_ERROR') return this.i18n.t('err.unreachable');

    const translated = `err.${p.code}` as StringKey;
    return translated in STRINGS ? this.i18n.t('err.title') : p.title;
  });

  /** Only useful where the message itself is not - i.e. a 500. */
  readonly showTrace = computed(() => {
    const p = this.problem();
    return !!p?.traceId && p.status >= 500;
  });
}

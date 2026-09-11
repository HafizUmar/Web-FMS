import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ProblemDetails } from '../../core/problem-details';

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
          <strong>{{ p.title }}</strong>
          <p>{{ p.detail }}</p>
          @if (showTrace()) {
            <small>Reference {{ p.traceId }}</small>
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
  readonly problem = input<ProblemDetails | null>(null);

  /** Only useful where the message itself is not - i.e. a 500. */
  readonly showTrace = computed(() => {
    const p = this.problem();
    return !!p?.traceId && p.status >= 500;
  });
}

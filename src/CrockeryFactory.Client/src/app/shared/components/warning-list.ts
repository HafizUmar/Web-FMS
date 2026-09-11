import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

const MESSAGES: Record<string, string> = {
  LOSS_UNUSUALLY_HIGH:
    'Loss on this entry is unusually high. It has been saved — check the figures if that looks wrong.',
  RATE_BELOW_LIST:
    'One or more rates are well below the list price. The dispatch has been saved.',
  PAYMENT_EXCEEDS_OUTSTANDING:
    'This receipt is more than the customer owed. It has been saved as an advance.',
};

/**
 * Warnings returned alongside a 201. The document was saved - these are never errors, and
 * never block. The first time the system refuses to record what actually happened on the
 * floor is the day the clerk goes back to the paper register.
 */
@Component({
  selector: 'app-warning-list',
  imports: [MatIconModule],
  template: `
    @if (warnings().length > 0) {
      <div class="warnings" role="status">
        @for (warning of warnings(); track warning) {
          <div class="warnings__row">
            <mat-icon>info_outline</mat-icon>
            <span>{{ message(warning) }}</span>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .warnings {
      background: #e8f4fd;
      border: 1px solid #b6dcf7;
      color: #0b4a6f;
      border-radius: 8px;
      padding: .75rem 1rem;
      margin-bottom: 1rem;
    }
    .warnings__row { display: flex; gap: .6rem; align-items: flex-start; }
    .warnings__row + .warnings__row { margin-top: .4rem; }
  `,
})
export class WarningListComponent {
  readonly warnings = input<string[]>([]);

  message(code: string): string {
    return MESSAGES[code] ?? code;
  }
}

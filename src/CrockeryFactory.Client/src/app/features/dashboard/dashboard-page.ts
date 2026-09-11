import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';

/**
 * Placeholder. The real dashboard (RP-07) is built in a later stage; this exists so the
 * shell has a landing page and so a route guard has somewhere to send a user who typed a
 * URL they cannot reach.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [MatCardModule, MatIconModule],
  template: `
    @if (denied()) {
      <div class="denied" role="alert">
        <mat-icon>lock</mat-icon>
        You do not have access to that page.
      </div>
    }

    <h1>Welcome, {{ auth.fullName() }}</h1>
    <p class="sub">Signed in as {{ auth.roles().join(', ') }}.</p>

    <mat-card class="card">
      <mat-card-content>
        <p>
          The dashboard, production, stock, dispatches, customers, payments, reports and
          administration screens are built in later stages. <strong>Products</strong> is
          complete and is reachable from the menu.
        </p>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    h1 { font-size: 1.5rem; margin: 0 0 .25rem; }
    .sub { opacity: .7; margin: 0 0 1.25rem; }
    .card { max-width: 640px; }
    .denied {
      display: flex; gap: .5rem; align-items: center;
      background: #fff4e5; border: 1px solid #ffd9a0; color: #663c00;
      padding: .75rem 1rem; border-radius: 8px; margin-bottom: 1rem;
    }
  `,
})
export class DashboardPageComponent {
  readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  denied(): boolean {
    return this.route.snapshot.queryParamMap.has('denied');
  }
}

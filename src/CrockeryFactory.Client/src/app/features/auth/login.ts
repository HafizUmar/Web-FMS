import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { LookupsService } from '../../core/lookups.service';
import { ProblemDetails } from '../../core/problem-details';
import { ErrorBannerComponent } from '../../shared/components/error-banner';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Split layout: the brand panel on the left, the form on the right.
 *
 * The panel collapses away below 900px rather than stacking above the form - on a phone
 * the only thing anyone wants here is the two fields and the button, and a decorative
 * header would push them under the fold.
 */
@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatProgressBarModule, ErrorBannerComponent,
  ],
  template: `
    <div class="login">
      <aside class="panel">
        <div class="panel__brand">
          <span class="panel__mark" aria-hidden="true"><mat-icon>local_fire_department</mat-icon></span>
          <div>
            <h1>Crockery Factory</h1>
            <p>{{ t('login.tagline') }}</p>
          </div>
        </div>

        <ul class="panel__points">
          <li><mat-icon>inventory_2</mat-icon> {{ t('login.point.stock') }}</li>
          <li><mat-icon>local_shipping</mat-icon> {{ t('login.point.sales') }}</li>
          <li><mat-icon>insights</mat-icon> {{ t('login.point.reports') }}</li>
        </ul>

        <p class="panel__foot">{{ t('login.network_note') }}</p>
      </aside>

      <main class="form-side">
        <div class="card">
          @if (busy()) { <mat-progress-bar mode="indeterminate" class="card__bar" /> }

          <h2>{{ t('login.welcome') }}</h2>
          <p class="card__sub">{{ t('login.subtitle') }}</p>

          @if (notice()) {
            <p class="notice" role="status">
              <mat-icon class="inline">info</mat-icon> {{ notice() }}
            </p>
          }

          <app-error-banner [problem]="error()" />

          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>{{ t('login.username') }}</mat-label>
              <input matInput formControlName="userName" autocomplete="username" cdkFocusInitial />
              <mat-icon matIconSuffix>person_outline</mat-icon>
              @if (form.controls.userName.touched && form.controls.userName.invalid) {
                <mat-error>{{ t('login.enter_username') }}</mat-error>
              }
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>{{ t('login.password') }}</mat-label>
              <input
                matInput
                [type]="reveal() ? 'text' : 'password'"
                formControlName="password"
                autocomplete="current-password" />
              <button
                matIconButton
                matIconSuffix
                type="button"
                (click)="reveal.set(!reveal())"
                [attr.aria-label]="reveal() ? t('login.hide_password') : t('login.show_password')">
                <mat-icon>{{ reveal() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
              @if (form.controls.password.touched && form.controls.password.invalid) {
                <mat-error>{{ t('login.enter_password') }}</mat-error>
              }
            </mat-form-field>

            <!-- No trailing icon: Material renders a button's icon before its label
                 whatever the markup order, and "-> Sign in" reads as a back arrow. -->
            <button matButton="filled" color="primary" type="submit" [disabled]="busy()" class="submit">
              {{ busy() ? t('login.submitting') : t('login.submit') }}
            </button>
          </form>

          <p class="card__help">
            {{ t('login.forgot') }}
          </p>
        </div>
      </main>
    </div>
  `,
  styles: `
    .login {
      display: grid;
      grid-template-columns: 1.05fr 1fr;
      min-height: 100vh;
    }

    /* ---- Brand panel ---- */
    .panel {
      position: relative;
      display: flex;
      flex-direction: column;
      justify-content: center;
      gap: 2.5rem;
      padding: 3rem clamp(2rem, 5vw, 4.5rem);
      background: var(--chrome);
      color: #fff;
      overflow: hidden;
    }

    /* Two soft lights rather than a flat fill, so the panel has some depth behind
       the text without any image to download. */
    .panel::before, .panel::after {
      content: '';
      position: absolute;
      border-radius: 50%;
      pointer-events: none;
    }
    .panel::before {
      width: 520px; height: 520px;
      top: -180px; right: -160px;
      background: radial-gradient(circle, rgba(240, 140, 26, .32), transparent 62%);
    }
    .panel::after {
      width: 420px; height: 420px;
      bottom: -140px; left: -120px;
      background: radial-gradient(circle, rgba(255, 255, 255, .16), transparent 64%);
    }

    .panel > * { position: relative; z-index: 1; }

    .panel__brand { display: flex; align-items: center; gap: 1rem; }

    .panel__mark {
      display: grid;
      place-items: center;
      width: 58px;
      height: 58px;
      border-radius: 17px;
      background: rgba(255, 255, 255, .15);
      border: 1px solid rgba(255, 255, 255, .24);
      color: var(--ember-300);
    }
    .panel__mark mat-icon { font-size: 30px; width: 30px; height: 30px; }

    .panel h1 { margin: 0; font-size: 1.85rem; font-weight: 600; letter-spacing: -.02em; }
    .panel__brand p { margin: .1rem 0 0; opacity: .8; letter-spacing: .1em;
                      text-transform: uppercase; font-size: .72rem; }

    .panel__points { list-style: none; margin: 0; padding: 0; display: grid; gap: 1.1rem; max-width: 42ch; }
    .panel__points li { display: flex; gap: .85rem; align-items: flex-start; opacity: .93; line-height: 1.5; }
    .panel__points mat-icon { color: var(--ember-300); flex: none; }

    .panel__foot { margin: 0; font-size: .8rem; opacity: .6; }

    /* ---- Form ---- */
    .form-side {
      display: grid;
      place-items: center;
      padding: 2rem 1.25rem;
      background-color: var(--app-bg);
      background-image: var(--app-bg-accent);
    }

    .card {
      position: relative;
      width: min(400px, 100%);
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 18px;
      box-shadow: var(--shadow-3);
      padding: 2.25rem 2rem 1.75rem;
      overflow: hidden;
    }

    .card__bar { position: absolute; inset: 0 0 auto; }
    .card h2 { margin: 0; font-size: 1.5rem; font-weight: 600; letter-spacing: -.02em; color: var(--ink); }
    .card__sub { margin: .25rem 0 1.5rem; color: var(--ink-3); }

    .notice {
      display: flex; gap: .5rem; align-items: flex-start;
      background: var(--info-bg); border: 1px solid var(--info-line); color: var(--info-ink);
      border-radius: var(--radius-sm); padding: .7rem .9rem; margin: 0 0 1rem; font-size: .9rem;
    }

    form { display: flex; flex-direction: column; gap: .25rem; }

    .submit { margin-top: .75rem; height: 46px; font-size: 1rem; }

    .card__help { margin: 1.25rem 0 0; font-size: .82rem; color: var(--ink-3); text-align: center; }

    /* The panel is decoration; on a narrow screen the form is the whole job. */
    @media (max-width: 900px) {
      .login { grid-template-columns: 1fr; }
      .panel { display: none; }
    }
  `,
})
export class LoginComponent {
  protected readonly t = inject(I18nService).t;
  private readonly auth = inject(AuthService);
  private readonly lookups = inject(LookupsService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  readonly busy = signal(false);
  readonly reveal = signal(false);
  readonly error = signal<ProblemDetails | null>(null);

  readonly form = this.fb.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  /** Why the user was sent here, when they did not choose to come. */
  readonly notice = signal(
    {
      'session-expired': 'Your session has ended. Please sign in again.',
      'password-changed': 'Your password was changed. Please sign in again.',
    }[this.route.snapshot.queryParamMap.get('reason') ?? ''] ?? '',
  );

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.login(this.form.getRawValue());

      // Dropdown data every screen needs. Loaded before navigating so the first page
      // does not render with empty selects.
      await this.lookups.load();

      const returnTo = this.route.snapshot.queryParamMap.get('returnTo');
      await this.router.navigateByUrl(returnTo && !returnTo.startsWith('/login') ? returnTo : '/');
    } catch (problem) {
      // 401 for a wrong password and 401 for an unknown username are identical by
      // design - showing detail verbatim keeps it that way.
      this.error.set(problem as ProblemDetails);
      this.notice.set('');
    } finally {
      this.busy.set(false);
    }
  }
}

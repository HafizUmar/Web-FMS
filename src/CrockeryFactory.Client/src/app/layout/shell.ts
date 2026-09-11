import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../core/auth.service';
import { LookupsService } from '../core/lookups.service';
import { ThemeService } from '../core/theme.service';
import { Permission } from '../core/api.types';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  permission: Permission;
  /** Starts a new group in the rail. The label is a heading above it. */
  group?: string;
}

const NAV: NavItem[] = [
  { path: '/', label: 'Dashboard', icon: 'dashboard', permission: 'CanViewReports' },

  { path: '/production', label: 'Production', icon: 'local_fire_department', permission: 'CanViewReports', group: 'Factory floor' },
  { path: '/stock', label: 'Stock', icon: 'inventory_2', permission: 'CanViewReports' },
  { path: '/products', label: 'Products', icon: 'category', permission: 'CanViewReports' },

  { path: '/dispatches', label: 'Dispatches', icon: 'local_shipping', permission: 'CanViewReports', group: 'Sales' },
  { path: '/customers', label: 'Customers', icon: 'groups', permission: 'CanViewReports' },
  { path: '/payments', label: 'Payments', icon: 'payments', permission: 'CanViewReports' },
  { path: '/reports', label: 'Reports', icon: 'assessment', permission: 'CanViewReports' },

  { path: '/admin/users', label: 'Users', icon: 'manage_accounts', permission: 'CanManageUsers', group: 'Administration' },
  { path: '/admin/settings', label: 'Settings', icon: 'settings', permission: 'CanManageSettings' },
  { path: '/admin/reason-codes', label: 'Reason codes', icon: 'list_alt', permission: 'CanViewReports' },
  { path: '/admin/audit', label: 'Audit', icon: 'history', permission: 'CanViewAudit' },
];

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive,
    MatToolbarModule, MatSidenavModule, MatListModule,
    MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule,
  ],
  template: `
    <mat-toolbar class="topbar">
      <span class="brand">
        <span class="brand__mark" aria-hidden="true">
          <mat-icon>local_fire_department</mat-icon>
        </span>
        <span class="brand__text">
          <strong>{{ factoryName() }}</strong>
          <small>Factory management</small>
        </span>
      </span>

      <span class="topbar__spacer"></span>

      <button
        matIconButton
        class="topbar__action"
        (click)="theme.toggle()"
        [matTooltip]="theme.mode() === 'dark' ? 'Switch to light' : 'Switch to dark'"
        [attr.aria-label]="theme.mode() === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'">
        <mat-icon>{{ theme.mode() === 'dark' ? 'light_mode' : 'dark_mode' }}</mat-icon>
      </button>

      <button matButton class="account-trigger" [matMenuTriggerFor]="account" aria-label="Account">
        <!--
          The row is declared on a wrapper this template owns. Styling Material's own
          .mdc-button__label does not work: that element comes from Material's template,
          so Angular's style scoping attribute is never applied to it and the rule is
          silently dropped - which is how the avatar ended up stacked above the name.
        -->
        <span class="account-trigger__inner">
          <span class="avatar" aria-hidden="true">{{ initials() }}</span>
          <span class="account-trigger__name">{{ auth.fullName() }}</span>
          <mat-icon class="account-trigger__caret">expand_more</mat-icon>
        </span>
      </button>

      <mat-menu #account="matMenu">
        <div class="account">
          <span class="avatar avatar--lg" aria-hidden="true">{{ initials() }}</span>
          <div>
            <strong>{{ auth.fullName() }}</strong>
            <small>{{ auth.roles().join(', ') }}</small>
          </div>
        </div>
        <button mat-menu-item routerLink="/change-password">
          <mat-icon>password</mat-icon><span>Change password</span>
        </button>
        <button mat-menu-item (click)="signOut()">
          <mat-icon>logout</mat-icon><span>Sign out</span>
        </button>
      </mat-menu>
    </mat-toolbar>

    <mat-sidenav-container class="layout">
      <mat-sidenav mode="side" opened class="layout__nav">
        <mat-nav-list>
          @for (item of visibleNav(); track item.path) {
            @if (item.group) { <h3 class="nav__group">{{ item.group }}</h3> }

            <a
              mat-list-item
              [routerLink]="item.path"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: item.path === '/' }">
              <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label }}</span>
            </a>
          }
        </mat-nav-list>
      </mat-sidenav>

      <mat-sidenav-content class="layout__content">
        <router-outlet />
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    /* One gradient for the whole chrome, so the bar and the rail read as a single
       piece rather than two panels that happen to be adjacent. */
    .topbar {
      position: sticky;
      top: 0;
      z-index: 10;
      height: 64px;
      padding: 0 1rem 0 1.25rem;
      background: var(--chrome);
      color: #fff;
      box-shadow: var(--shadow-2);
    }

    .brand { display: flex; align-items: center; gap: .7rem; }

    .brand__mark {
      display: grid;
      place-items: center;
      width: 38px;
      height: 38px;
      border-radius: 11px;
      background: rgba(255, 255, 255, .16);
      border: 1px solid rgba(255, 255, 255, .22);
      color: var(--ember-300);
    }

    .brand__text { display: flex; flex-direction: column; line-height: 1.15; }
    .brand__text strong { font-size: 1.02rem; font-weight: 600; letter-spacing: -.01em; }
    .brand__text small { font-size: .7rem; opacity: .75; letter-spacing: .06em; text-transform: uppercase; }

    .topbar__spacer { flex: 1 1 auto; }
    .topbar__action { color: rgba(255, 255, 255, .92); }

    .account-trigger {
      color: #fff !important;
      height: 42px;
      padding: 0 .7rem 0 .35rem !important;
      border-radius: 999px !important;
      background: rgba(255, 255, 255, .12);
    }

    .account-trigger__inner {
      display: inline-flex;
      align-items: center;
      gap: .5rem;
      white-space: nowrap;
    }

    .account-trigger__name { font-weight: 500; max-width: 16ch; overflow: hidden; text-overflow: ellipsis; }
    .account-trigger__caret { font-size: 20px; width: 20px; height: 20px; opacity: .8; }

    .avatar {
      display: grid;
      place-items: center;
      width: 30px;
      height: 30px;
      border-radius: 50%;
      background: linear-gradient(140deg, var(--ember-500), var(--ember-700));
      color: #fff;
      font-size: .76rem;
      font-weight: 700;
      letter-spacing: .02em;
    }

    .avatar--lg { width: 40px; height: 40px; font-size: .95rem; }

    .account {
      display: flex;
      align-items: center;
      gap: .75rem;
      padding: .9rem 1rem;
      border-bottom: 1px solid var(--line);
      min-width: 220px;
    }
    .account strong { display: block; color: var(--ink); }
    .account small { color: var(--ink-3); }

    .layout { height: calc(100vh - 64px); background: transparent; }

    .layout__nav {
      width: 244px;
      border-right: 1px solid var(--line);
      background: var(--surface);
      padding: .5rem 0 1rem;
    }

    .nav__group {
      margin: 1.1rem 0 .2rem;
      padding: 0 1.25rem;
      font-size: .68rem;
      font-weight: 600;
      letter-spacing: .1em;
      text-transform: uppercase;
      color: var(--ink-3);
    }

    /* Inset pills rather than full-width bars: the active item reads as a selected
       object, and the rail keeps a margin so it does not run into the content. */
    .layout__nav a[mat-list-item] {
      width: calc(100% - 1rem);
      margin: 1px .5rem;
      border-radius: 10px;
      color: var(--ink-2);
      transition: background-color .12s ease, color .12s ease;
    }

    .layout__nav a[mat-list-item]:not(.active):hover { background: var(--surface-hover); }

    /*
      :not(.active) on the hover rule rather than extra weight on the active one.
      Both selectors were the same specificity and hover came later, so putting the
      pointer on the page you are already looking at wiped the active styling - which
      is most of the time, since that is the link you just clicked.
    */
    .layout__nav a[mat-list-item].active {
      background: linear-gradient(135deg, var(--brand-600), var(--brand-500));
      color: #fff;
      box-shadow: var(--shadow-brand);
    }

    .layout__nav a[mat-list-item].active:hover {
      background: linear-gradient(135deg, var(--brand-700), var(--brand-600));
    }

    .layout__nav a.active mat-icon { color: #fff; }
    .layout__nav mat-icon { color: var(--ink-3); }

    .layout__content { padding: 1.75rem; background: transparent; }
  `,
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  readonly theme = inject(ThemeService);
  private readonly lookups = inject(LookupsService);

  /**
   * Built from the permissions the server issued, so the menu cannot offer a page the
   * server would refuse. The route guards repeat the check for typed URLs.
   */
  readonly visibleNav = computed(() => {
    this.auth.user();
    const allowed = NAV.filter((item) => this.auth.has(item.permission));

    // A heading whose whole group was filtered out would leave a label over nothing, so
    // the heading moves to whichever item now starts the group.
    return allowed.map((item, index) => {
      const previous = NAV.slice(0, NAV.indexOf(item)).reverse();
      const heading = item.group ?? previous.find((p) => p.group)?.group;
      const priorHeading = index === 0
        ? null
        : headingFor(allowed[index - 1]);

      return { ...item, group: heading !== priorHeading ? heading : undefined };
    });
  });

  readonly factoryName = computed(
    () => this.lookups.setting('Factory.Name') || 'Crockery Factory',
  );

  /** Two letters is enough to recognise yourself, and fits a 30px circle. */
  readonly initials = computed(() => {
    const parts = this.auth.fullName().trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return '?';

    return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
  });

  signOut(): void {
    void this.auth.logout();
  }
}

/** The heading an item sits under, whether or not it declares one itself. */
function headingFor(item: NavItem): string | undefined {
  const index = NAV.indexOf(item);
  for (let i = index; i >= 0; i--) {
    if (NAV[i].group) return NAV[i].group;
  }
  return undefined;
}

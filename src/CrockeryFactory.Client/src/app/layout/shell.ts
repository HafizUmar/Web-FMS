import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { AuthService } from '../core/auth.service';
import { LookupsService } from '../core/lookups.service';
import { Permission } from '../core/api.types';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  permission: Permission;
}

const NAV: NavItem[] = [
  { path: '/', label: 'Dashboard', icon: 'dashboard', permission: 'CanViewReports' },
  { path: '/production', label: 'Production', icon: 'local_fire_department', permission: 'CanViewReports' },
  { path: '/stock', label: 'Stock', icon: 'inventory_2', permission: 'CanViewReports' },
  { path: '/dispatches', label: 'Dispatches', icon: 'local_shipping', permission: 'CanViewReports' },
  { path: '/customers', label: 'Customers', icon: 'groups', permission: 'CanViewReports' },
  { path: '/payments', label: 'Payments', icon: 'payments', permission: 'CanViewReports' },
  { path: '/products', label: 'Products', icon: 'category', permission: 'CanViewReports' },
  { path: '/reports', label: 'Reports', icon: 'assessment', permission: 'CanViewReports' },
  { path: '/admin/users', label: 'Users', icon: 'manage_accounts', permission: 'CanManageUsers' },
  { path: '/admin/settings', label: 'Settings', icon: 'settings', permission: 'CanManageSettings' },
  { path: '/admin/reason-codes', label: 'Reason codes', icon: 'list_alt', permission: 'CanViewReports' },
  { path: '/admin/audit', label: 'Audit', icon: 'history', permission: 'CanViewAudit' },
];

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive,
    MatToolbarModule, MatSidenavModule, MatListModule,
    MatIconModule, MatButtonModule, MatMenuModule,
  ],
  template: `
    <mat-toolbar color="primary" class="topbar">
      <span class="topbar__brand">{{ factoryName() }}</span>
      <span class="topbar__spacer"></span>

      <button matIconButton [matMenuTriggerFor]="account" aria-label="Account">
        <mat-icon>account_circle</mat-icon>
      </button>
      <mat-menu #account="matMenu">
        <div class="account">
          <strong>{{ auth.fullName() }}</strong>
          <small>{{ auth.roles().join(', ') }}</small>
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
    .topbar { position: sticky; top: 0; z-index: 10; }
    .topbar__brand { font-weight: 500; }
    .topbar__spacer { flex: 1 1 auto; }
    .account { padding: .75rem 1rem; display: flex; flex-direction: column; }
    .account small { opacity: .7; }
    .layout { height: calc(100vh - 64px); }
    .layout__nav { width: 232px; border-right: 1px solid rgba(0,0,0,.08); }
    .layout__content { padding: 1.5rem; background: #fafafa; }
    a.active { background: rgba(0,0,0,.06); font-weight: 500; }
  `,
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  private readonly lookups = inject(LookupsService);

  /**
   * Built from the permissions the server issued, so the menu cannot offer a page the
   * server would refuse. The route guards repeat the check for typed URLs.
   */
  readonly visibleNav = computed(() => {
    this.auth.user();
    return NAV.filter((item) => this.auth.has(item.permission));
  });

  readonly factoryName = computed(
    () => this.lookups.setting('Factory.Name') || 'Crockery Factory',
  );

  signOut(): void {
    void this.auth.logout();
  }
}

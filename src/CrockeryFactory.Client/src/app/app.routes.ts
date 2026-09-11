import { Routes } from '@angular/router';
import { requiresAuthentication, requiresPermission } from './core/auth.guard';
import { ShellComponent } from './layout/shell';

/**
 * Everything except the login page sits behind the shell and an authentication guard.
 * Pages needing a specific policy carry their own guard as well, so a typed URL is
 * refused exactly as the nav would have hidden it.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login').then((m) => m.LoginComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [requiresAuthentication],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/dashboard/dashboard-page').then((m) => m.DashboardPageComponent),
      },
      {
        path: 'products',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/products/products-page').then((m) => m.ProductsPageComponent),
      },
      {
        path: 'production',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/production/production-page').then((m) => m.ProductionPageComponent),
      },
      {
        path: 'stock',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/stock/stock-page').then((m) => m.StockPageComponent),
      },
      {
        path: 'customers',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/sales/customers-page').then((m) => m.CustomersPageComponent),
      },
      {
        path: 'dispatches',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/sales/dispatches-page').then((m) => m.DispatchesPageComponent),
      },
      {
        path: 'payments',
        canActivate: [requiresPermission('CanViewReports')],
        loadComponent: () =>
          import('./features/sales/payments-page').then((m) => m.PaymentsPageComponent),
      },
      {
        path: 'change-password',
        loadComponent: () =>
          import('./features/auth/change-password').then((m) => m.ChangePasswordComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

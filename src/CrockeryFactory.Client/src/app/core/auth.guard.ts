import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { Permission } from './api.types';

/**
 * The nav hides what a user cannot reach, but a typed URL must not render it either -
 * hiding a link is not access control.
 */
export function requiresPermission(permission: Permission): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isAuthenticated()) {
      auth.clearAndRedirect();
      return false;
    }

    if (auth.has(permission)) return true;

    return router.createUrlTree(['/'], { queryParams: { denied: permission } });
  };
}

export const requiresAuthentication: CanActivateFn = () => {
  const auth = inject(AuthService);

  if (auth.isAuthenticated()) return true;

  auth.clearAndRedirect();
  return false;
};

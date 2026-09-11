import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideNativeDateAdapter } from '@angular/material/core';
import { routes } from './app.routes';
import { apiErrorInterceptor, credentialsInterceptor } from './core/http.interceptors';
import { AuthService } from './core/auth.service';
import { LookupsService } from './core/lookups.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideNativeDateAdapter(),

    provideHttpClient(
      withFetch(),
      // Order matters: credentials must be on the request before anything can succeed,
      // and the error interceptor must wrap everything so no failure escapes unmapped.
      withInterceptors([credentialsInterceptor, apiErrorInterceptor]),
    ),

    /**
     * Restores the session before the first route renders. The cookie survives a page
     * refresh, so a returning user is already signed in; without this they would be
     * bounced to the login page and would have to sign in again on every reload.
     */
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      const lookups = inject(LookupsService);

      await auth.restore();

      // Only worth fetching once there is a session to fetch them with.
      if (auth.isAuthenticated()) await lookups.load();
    }),
  ],
};

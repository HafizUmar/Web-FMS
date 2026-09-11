import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { toProblemDetails } from './problem-details';

/**
 * Sends the session cookie. Without withCredentials the browser omits it and every
 * authenticated call returns 401 for a reason that has nothing to do with the code.
 */
export const credentialsInterceptor: HttpInterceptorFn = (request, next) =>
  next(request.clone({ withCredentials: true }));

/**
 * Turns every failure into a ProblemDetails, and ends the session on a 401.
 *
 * There is deliberately no refresh-and-retry here: the backend issues no refresh token
 * and has no refresh endpoint. The cookie slides on each request; when it is gone, it is
 * gone, and the only correct response is to sign in again.
 */
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);

      const problem = toProblemDetails(error.error, error.status);

      if (error.status === 401) {
        // Login returning 401 is a wrong password, not a dead session. The login page
        // shows it as a form error; redirecting from there would lose the message.
        const isLogin = request.url.includes('/auth/login');

        // /auth/me returning 401 during startup is the ordinary "not signed in" answer.
        const isRestore = request.url.includes('/auth/me');

        if (!isLogin && !isRestore) auth.clearAndRedirect();
      }

      return throwError(() => problem);
    }),
  );
};

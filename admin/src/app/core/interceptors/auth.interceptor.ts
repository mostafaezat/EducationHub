import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  if (!req.url.includes('/api/')) {
    return next(req);
  }

  // The public pre-registration page is anonymous by design: the centre comes from the URL,
  // so a staff session open in the same browser (possibly of another centre) must not ride along.
  const isAuthEndpoint =
    req.url.includes('/api/auth/login') ||
    req.url.includes('/api/auth/refresh') ||
    req.url.includes('/api/public/');

  if (isAuthEndpoint) {
    return next(req);
  }

  return auth.ensureValidToken().pipe(
    switchMap((valid) => {
      if (!valid) {
        return next(req);
      }

      const send = (retried: boolean) =>
        next(
          req.clone({
            setHeaders: {
              Authorization: `Bearer ${auth.getAccessToken()}`,
              'Accept-Language': auth.preferredLanguage(),
              ...(retried ? { 'X-Retried-After-Refresh': '1' } : {})
            }
          })
        );

      // A token that looks valid can still be rejected server-side (revoked, rotated,
      // signing key changed). Refresh once and replay the request before giving up;
      // the marker header keeps a second 401 from looping - it falls through to the
      // error interceptor, which ends the session.
      return send(false).pipe(
        catchError((error: unknown) => {
          const canRetry =
            error instanceof HttpErrorResponse &&
            error.status === 401 &&
            !req.headers.has('X-Retried-After-Refresh');

          if (!canRetry) {
            return throwError(() => error);
          }

          return auth.refreshToken().pipe(
            switchMap(() => send(true)),
            catchError(() => throwError(() => error))
          );
        })
      );
    })
  );
};

import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../models';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { TranslateService } from '../i18n/translate.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);
  const auth = inject(AuthService);
  const router = inject(Router);
  const tr = inject(TranslateService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      const problem = extractProblem(error);

      // problem.title / problem.detail come from the API and are already localized via
      // the Accept-Language header - only the SPA's own fallback texts go through t().
      if (error.status === 401 && !req.url.includes('/api/auth/login') && !req.url.includes('/api/auth/refresh')) {
        auth.clearSession();
        notifications.warn(tr.t('common.sessionExpired'), tr.t('common.sessionExpiredTitle'));
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      } else if (error.status === 403) {
        notifications.error(
          problem.detail ?? tr.t('common.forbiddenAction'),
          problem.title ?? tr.t('common.forbiddenTitle')
        );
      } else if (error.status === 0) {
        notifications.error(tr.t('common.connectionError'), tr.t('common.connectionErrorTitle'));
      } else if (error.status >= 500) {
        notifications.error(
          problem.detail ?? tr.t('common.serverError'),
          problem.title ?? tr.t('common.serverErrorTitle')
        );
      } else if (error.status === 429) {
        notifications.warn(
          problem.detail ?? tr.t('common.rateLimited'),
          tr.t('common.rateLimitedTitle')
        );
      } else {
        notifications.error(
          problem.detail ??
            problem.title ??
            tr.t('common.requestFailed', { status: error.status }),
          problem.title ?? tr.t('common.errorTitle')
        );
      }

      if (problem.errors?.length) {
        for (const fieldError of problem.errors) {
          if (fieldError.message && fieldError.message !== problem.detail) {
            notifications.error(fieldError.message, problem.title ?? tr.t('common.validationTitle'));
          }
        }
      }

      return throwError(() => error);
    })
  );
};

function extractProblem(error: HttpErrorResponse): ProblemDetails {
  const body = error.error;

  if (body && typeof body === 'object') {
    return body as ProblemDetails;
  }

  if (typeof body === 'string' && body.trim().length > 0) {
    return { detail: body };
  }

  return { detail: error.message };
}

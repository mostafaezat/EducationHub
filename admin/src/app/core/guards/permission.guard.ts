import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { NotificationService } from '../services/notification.service';
import { AuthService } from '../services/auth.service';
import { TranslateService } from '../i18n/translate.service';
import { isFamilyOnly } from '../nav';

export const permissionGuard = (permission: string): CanActivateFn => {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const notifications = inject(NotificationService);
    const tr = inject(TranslateService);

    if (auth.hasPermission(permission)) {
      return true;
    }

    notifications.error(tr.t('common.forbidden'), tr.t('common.forbiddenTitle'));
    return router.createUrlTree(['/dashboard']);
  };
};

export const roleGuard = (role: string): CanActivateFn => {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const notifications = inject(NotificationService);
    const tr = inject(TranslateService);

    if (auth.hasRole(role)) {
      return true;
    }

    notifications.error(tr.t('common.forbidden'), tr.t('common.forbiddenTitle'));
    return router.createUrlTree(['/dashboard']);
  };
};

/** Allows the route when the user holds at least one of the given roles. */
export const anyRoleGuard = (roles: string[]): CanActivateFn => {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const notifications = inject(NotificationService);
    const tr = inject(TranslateService);

    if (roles.some((role) => auth.hasRole(role))) {
      return true;
    }

    notifications.error(tr.t('common.forbidden'), tr.t('common.forbiddenTitle'));
    return router.createUrlTree(['/dashboard']);
  };
};

/**
 * Staff teaching screens (groups, sessions, homework, exams). Parents and students hold the view
 * permissions - their data is scoped to their own children - but their page is My family, so an
 * account that is only Parent/Student is redirected there instead of seeing staff screens.
 */
export const staffRouteGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return isFamilyOnly(auth.roles()) ? router.createUrlTree(['/my-family']) : true;
};

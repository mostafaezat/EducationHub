import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { NotificationService } from '../services/notification.service';
import { AuthService } from '../services/auth.service';
import { TranslateService } from '../i18n/translate.service';

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

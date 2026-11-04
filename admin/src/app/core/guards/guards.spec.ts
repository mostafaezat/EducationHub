import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree
} from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { AuthServiceStub, NotificationServiceStub } from '../testing/auth-stub';
import { authGuard, guestGuard, requireAuthGuard } from './auth.guard';
import { permissionGuard, roleGuard } from './permission.guard';

describe('route guards', () => {
  let auth: AuthServiceStub;
  let notifications: NotificationServiceStub;
  let router: Router;

  const run = (guard: CanActivateFn, url = '/users') =>
    TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)
    );

  const asUrl = (result: unknown) => router.serializeUrl(result as UrlTree);

  beforeEach(() => {
    auth = new AuthServiceStub();
    notifications = new NotificationServiceStub();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: NotificationService, useValue: notifications }
      ]
    });

    router = TestBed.inject(Router);
  });

  describe('authGuard', () => {
    it('redirects an anonymous visitor to /login and keeps the return url', () => {
      const result = run(authGuard, '/users');

      expect(result instanceof UrlTree).toBeTrue();
      expect(asUrl(result)).toBe('/login?returnUrl=%2Fusers');
    });

    it('sends a user who must change their password to /change-password', () => {
      auth.authenticated.set(true);
      auth.user.set({ mustChangePassword: true });

      expect(asUrl(run(authGuard))).toBe('/change-password');
    });

    it('lets an authenticated user through', () => {
      auth.authenticated.set(true);
      auth.user.set({ mustChangePassword: false });

      expect(run(authGuard)).toBeTrue();
    });
  });

  describe('requireAuthGuard', () => {
    it('allows a forced-password-change user (the change-password page itself)', () => {
      auth.authenticated.set(true);
      auth.user.set({ mustChangePassword: true });

      expect(run(requireAuthGuard)).toBeTrue();
    });

    it('redirects an anonymous visitor to /login', () => {
      expect(asUrl(run(requireAuthGuard, '/change-password'))).toBe(
        '/login?returnUrl=%2Fchange-password'
      );
    });
  });

  describe('guestGuard', () => {
    it('lets an anonymous visitor reach the login page', () => {
      expect(run(guestGuard)).toBeTrue();
    });

    it('bounces a signed-in user to the dashboard', () => {
      auth.authenticated.set(true);

      expect(asUrl(run(guestGuard))).toBe('/dashboard');
    });
  });

  describe('permissionGuard', () => {
    it('allows a user holding the permission', () => {
      auth.permissionSet.add('Users.View');

      expect(run(permissionGuard('Users.View'))).toBeTrue();
      expect(notifications.errors.length).toBe(0);
    });

    it('redirects to the dashboard and shows an error without the permission', () => {
      auth.permissionSet.add('Users.Create');

      expect(asUrl(run(permissionGuard('Users.View')))).toBe('/dashboard');
      expect(notifications.errors.length).toBe(1);
    });
  });

  describe('roleGuard', () => {
    it('allows a user in the role', () => {
      auth.roleSet.add('TenantAdmin');

      expect(run(roleGuard('TenantAdmin'))).toBeTrue();
    });

    it('denies a user outside the role', () => {
      expect(asUrl(run(roleGuard('PlatformAdmin')))).toBe('/dashboard');
      expect(notifications.errors.length).toBe(1);
    });
  });
});

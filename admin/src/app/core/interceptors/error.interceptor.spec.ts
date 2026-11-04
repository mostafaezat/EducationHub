import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { NotificationService } from '../services/notification.service';
import { AuthServiceStub, NotificationServiceStub } from '../testing/auth-stub';
import { errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let auth: AuthServiceStub;
  let notifications: NotificationServiceStub;
  let http: HttpClient;
  let backend: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    auth = new AuthServiceStub();
    notifications = new NotificationServiceStub();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: NotificationService, useValue: notifications }
      ]
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
  });

  afterEach(() => backend.verify());

  const fail = (url: string, status: number, body: object = {}) => {
    http.get(url).subscribe({ error: () => undefined });
    backend.expectOne(url).flush(body, { status, statusText: 'x' });
  };

  it('ends the session and goes to /login on a 401 from a normal endpoint', () => {
    fail('/api/users', 401);

    expect(auth.clearSessionCalls).toBe(1);
    expect(notifications.warnings.length).toBe(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], jasmine.any(Object));
  });

  it('does not end the session on a 401 from the login endpoint (wrong password)', () => {
    fail('/api/auth/login', 401, { title: 'Auth.InvalidCredentials', detail: 'Wrong password' });

    expect(auth.clearSessionCalls).toBe(0);
    expect(notifications.errors[0].detail).toBe('Wrong password');
  });

  it('shows the server-localized detail for a 403', () => {
    fail('/api/users', 403, { title: 'Auth.MustChangePassword', detail: 'Change your password' });

    expect(notifications.errors[0]).toEqual({
      detail: 'Change your password',
      summary: 'Auth.MustChangePassword'
    });
  });

  it('warns (not errors) on a 429', () => {
    fail('/api/auth/forgot-password', 429, { detail: 'Slow down' });

    expect(notifications.warnings[0].detail).toBe('Slow down');
    expect(notifications.errors.length).toBe(0);
  });

  it('lists each field validation message once', () => {
    fail('/api/grades', 400, {
      title: 'Errors.Validation',
      detail: 'Validation failed',
      errors: [
        { field: 'code', message: 'Code is required.' },
        { field: 'name', message: 'Validation failed' }
      ]
    });

    const details = notifications.errors.map((e) => e.detail);
    expect(details).toEqual(['Validation failed', 'Code is required.']);
  });
});

import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { AuthServiceStub } from '../testing/auth-stub';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let auth: AuthServiceStub;
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    auth = new AuthServiceStub();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth }
      ]
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('adds the bearer token and Accept-Language to API calls', () => {
    auth.language.set('ar');

    http.get('/api/users').subscribe();

    const req = backend.expectOne('/api/users');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token-1');
    expect(req.request.headers.get('Accept-Language')).toBe('ar');
    req.flush({});
  });

  it('leaves non-API requests untouched', () => {
    http.get('/assets/logo.svg').subscribe();

    const req = backend.expectOne('/assets/logo.svg');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush('');
  });

  it('never attaches a token to login or refresh', () => {
    http.post('/api/auth/login', {}).subscribe();
    http.post('/api/auth/refresh', {}).subscribe();

    expect(backend.expectOne('/api/auth/login').request.headers.has('Authorization')).toBeFalse();
    expect(backend.expectOne('/api/auth/refresh').request.headers.has('Authorization')).toBeFalse();
    backend.match(() => true).forEach((r) => r.flush({}));
  });

  it('never attaches a staff token to the public registration page', () => {
    http.get('/api/public/registration/some-centre').subscribe();

    expect(backend.expectOne('/api/public/registration/some-centre').request.headers.has('Authorization')).toBeFalse();
    backend.match(() => true).forEach((r) => r.flush({}));
  });

  it('sends the request without a token when no valid session exists', () => {
    auth.ensureValidTokenResult = of(false);

    http.get('/api/users').subscribe();

    const req = backend.expectOne('/api/users');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
  });

  it('refreshes once on a 401 and replays the request with the new token', () => {
    let body: unknown;
    http.get('/api/users').subscribe((b) => (body = b));

    backend.expectOne('/api/users').flush({}, { status: 401, statusText: 'Unauthorized' });

    const retry = backend.expectOne('/api/users');
    expect(auth.refreshCalls).toBe(1);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer token-2');
    expect(retry.request.headers.get('X-Retried-After-Refresh')).toBe('1');
    retry.flush({ ok: true });

    expect(body).toEqual({ ok: true });
  });

  it('surfaces the original 401 when the refresh itself fails', () => {
    auth.refreshResult = () => throwError(() => new Error('refresh failed'));
    let status = 0;

    http.get('/api/users').subscribe({ error: (e) => (status = e.status) });

    backend.expectOne('/api/users').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(auth.refreshCalls).toBe(1);
    expect(status).toBe(401);
  });

  it('does not refresh on errors other than 401', () => {
    let status = 0;

    http.get('/api/users').subscribe({ error: (e) => (status = e.status) });
    backend.expectOne('/api/users').flush({}, { status: 403, statusText: 'Forbidden' });

    expect(auth.refreshCalls).toBe(0);
    expect(status).toBe(403);
  });
});

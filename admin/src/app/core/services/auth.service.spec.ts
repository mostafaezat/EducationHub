import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ACCESS_EXPIRY_KEY,
  ACCESS_TOKEN_KEY,
  REFRESH_EXPIRY_KEY,
  REFRESH_TOKEN_KEY,
  USER_KEY
} from '../app-config';
import { AuthService } from './auth.service';

/** QA-01: a refresh after idle must not end a valid session. */
describe('AuthService token refresh', () => {
  let auth: AuthService;
  let backend: HttpTestingController;

  const user = { id: 'u1', fullName: 'QA', email: 'qa@example.test', roles: ['TenantAdmin'], permissions: [] };
  const refreshed = {
    data: {
      accessToken: 'access-2',
      accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
      refreshToken: 'refresh-2',
      refreshTokenExpiresAtUtc: new Date(Date.now() + 86_400_000).toISOString(),
      tokenType: 'Bearer',
      user
    }
  };

  function seedExpiredSession(): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, 'access-1');
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-1');
    localStorage.setItem(ACCESS_EXPIRY_KEY, String(Date.now() - 60 * 60_000));
    localStorage.setItem(REFRESH_EXPIRY_KEY, String(Date.now() + 86_400_000));
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }

  beforeEach(() => {
    localStorage.clear();
    seedExpiredSession();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });

    auth = TestBed.inject(AuthService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  const refreshRequests = () => backend.match((r) => r.url.endsWith('/api/auth/refresh'));

  it('sends ONE refresh for parallel callers and resolves all of them', () => {
    const results: boolean[] = [];

    for (let i = 0; i < 5; i++) {
      auth.ensureValidToken().subscribe((ok) => results.push(ok));
    }

    const requests = refreshRequests();
    expect(requests.length).toBe(1);
    expect(requests[0].request.body).toEqual({ refreshToken: 'refresh-1' });
    requests[0].flush(refreshed);

    expect(results).toEqual([true, true, true, true, true]);
    expect(auth.getAccessToken()).toBe('access-2');
  });

  it('starts a new refresh once the previous one has settled', () => {
    auth.ensureValidToken().subscribe();
    refreshRequests()[0].flush(refreshed);

    localStorage.setItem(ACCESS_EXPIRY_KEY, String(Date.now() - 1000));
    auth.ensureValidToken().subscribe();

    const second = refreshRequests();
    expect(second.length).toBe(1);
    expect(second[0].request.body).toEqual({ refreshToken: 'refresh-2' });
    second[0].flush(refreshed);
  });

  it('keeps the session when the refresh is rate limited (429)', () => {
    let status = 0;
    auth.ensureValidToken().subscribe({ error: (e) => (status = e.status) });

    refreshRequests()[0].flush({}, { status: 429, statusText: 'Too Many Requests' });

    expect(status).toBe(429);
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1');
    expect(auth.user()).not.toBeNull();
  });

  it('ends the session when the server rejects the refresh token (400)', () => {
    let ok: boolean | undefined;
    auth.ensureValidToken().subscribe((v) => (ok = v));

    refreshRequests()[0].flush({ code: 'Auth.InvalidRefreshToken' }, { status: 400, statusText: 'Bad Request' });

    expect(ok).toBeFalse();
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
    expect(auth.user()).toBeNull();
  });

  it('adopts the pair another tab stored when its own refresh lost the race', () => {
    let ok: boolean | undefined;
    auth.ensureValidToken().subscribe((v) => (ok = v));

    // The other tab rotated the token first and wrote its new pair.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-from-other-tab');
    localStorage.setItem(ACCESS_TOKEN_KEY, 'access-from-other-tab');
    refreshRequests()[0].flush({ code: 'Auth.InvalidRefreshToken' }, { status: 400, statusText: 'Bad Request' });

    expect(ok).toBeTrue();
    expect(auth.getAccessToken()).toBe('access-from-other-tab');
  });
});

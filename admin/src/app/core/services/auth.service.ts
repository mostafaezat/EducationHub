import { HttpErrorResponse, HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, switchMap, tap } from 'rxjs';
import {
  ACCESS_EXPIRY_KEY,
  ACCESS_TOKEN_KEY,
  API_BASE_URL,
  LANGUAGE_KEY,
  REFRESH_EXPIRY_KEY,
  REFRESH_TOKEN_KEY,
  USER_KEY
} from '../app-config';
import {
  AuthResponse,
  ChangePasswordRequest,
  ConfirmEmailRequest,
  ForgotPasswordRequest,
  ForgotPasswordByPhoneRequest,
  ResetPasswordByPhoneRequest,
  LoginRequest,
  RefreshTokenRequest,
  ResendConfirmationRequest,
  ResetPasswordRequest,
  Result,
  UserProfileDto
} from '../models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly userSignal = signal<UserProfileDto | null>(this.readStoredUser());
  readonly user = this.userSignal.asReadonly();

  private readonly languageSignal = signal<string>(this.readStoredLanguage());
  readonly language = this.languageSignal.asReadonly();

  readonly roles = computed(() => new Set(this.userSignal()?.roles ?? []));
  readonly permissions = computed(() => new Set(this.userSignal()?.permissions ?? []));
  readonly isAuthenticated = computed(() => this.userSignal() !== null && !!this.getAccessToken());

  login(request: LoginRequest): Observable<UserProfileDto> {
    return this.http
      .post<Result<AuthResponse>>(`${API_BASE_URL}/api/auth/login`, request)
      .pipe(
        map((result) => result.data as AuthResponse),
        tap((auth) => this.storeSession(auth)),
        map((auth) => auth.user)
      );
  }

  refreshToken(): Observable<void> {
    const refreshToken = this.getRefreshToken();

    if (!refreshToken) {
      return of(undefined);
    }

    return this.http
      .post<Result<AuthResponse>>(`${API_BASE_URL}/api/auth/refresh`, {
        refreshToken
      } satisfies RefreshTokenRequest)
      .pipe(
        map((result) => result.data as AuthResponse),
        tap((auth) => this.storeSession(auth)),
        map(() => undefined)
      );
  }

  loadProfile(): Observable<UserProfileDto> {
    return this.http
      .get<Result<UserProfileDto>>(`${API_BASE_URL}/api/auth/me`)
      .pipe(
        map((result) => result.data as UserProfileDto),
        tap((user) => this.setUser(user))
      );
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/change-password`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. The server always answers 200 so addresses cannot be probed. */
  forgotPassword(request: ForgotPasswordRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/forgot-password`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. Sends a 6-digit reset code by WhatsApp; always succeeds unless rate-limited. */
  forgotPasswordByPhone(request: ForgotPasswordByPhoneRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/forgot-password/phone`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. Completes a phone reset with the WhatsApp code. */
  resetPasswordByPhone(request: ResetPasswordByPhoneRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/reset-password/phone`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. Completes the flow started by the emailed `/auth/reset-password` link. */
  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/reset-password`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. Completes the flow started by the emailed `/auth/confirm-email` link. */
  confirmEmail(request: ConfirmEmailRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/confirm-email`, request)
      .pipe(map(() => undefined));
  }

  /** Anonymous. Always answers 200 so addresses cannot be probed. */
  resendConfirmation(request: ResendConfirmationRequest): Observable<void> {
    return this.http
      .post<Result<unknown>>(`${API_BASE_URL}/api/auth/resend-confirmation`, request)
      .pipe(map(() => undefined));
  }

  logout(): void {
    const refreshToken = this.getRefreshToken();

    this.http
      .post(`${API_BASE_URL}/api/auth/logout`, { refreshToken })
      .subscribe({ error: () => undefined });

    this.clearSession();
  }

  clearSession(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
    localStorage.removeItem(ACCESS_EXPIRY_KEY);
    localStorage.removeItem(REFRESH_EXPIRY_KEY);
    localStorage.removeItem(USER_KEY);
    this.userSignal.set(null);
  }

  ensureValidToken(): Observable<boolean> {
    if (!this.getAccessToken()) {
      return of(false);
    }

    const expiresAt = this.getAccessExpiry();

    if (expiresAt && Date.now() < expiresAt - 60_000) {
      return of(true);
    }

    if (!this.getRefreshToken()) {
      return of(false);
    }

    return this.refreshToken().pipe(
      map(() => true),
      catchError(() => {
        this.clearSession();
        return of(false);
      })
    );
  }

  hasPermission(permission: string): boolean {
    return this.permissions().has(permission);
  }

  hasRole(role: string): boolean {
    return this.roles().has(role);
  }

  setLanguage(code: string): void {
    localStorage.setItem(LANGUAGE_KEY, code);
    this.languageSignal.set(code);
    this.applyDocumentLanguage(code);
  }

  /** Applies the stored language to `<html lang>`/`<html dir>`; safe to call at boot. */
  applyStoredLanguage(): void {
    this.applyDocumentLanguage(this.languageSignal());
  }

  /**
   * Revalidates the stored session when the app starts: refresh the access token if it is
   * about to expire, then pull a fresh profile so permissions revoked while the tab was
   * closed take effect immediately instead of failing one request at a time.
   */
  bootstrapSession(): Observable<void> {
    if (!this.isAuthenticated()) {
      return of(undefined);
    }

    return this.ensureValidToken().pipe(
      switchMap((valid) => (valid ? this.loadProfile() : of(null))),
      map(() => undefined),
      catchError((error: unknown) => {
        // Only an explicit 401 means the stored session is dead. A network blip at boot
        // must not wipe credentials that would work fine on the next navigation.
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.clearSession();
        }

        return of(undefined);
      })
    );
  }

  preferredLanguage(): string {
    return this.languageSignal() || this.userSignal()?.preferredLanguage || 'ar';
  }

  getAccessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  }

  private getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  }

  private getAccessExpiry(): number | null {
    const raw = localStorage.getItem(ACCESS_EXPIRY_KEY);
    const value = raw ? Number(raw) : NaN;
    return Number.isFinite(value) ? value : null;
  }

  private storeSession(auth: AuthResponse): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, auth.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, auth.refreshToken);
    localStorage.setItem(ACCESS_EXPIRY_KEY, String(new Date(auth.accessTokenExpiresAtUtc).getTime()));
    localStorage.setItem(
      REFRESH_EXPIRY_KEY,
      String(new Date(auth.refreshTokenExpiresAtUtc).getTime())
    );
    this.setUser(auth.user);
  }

  private setUser(user: UserProfileDto): void {
    localStorage.setItem(USER_KEY, JSON.stringify(user));
    this.userSignal.set(user);
  }

  private readStoredUser(): UserProfileDto | null {
    const raw = localStorage.getItem(USER_KEY);

    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as UserProfileDto;
    } catch {
      return null;
    }
  }

  private readStoredLanguage(): string {
    return localStorage.getItem(LANGUAGE_KEY) ?? 'ar';
  }

  private applyDocumentLanguage(code: string): void {
    document.documentElement.lang = code;
    document.documentElement.dir = code === 'ar' ? 'rtl' : 'ltr';
  }
}

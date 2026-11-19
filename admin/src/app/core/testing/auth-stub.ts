import { signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { UserProfileDto } from '../models';

/**
 * Minimal stand-in for AuthService in unit tests: the same signal-shaped surface the guards,
 * interceptors and TranslateService read, with every network call replaced by a settable
 * observable. Keeps specs free of HttpClient and localStorage.
 */
export class AuthServiceStub {
  readonly user = signal<Partial<UserProfileDto> | null>(null);
  readonly language = signal('en');
  readonly authenticated = signal(false);
  readonly isAuthenticated = () => this.authenticated();

  accessToken: string | null = 'token-1';
  permissionSet = new Set<string>();
  roleSet = new Set<string>();

  ensureValidTokenResult: Observable<boolean> = of(true);
  refreshCalls = 0;
  refreshResult: () => Observable<void> = () => {
    this.accessToken = 'token-2';
    return of(undefined);
  };
  clearSessionCalls = 0;

  ensureValidToken(): Observable<boolean> {
    return this.ensureValidTokenResult;
  }

  refreshToken(): Observable<void> {
    this.refreshCalls++;
    return this.refreshResult();
  }

  getAccessToken(): string | null {
    return this.accessToken;
  }

  preferredLanguage(): string {
    return this.language();
  }

  hasPermission(permission: string): boolean {
    return this.permissionSet.has(permission);
  }

  /** Same shape as AuthService.roles (a signal of the role set). */
  readonly roles = () => this.roleSet;

  hasRole(role: string): boolean {
    return this.roleSet.has(role);
  }

  clearSession(): void {
    this.clearSessionCalls++;
  }
}

export class NotificationServiceStub {
  readonly errors: { detail: string; summary?: string }[] = [];
  readonly warnings: { detail: string; summary?: string }[] = [];

  error(detail: string, summary?: string): void {
    this.errors.push({ detail, summary });
  }

  warn(detail: string, summary?: string): void {
    this.warnings.push({ detail, summary });
  }

  success(): void {}

  info(): void {}
}

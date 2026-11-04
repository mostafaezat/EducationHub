import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';

/**
 * Landing page for the emailed `/auth/reset-password?email=…&token=…` link
 * (see `WebLinkBuilder.PasswordResetLink` - the route must stay exactly where the
 * backend composes it).
 */
@Component({
  selector: 'app-reset-password',
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, PasswordModule, MessageModule, RouterLink],
  template: `
    <div class="auth-wrap">
      <div class="card auth-card">
        <h1>{{ t('reset.title') }}</h1>
        @if (email()) {
          <p class="sub">{{ t('reset.subtitle', { email: email()! }) }}</p>
        }

        @if (linkValid()) {
          <form [formGroup]="form" (ngSubmit)="submit()">
            <div class="field">
              <label for="new">{{ t('reset.newPassword') }}</label>
              <p-password inputId="new" formControlName="newPassword" [toggleMask]="true" />
              <small class="hint">{{ t('reset.passwordHint') }}</small>
            </div>
            <div class="field">
              <label for="confirm">{{ t('reset.confirmPassword') }}</label>
              <p-password
                inputId="confirm"
                formControlName="confirmPassword"
                [feedback]="false"
                [toggleMask]="true"
              />
            </div>

            @if (error()) {
              <p-message severity="error" [text]="error()!" />
            }

            <div class="auth-actions">
              <p-button
                type="submit"
                [label]="t('reset.submit')"
                icon="pi pi-key"
                [loading]="loading()"
              />
              <p-button
                type="button"
                severity="secondary"
                [text]="true"
                [label]="t('common.cancel')"
                routerLink="/login"
              />
            </div>
          </form>
        } @else {
          <p-message severity="warn" [text]="t('reset.invalidLink')" />
          <div class="auth-actions spaced">
            <p-button
              type="button"
              [label]="t('reset.requestNew')"
              icon="pi pi-envelope"
              routerLink="/auth/forgot-password"
            />
            <p-button
              type="button"
              severity="secondary"
              [text]="true"
              [label]="t('common.back')"
              routerLink="/login"
            />
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .auth-wrap {
        min-height: 100vh;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 1.5rem;
        background: #f4f6fa;
      }
      .auth-card {
        width: 100%;
        max-width: 460px;
      }
      h1 {
        margin-top: 0;
        font-size: 1.3rem;
      }
      .sub {
        color: #64748b;
        font-size: 0.9rem;
        margin-top: -0.4rem;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }
      .hint {
        display: block;
        color: #64748b;
        font-size: 0.8rem;
        margin-top: 0.35rem;
      }
      .auth-actions {
        display: flex;
        gap: 0.75rem;
        align-items: center;
      }
      .auth-actions.spaced {
        margin-top: 1rem;
      }
    `
  ]
})
export class ResetPasswordComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly email = signal<string | null>(null);
  protected readonly linkValid = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]]
  });

  constructor() {
    const query = this.route.snapshot.queryParamMap;
    const email = query.get('email');
    const token = query.get('token');

    this.email.set(email);
    this.linkValid.set(!!email && !!token);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();

    if (value.newPassword !== value.confirmPassword) {
      this.error.set(this.t('reset.mismatch'));
      return;
    }

    if (!/\d/.test(value.newPassword)) {
      this.error.set(this.t('reset.needDigit'));
      return;
    }

    const token = this.route.snapshot.queryParamMap.get('token');
    const email = this.email();

    if (!token || !email) {
      this.error.set(this.t('reset.invalidLink'));
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth
      .resetPassword({
        email,
        resetToken: token,
        newPassword: value.newPassword,
        confirmPassword: value.confirmPassword
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          void this.router.navigate(['/login']);
        },
        error: (err: unknown) => {
          this.loading.set(false);
          const problem =
            err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
              ? (err.error as { detail?: string })
              : null;
          this.error.set(problem?.detail ?? this.t('reset.failed'));
        }
      });
  }
}

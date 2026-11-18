import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { SUPPORTED_LANGUAGES } from '../../core/app-config';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-login',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    InputTextModule,
    PasswordModule,
    CheckboxModule,
    MessageModule,
    RouterLink
  ],
  template: `
    <div class="login-wrap">
      <div class="login-card">
        <div class="login-head">
          <i class="pi pi-graduation-cap"></i>
          <h1>EducationHub</h1>
          <p>{{ t('login.subtitle') }}</p>
        </div>

        <form [formGroup]="form" (ngSubmit)="submit()" class="login-form">
          <div class="field">
            <label for="email">{{ t('login.form.identifierLabel') }}</label>
            <input
              id="email"
              type="text"
              pInputText
              formControlName="email"
              autocomplete="username"
              [placeholder]="t('login.form.identifierPlaceholder')"
            />
          </div>

          <div class="field">
            <label for="password">{{ t('login.form.passwordLabel') }}</label>
            <p-password
              inputId="password"
              formControlName="password"
              [feedback]="false"
              [toggleMask]="true"
              autocomplete="current-password"
            />
          </div>

          <div class="login-row">
            <p-checkbox formControlName="rememberMe" [binary]="true" inputId="rememberMe" />
            <label for="rememberMe">{{ t('login.form.rememberMe') }}</label>
            <a class="forgot-link" routerLink="/auth/forgot-password">{{ t('login.forgotPassword') }}</a>
          </div>

          @if (error()) {
            <p-message severity="error" [text]="error()!" />
          }

          @if (emailNotConfirmed()) {
            <div class="resend-box">
              <p-message severity="warn" [text]="t('login.emailNotConfirmedHint')" />
              <p-button
                type="button"
                severity="secondary"
                size="small"
                [label]="t('confirm.resend')"
                [loading]="resending()"
                (onClick)="resendConfirmation()"
              />
            </div>
          }

          <p-button
            type="submit"
            [label]="t('login.signIn')"
            icon="pi pi-sign-in"
            styleClass="w-full"
            [loading]="loading()"
          />
        </form>

        <div class="login-lang">
          @for (lang of languages; track lang.code) {
            <button
              type="button"
              class="lang-btn"
              [class.active]="auth.language() === lang.code"
              (click)="setLanguage(lang.code)"
            >
              {{ lang.label }}
            </button>
          }
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .login-wrap {
        min-height: 100vh;
        display: flex;
        align-items: center;
        justify-content: center;
        background: linear-gradient(135deg, #10233f 0%, #1d3f6e 100%);
        padding: 1.5rem;
      }
      .login-card {
        width: 100%;
        max-width: 400px;
        background: #fff;
        border-radius: 16px;
        padding: 2rem;
        box-shadow: 0 24px 60px rgba(10, 22, 40, 0.35);
      }
      .login-head {
        text-align: center;
        margin-bottom: 1.5rem;
      }
      .login-head i {
        font-size: 2.5rem;
        color: #2563eb;
      }
      .login-head h1 {
        margin: 0.4rem 0 0.1rem;
        font-size: 1.5rem;
      }
      .login-head p {
        margin: 0;
        color: #64748b;
        font-size: 0.9rem;
      }
      .login-form {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }
      .login-form .field input,
      .login-form .field p-password {
        width: 100%;
      }
      .login-row {
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      .login-row label {
        font-size: 0.88rem;
        color: #334155;
      }
      .forgot-link {
        margin-inline-start: auto;
        font-size: 0.82rem;
        color: #2563eb;
        text-decoration: none;
      }
      .forgot-link:hover {
        text-decoration: underline;
      }
      .resend-box {
        display: flex;
        flex-direction: column;
        gap: 0.6rem;
        align-items: flex-start;
      }
      .login-lang {
        display: flex;
        justify-content: center;
        gap: 0.5rem;
        margin-top: 1.5rem;
      }
      .lang-btn {
        background: #f1f5f9;
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        padding: 0.3rem 0.8rem;
        cursor: pointer;
        font-size: 0.82rem;
        color: #475569;
      }
      .lang-btn.active {
        background: #2563eb;
        border-color: #2563eb;
        color: #fff;
      }
    `
  ]
})
export class LoginComponent {
  protected readonly auth = inject(AuthService);
  protected readonly t = inject(TranslateService).t;
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly emailNotConfirmed = signal(false);
  protected readonly resending = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    // An e-mail address or a mobile number (parents can sign in with either).
    email: ['', [Validators.required]],
    password: ['', [Validators.required]],
    rememberMe: [false]
  });

  protected setLanguage(code: string): void {
    this.auth.setLanguage(code);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: (user) => {
        this.loading.set(false);
        this.notifications.success(this.t('login.welcomeBack', { name: user.fullName }));

        if (user.mustChangePassword) {
          void this.router.navigate(['/change-password']);
          return;
        }

        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        void this.router.navigateByUrl(returnUrl ?? '/dashboard');
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.emailNotConfirmed.set(false);

        const problem =
          err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
            ? (err.error as { detail?: string; title?: string })
            : null;

        this.error.set(problem?.detail ?? this.t('login.signInFailed'));
        this.emailNotConfirmed.set(problem?.title === 'Account.EmailNotConfirmed');
      }
    });
  }

  protected resendConfirmation(): void {
    const emailControl = this.form.controls.email;

    if (emailControl.invalid) {
      emailControl.markAsTouched();
      return;
    }

    const email = this.form.getRawValue().email;
    this.resending.set(true);

    this.auth.resendConfirmation({ email }).subscribe({
      next: () => {
        this.resending.set(false);
        this.notifications.info(this.t('confirm.resendSent', { email }));
      },
      error: () => {
        this.resending.set(false);
        this.notifications.error(this.t('forgot.failed'));
      }
    });
  }
}

import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { SelectButtonModule } from 'primeng/selectbutton';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';

type ResetChannel = 'email' | 'phone';

/**
 * Starts a password reset. Email: a reset link is mailed. Mobile (phone-only parents): a 6-digit
 * code is sent by WhatsApp and entered here together with the new password.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    InputTextModule,
    MessageModule,
    PasswordModule,
    SelectButtonModule,
    RouterLink
  ],
  template: `
    <div class="auth-wrap">
      <div class="card auth-card">
        <h1>{{ t('forgot.title') }}</h1>

        <p-selectbutton
          [options]="channels()"
          [ngModel]="channel()"
          (ngModelChange)="switchChannel($event)"
          optionLabel="label"
          optionValue="value"
          [allowEmpty]="false"
          styleClass="channel"
        />

        @if (channel() === 'email') {
          <p class="sub">{{ t('forgot.subtitle') }}</p>

          <form [formGroup]="form" (ngSubmit)="submit()">
            <div class="field">
              <label for="email">{{ t('common.email') }}</label>
              <input id="email" type="email" pInputText formControlName="email" autocomplete="username" />
            </div>

            @if (sent()) {
              <p-message severity="success" [text]="sent()!" />
            }
            @if (error()) {
              <p-message severity="error" [text]="error()!" />
            }

            <div class="auth-actions">
              <p-button type="submit" [label]="t('forgot.submit')" icon="pi pi-envelope" [loading]="loading()" />
              <p-button
                type="button"
                severity="secondary"
                [text]="true"
                [label]="t('forgot.backToLogin')"
                routerLink="/login"
              />
            </div>
          </form>
        } @else {
          <p class="sub">{{ t('forgot.phoneSubtitle') }}</p>

          @if (!codeSent()) {
            <form [formGroup]="phoneForm" (ngSubmit)="sendCode()">
              <div class="field">
                <label for="phone">{{ t('forgot.phone') }}</label>
                <input
                  id="phone"
                  type="tel"
                  dir="ltr"
                  inputmode="tel"
                  pInputText
                  formControlName="phone"
                  autocomplete="tel"
                  [placeholder]="t('forgot.phonePlaceholder')"
                />
              </div>

              @if (error()) {
                <p-message severity="error" [text]="error()!" />
              }

              <div class="auth-actions">
                <p-button type="submit" [label]="t('forgot.sendCode')" icon="pi pi-whatsapp" [loading]="loading()" />
                <p-button
                  type="button"
                  severity="secondary"
                  [text]="true"
                  [label]="t('forgot.backToLogin')"
                  routerLink="/login"
                />
              </div>
            </form>
          } @else {
            <form [formGroup]="codeForm" (ngSubmit)="resetByPhone()">
              <p-message severity="info" [text]="t('forgot.codeSent', { phone: phoneForm.getRawValue().phone })" />

              <div class="field">
                <label for="code">{{ t('forgot.code') }}</label>
                <input
                  id="code"
                  pInputText
                  dir="ltr"
                  inputmode="numeric"
                  maxlength="6"
                  autocomplete="one-time-code"
                  formControlName="code"
                  class="code"
                />
                @if (codeForm.controls.code.touched && codeForm.controls.code.invalid) {
                  <small class="err">{{ t('forgot.codeInvalid') }}</small>
                }
              </div>

              <div class="field">
                <label for="new">{{ t('reset.newPassword') }}</label>
                <p-password inputId="new" formControlName="newPassword" [toggleMask]="true" [feedback]="false" />
                <small class="hint">{{ t('reset.passwordHint') }}</small>
              </div>

              <div class="field">
                <label for="confirm">{{ t('reset.confirmPassword') }}</label>
                <p-password inputId="confirm" formControlName="confirmPassword" [toggleMask]="true" [feedback]="false" />
              </div>

              @if (mismatch()) {
                <p-message severity="warn" [text]="t('reset.mismatch')" />
              }
              @if (error()) {
                <p-message severity="error" [text]="error()!" />
              }
              @if (done()) {
                <p-message severity="success" [text]="t('reset.success')" />
              }

              <div class="auth-actions wrap">
                @if (done()) {
                  <p-button type="button" [label]="t('forgot.backToLogin')" icon="pi pi-sign-in" routerLink="/login" />
                } @else {
                  <p-button type="submit" [label]="t('reset.submit')" icon="pi pi-lock" [loading]="loading()" />
                  <p-button type="button" severity="secondary" [text]="true" [label]="t('forgot.resendCode')" (onClick)="sendCode()" />
                  <p-button type="button" severity="secondary" [text]="true" [label]="t('forgot.changeNumber')" (onClick)="changeNumber()" />
                }
              </div>
            </form>
          }
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
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
      }
      .hint {
        color: #64748b;
      }
      .err {
        color: #dc2626;
      }
      .code {
        letter-spacing: 0.4em;
        font-size: 1.2rem;
        text-align: center;
      }
      .auth-actions {
        display: flex;
        gap: 0.75rem;
        align-items: center;
      }
      .auth-actions.wrap {
        flex-wrap: wrap;
      }
      :host ::ng-deep .channel {
        margin: 0.5rem 0;
      }
      :host ::ng-deep p-password,
      :host ::ng-deep p-password input {
        width: 100%;
      }
    `
  ]
})
export class ForgotPasswordComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly channel = signal<ResetChannel>('email');
  protected readonly channels = computed(() => [
    { label: this.t('forgot.byEmail'), value: 'email' as ResetChannel },
    { label: this.t('forgot.byPhone'), value: 'phone' as ResetChannel }
  ]);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sent = signal<string | null>(null);
  protected readonly codeSent = signal(false);
  protected readonly done = signal(false);
  protected readonly mismatch = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

  protected readonly phoneForm = this.fb.nonNullable.group({
    phone: ['', [Validators.required, Validators.pattern(/^[+0-9 ()-]{8,20}$/)]]
  });

  protected readonly codeForm = this.fb.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[0-9]{6}$/)]],
    newPassword: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/(?=.*\d)(?=.*[a-z])/)]],
    confirmPassword: ['', Validators.required]
  });

  protected switchChannel(channel: ResetChannel): void {
    this.channel.set(channel);
    this.error.set(null);
    this.sent.set(null);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const email = this.form.getRawValue().email;
    this.loading.set(true);
    this.error.set(null);
    this.sent.set(null);

    this.auth.forgotPassword({ email }).subscribe({
      next: () => {
        this.loading.set(false);
        this.sent.set(this.t('forgot.sent', { email }));
      },
      error: (err: unknown) => this.fail(err, 'forgot.failed')
    });
  }

  protected sendCode(): void {
    if (this.phoneForm.invalid) {
      this.phoneForm.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth.forgotPasswordByPhone({ phone: this.phoneForm.getRawValue().phone.trim() }).subscribe({
      next: () => {
        this.loading.set(false);
        this.codeSent.set(true);
      },
      error: (err: unknown) => this.fail(err, 'forgot.failed')
    });
  }

  protected resetByPhone(): void {
    const value = this.codeForm.getRawValue();
    this.mismatch.set(value.newPassword !== value.confirmPassword);

    if (this.codeForm.invalid || this.mismatch()) {
      this.codeForm.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth
      .resetPasswordByPhone({
        phone: this.phoneForm.getRawValue().phone.trim(),
        code: value.code,
        newPassword: value.newPassword,
        confirmPassword: value.confirmPassword
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.done.set(true);
        },
        error: (err: unknown) => this.fail(err, 'reset.failed')
      });
  }

  protected changeNumber(): void {
    this.codeSent.set(false);
    this.done.set(false);
    this.error.set(null);
    this.codeForm.reset();
  }

  protected backToLogin(): void {
    void this.router.navigate(['/login']);
  }

  /** Shows the server's (already localized) message when it has one. */
  private fail(err: unknown, fallbackKey: string): void {
    this.loading.set(false);
    const problem =
      err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
        ? (err.error as { detail?: string })
        : null;
    this.error.set(problem?.detail ?? this.t(fallbackKey));
  }
}

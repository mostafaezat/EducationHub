import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, MessageModule, RouterLink],
  template: `
    <div class="auth-wrap">
      <div class="card auth-card">
        <h1>{{ t('forgot.title') }}</h1>
        <p class="sub">{{ t('forgot.subtitle') }}</p>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <div class="field">
            <label for="email">{{ t('common.email') }}</label>
            <input
              id="email"
              type="email"
              pInputText
              formControlName="email"
              autocomplete="username"
            />
          </div>

          @if (sent()) {
            <p-message severity="success" [text]="sent()!" />
          }
          @if (error()) {
            <p-message severity="error" [text]="error()!" />
          }

          <div class="auth-actions">
            <p-button
              type="submit"
              [label]="t('forgot.submit')"
              icon="pi pi-envelope"
              [loading]="loading()"
            />
            <p-button
              type="button"
              severity="secondary"
              [text]="true"
              [label]="t('forgot.backToLogin')"
              routerLink="/login"
            />
          </div>
        </form>
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
      .auth-actions {
        display: flex;
        gap: 0.75rem;
        align-items: center;
      }
    `
  ]
})
export class ForgotPasswordComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sent = signal<string | null>(null);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]]
  });

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
      error: (err: unknown) => {
        this.loading.set(false);
        // The endpoint always answers 200 unless rate-limited; anything else shows the
        // server's (already localized) message when it has one.
        const problem =
          err instanceof HttpErrorResponse && err.error && typeof err.error === 'object'
            ? (err.error as { detail?: string })
            : null;
        this.error.set(problem?.detail ?? this.t('forgot.failed'));
      }
    });
  }

  protected backToLogin(): void {
    void this.router.navigate(['/login']);
  }
}

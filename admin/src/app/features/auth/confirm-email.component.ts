import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

type ConfirmStatus = 'pending' | 'success' | 'failed';

/**
 * Landing page for the emailed `/auth/confirm-email?email=…&token=…` link
 * (see `WebLinkBuilder.EmailConfirmationLink`). The confirmation POSTs
 * automatically on arrival - the user clicked a link, not a button - with a
 * failure state that offers a resend.
 */
@Component({
  selector: 'app-confirm-email',
  imports: [CommonModule, ButtonModule, MessageModule, RouterLink],
  template: `
    <div class="auth-wrap">
      <div class="card auth-card">
        <h1>{{ t('confirm.title') }}</h1>

        @switch (status()) {
          @case ('pending') {
            <p class="sub">{{ t('confirm.confirming') }}</p>
          }
          @case ('success') {
            <p-message severity="success" [text]="t('confirm.success')" />
            <div class="auth-actions spaced">
              <p-button
                type="button"
                [label]="t('login.signIn')"
                icon="pi pi-sign-in"
                routerLink="/login"
              />
            </div>
          }
          @case ('failed') {
            <p-message severity="error" [text]="failureDetail() ?? t('confirm.failed')" />
            <div class="auth-actions spaced">
              @if (email()) {
                <p-button
                  type="button"
                  severity="secondary"
                  [label]="t('confirm.resend')"
                  icon="pi pi-envelope"
                  [loading]="resending()"
                  (onClick)="resend()"
                />
              }
              <p-button
                type="button"
                severity="secondary"
                [text]="true"
                [label]="t('confirm.backToLogin')"
                routerLink="/login"
              />
            </div>
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
export class ConfirmEmailComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly notifications = inject(NotificationService);

  protected readonly status = signal<ConfirmStatus>('pending');
  protected readonly failureDetail = signal<string | null>(null);
  protected readonly resending = signal(false);
  protected readonly email = signal<string | null>(null);

  private confirming = false;

  constructor() {
    const query = this.route.snapshot.queryParamMap;
    this.email.set(query.get('email'));
    this.confirm(query.get('email'), query.get('token'));
  }

  private confirm(email: string | null, token: string | null): void {
    if (this.confirming) {
      return;
    }

    this.confirming = true;

    if (!email || !token) {
      this.status.set('failed');
      return;
    }

    this.auth.confirmEmail({ email, token }).subscribe({
      next: () => this.status.set('success'),
      error: (err: unknown) => {
        this.status.set('failed');

        if (err && typeof err === 'object' && 'error' in err) {
          const problem = (err as { error?: unknown }).error;
          if (problem && typeof problem === 'object' && 'detail' in problem) {
            const detail = (problem as { detail?: unknown }).detail;
            if (typeof detail === 'string' && detail.length > 0) {
              this.failureDetail.set(detail);
            }
          }
        }
      }
    });
  }

  protected resend(): void {
    const email = this.email();

    if (!email) {
      return;
    }

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

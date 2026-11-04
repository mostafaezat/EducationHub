import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { finalize, switchMap } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-change-password',
  imports: [CommonModule, ReactiveFormsModule, ButtonModule, InputTextModule, PasswordModule, MessageModule],
  template: `
    <div class="cp-wrap">
      <div class="card cp-card">
        <h1>{{ t('changePassword.title') }}</h1>
        @if (forced()) {
          <p class="forced-note">
            {{ t('changePassword.forcedNote') }}
          </p>
        }

        <form [formGroup]="form" (ngSubmit)="submit()">
          <div class="field">
            <label for="current">{{ t('changePassword.currentPassword') }}</label>
            <p-password inputId="current" formControlName="currentPassword" [feedback]="false" [toggleMask]="true" />
          </div>
          <div class="field">
            <label for="new">{{ t('changePassword.newPassword') }}</label>
            <p-password inputId="new" formControlName="newPassword" [toggleMask]="true" />
            <small class="hint">{{ t('changePassword.passwordHint') }}</small>
          </div>
          <div class="field">
            <label for="confirm">{{ t('changePassword.confirmPassword') }}</label>
            <p-password inputId="confirm" formControlName="confirmPassword" [feedback]="false" [toggleMask]="true" />
          </div>

          @if (error()) {
            <p-message severity="error" [text]="error()!" />
          }

          <div class="cp-actions">
            <p-button
              type="submit"
              [label]="t('changePassword.submit')"
              icon="pi pi-key"
              [loading]="loading()"
            />
            @if (!forced()) {
              <p-button
                type="button"
                [label]="t('common.cancel')"
                severity="secondary"
                [text]="true"
                (onClick)="cancel()"
              />
            }
          </div>
        </form>
      </div>
    </div>
  `,
  styles: [
    `
      .cp-wrap {
        min-height: 100vh;
        display: flex;
        align-items: center;
        justify-content: center;
        padding: 1.5rem;
        background: #f4f6fa;
      }
      .cp-card {
        width: 100%;
        max-width: 460px;
      }
      h1 {
        margin-top: 0;
        font-size: 1.3rem;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 1rem;
      }
      .field p-password {
        width: 100%;
      }
      .forced-note {
        background: #fff7ed;
        border: 1px solid #fed7aa;
        color: #9a3412;
        padding: 0.6rem 0.8rem;
        border-radius: 8px;
        font-size: 0.85rem;
      }
      .cp-actions {
        display: flex;
        gap: 0.75rem;
        align-items: center;
      }
    `
  ]
})
export class ChangePasswordComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly forced = signal(this.auth.user()?.mustChangePassword === true);

  protected readonly form = this.fb.nonNullable.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required]]
  });

  protected cancel(): void {
    void this.router.navigate(['/dashboard']);
  }

  protected submit(): void {
    const value = this.form.getRawValue();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (value.newPassword !== value.confirmPassword) {
      this.error.set(this.t('changePassword.mismatch'));
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.auth
      .changePassword({
        currentPassword: value.currentPassword,
        newPassword: value.newPassword,
        confirmPassword: value.confirmPassword
      })
      .pipe(
        switchMap(() => this.auth.refreshToken()),
        switchMap(() => this.auth.loadProfile()),
        finalize(() => this.loading.set(false))
      )
      .subscribe({
        next: () => {
          this.notifications.success(this.t('changePassword.success'));
          void this.router.navigate(['/dashboard']);
        },
        error: () => {
          this.error.set(this.t('changePassword.updateFailed'));
        }
      });
  }
}

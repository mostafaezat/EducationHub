import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { TenantSettingsDto, UpdateTenantSettingsRequest } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { SettingsService } from '../../core/services/settings.service';

@Component({
  selector: 'app-settings',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    InputNumberModule,
    InputTextModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('settings.title') }}</h1>
        <div class="subtitle">{{ t('settings.subtitle') }}</div>
      </div>
      @if (canUpdate()) {
        <p-button [label]="t('settings.saveChanges')" icon="pi pi-save" [loading]="saving()" [disabled]="!loaded()" (onClick)="save()" />
      }
    </div>

    @if (loadError()) {
      <div class="card error-panel">
        <i class="pi pi-exclamation-triangle"></i>
        <div>
          <strong>{{ t('settings.loadFailed') }}</strong>
          <p>{{ loadError() }}</p>
          <p-button [label]="t('common.retry')" icon="pi pi-refresh" [text]="true" (onClick)="load()" />
        </div>
      </div>
    }

    @if (loading()) {
      <div class="card state-panel"><i class="pi pi-spin pi-spinner"></i>{{ t('settings.loading') }}</div>
    }

    @if (loaded()) {
      <form [formGroup]="form" class="settings-grid">
        <div class="card group">
          <h2>{{ t('settings.groups.attendance') }}</h2>
          <div class="row">
            <span>{{ t('settings.attendance.notifyParentOnAbsence') }}</span>
            <p-toggleswitch formControlName="notifyParentOnAbsence" />
          </div>
          <div class="row">
            <span>{{ t('settings.attendance.notifyParentOnSessionCancellation') }}</span>
            <p-toggleswitch formControlName="notifyParentOnSessionCancellation" />
          </div>
          <div class="row">
            <span>{{ t('settings.attendance.lateThresholdMinutes') }}</span>
            <p-inputNumber formControlName="lateThresholdMinutes" [min]="0" [max]="240" />
          </div>
          <div class="row">
            <span>{{ t('settings.attendance.requireExcuseReason') }}</span>
            <p-toggleswitch formControlName="requireExcuseReason" />
          </div>
        </div>

        <div class="card group">
          <h2>{{ t('settings.groups.fees') }}</h2>
          <div class="row">
            <span>{{ t('settings.fees.autoGenerateMonthlyFees') }}</span>
            <p-toggleswitch formControlName="autoGenerateMonthlyFees" />
          </div>
          <div class="row">
            <span>{{ t('settings.fees.feeDueDayOfMonth') }}</span>
            <p-inputNumber formControlName="feeDueDayOfMonth" [min]="1" [max]="28" />
          </div>
          <div class="row">
            <span>{{ t('settings.fees.gracePeriodDays') }}</span>
            <p-inputNumber formControlName="gracePeriodDays" [min]="0" [max]="60" />
          </div>
          <div class="row">
            <span>{{ t('settings.fees.siblingDiscountPercentage') }}</span>
            <p-inputNumber formControlName="siblingDiscountPercentage" [min]="0" [max]="100" />
          </div>
        </div>

        <div class="card group">
          <h2>{{ t('settings.groups.grading') }}</h2>
          <div class="row">
            <span>{{ t('settings.grading.defaultPassingPercentage') }}</span>
            <p-inputNumber formControlName="defaultPassingPercentage" [min]="0" [max]="100" />
          </div>
          <div class="row">
            <span>{{ t('settings.grading.useCustomGradingScale') }}</span>
            <p-toggleswitch formControlName="useCustomGradingScale" />
          </div>
        </div>

        <div class="card group">
          <h2>{{ t('settings.groups.notifications') }}</h2>
          <div class="row">
            <span>{{ t('settings.notifications.notifyParentOnFeeDue') }}</span>
            <p-toggleswitch formControlName="notifyParentOnFeeDue" />
          </div>
          <div class="row">
            <span>{{ t('settings.notifications.notifyParentOnPaymentReceived') }}</span>
            <p-toggleswitch formControlName="notifyParentOnPaymentReceived" />
          </div>
          <div class="row">
            <span>{{ t('settings.notifications.notifyParentOnHomeworkAssigned') }}</span>
            <p-toggleswitch formControlName="notifyParentOnHomeworkAssigned" />
          </div>
          <div class="row">
            <span>{{ t('settings.notifications.notifyParentOnExamResultPublished') }}</span>
            <p-toggleswitch formControlName="notifyParentOnExamResultPublished" />
          </div>
          <div class="row">
            <span>{{ t('settings.notifications.notifyParentOnMakeupClassScheduled') }}</span>
            <p-toggleswitch formControlName="notifyParentOnMakeupClassScheduled" />
          </div>
        </div>

        <div class="card group">
          <h2>{{ t('settings.groups.localization') }}</h2>
          <div class="row">
            <span>{{ t('settings.localization.dateFormat') }}</span>
            <input pInputText formControlName="dateFormat" />
          </div>
          <div class="row">
            <span>{{ t('settings.localization.currencySymbol') }}</span>
            <input pInputText formControlName="currencySymbol" />
          </div>
        </div>
      </form>
    }
  `,
  styles: [
    `
      .settings-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
        gap: 1rem;
      }
      .group {
        display: flex;
        flex-direction: column;
        gap: 0.75rem;
      }
      .group h2 {
        margin: 0;
        font-size: 1rem;
        color: #0f172a;
      }
      .row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 1rem;
        font-size: 0.9rem;
        color: #334155;
      }
      .error-panel {
        display: flex;
        gap: 0.75rem;
        color: #b91c1c;
      }
      .error-panel p {
        margin: 0.25rem 0 0.5rem;
        color: #475569;
      }
    `
  ]
})
export class SettingsComponent implements OnInit {
  private readonly service = inject(SettingsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);

  protected readonly t = inject(TranslateService).t;

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly canUpdate = signal(this.auth.hasPermission('Settings.Update'));

  protected readonly form = this.fb.nonNullable.group({
    notifyParentOnAbsence: [false],
    notifyParentOnSessionCancellation: [false],
    lateThresholdMinutes: [15],
    requireExcuseReason: [false],
    autoGenerateMonthlyFees: [false],
    feeDueDayOfMonth: [1],
    gracePeriodDays: [0],
    siblingDiscountPercentage: [0],
    defaultPassingPercentage: [50],
    useCustomGradingScale: [false],
    notifyParentOnFeeDue: [false],
    notifyParentOnPaymentReceived: [false],
    notifyParentOnHomeworkAssigned: [false],
    notifyParentOnExamResultPublished: [false],
    notifyParentOnMakeupClassScheduled: [false],
    dateFormat: ['dd/MM/yyyy'],
    currencySymbol: ['EGP']
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .get()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (settings) => {
          this.apply(settings);
          this.loaded.set(true);
        },
        error: (problem: { error?: { code?: string; detail?: string }; message?: string }) => {
          this.loaded.set(false);
          this.loadError.set(
            problem?.error?.detail ??
              problem?.message ??
              this.t('settings.loadErrorFallback')
          );
        }
      });
  }

  private apply(settings: TenantSettingsDto): void {
    this.form.reset({
      notifyParentOnAbsence: settings.notifyParentOnAbsence,
      notifyParentOnSessionCancellation: settings.notifyParentOnSessionCancellation,
      lateThresholdMinutes: settings.lateThresholdMinutes,
      requireExcuseReason: settings.requireExcuseReason,
      autoGenerateMonthlyFees: settings.autoGenerateMonthlyFees,
      feeDueDayOfMonth: settings.feeDueDayOfMonth,
      gracePeriodDays: settings.gracePeriodDays,
      siblingDiscountPercentage: settings.siblingDiscountPercentage,
      defaultPassingPercentage: settings.defaultPassingPercentage,
      useCustomGradingScale: settings.useCustomGradingScale,
      notifyParentOnFeeDue: settings.notifyParentOnFeeDue,
      notifyParentOnPaymentReceived: settings.notifyParentOnPaymentReceived,
      notifyParentOnHomeworkAssigned: settings.notifyParentOnHomeworkAssigned,
      notifyParentOnExamResultPublished: settings.notifyParentOnExamResultPublished,
      notifyParentOnMakeupClassScheduled: settings.notifyParentOnMakeupClassScheduled,
      dateFormat: settings.dateFormat,
      currencySymbol: settings.currencySymbol
    });
  }

  protected save(): void {
    if (!this.loaded()) {
      return;
    }

    const value = this.form.getRawValue();
    const request: UpdateTenantSettingsRequest = { ...value };

    this.saving.set(true);
    this.service
      .update(request)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: (settings) => {
          this.apply(settings);
          this.notifications.success(this.t('settings.savedMessage'));
        },
        error: (problem: { error?: { detail?: string }; message?: string }) => {
          this.notifications.error(
            problem?.error?.detail ?? problem?.message ?? this.t('settings.saveErrorFallback')
          );
        }
      });
  }
}

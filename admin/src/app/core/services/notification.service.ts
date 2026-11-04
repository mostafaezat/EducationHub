import { inject, Injectable } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { Observable, map } from 'rxjs';
import { TranslateService } from '../i18n/translate.service';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly messages = inject(MessageService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly tr = inject(TranslateService);

  protected readonly t = this.tr.t;

  success(detail: string, summary?: string): void {
    this.messages.add({ severity: 'success', summary: summary ?? this.t('common.success'), detail, life: 4000 });
  }

  info(detail: string, summary?: string): void {
    this.messages.add({ severity: 'info', summary: summary ?? this.t('common.information'), detail, life: 4000 });
  }

  warn(detail: string, summary?: string): void {
    this.messages.add({ severity: 'warn', summary: summary ?? this.t('common.warning'), detail, life: 5000 });
  }

  error(detail: string, summary?: string): void {
    this.messages.add({ severity: 'error', summary: summary ?? this.t('common.errorTitle'), detail, life: 6000 });
  }

  confirm(options: {
    message: string;
    header?: string;
    acceptLabel?: string;
    rejectLabel?: string;
    acceptButtonStyleClass?: string;
    icon?: string;
  }): Observable<boolean> {
    return new Observable<boolean>((observer) => {
      this.confirmation.confirm({
        message: options.message,
        header: options.header ?? this.t('common.pleaseConfirm'),
        icon: options.icon ?? 'pi pi-exclamation-triangle',
        acceptLabel: options.acceptLabel ?? this.t('common.confirm'),
        rejectLabel: options.rejectLabel ?? this.t('common.cancel'),
        acceptButtonStyleClass: options.acceptButtonStyleClass,
        accept: () => {
          observer.next(true);
          observer.complete();
        },
        reject: () => {
          observer.next(false);
          observer.complete();
        }
      });
    }).pipe(map((value) => value));
  }
}

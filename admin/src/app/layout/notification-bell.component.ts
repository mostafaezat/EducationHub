import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, of, switchMap, timer } from 'rxjs';
import { TranslateService } from '../core/i18n/translate.service';
import { AppNotificationDto, NotificationFeedDto } from '../core/models';
import { LearningService } from '../core/services/learning.service';

/** Top-bar bell with the signed-in user's in-app notifications (Mission Phase 6). Polls every minute. */
@Component({
  selector: 'app-notification-bell',
  imports: [CommonModule],
  template: `
    <div class="bell-wrap">
      <button type="button" class="bell" (click)="toggle()" [attr.aria-label]="t('notifications.title')">
        <i class="pi pi-bell"></i>
        @if (unread() > 0) {
          <span class="badge">{{ unread() > 99 ? '99+' : unread() }}</span>
        }
      </button>
      @if (open()) {
        <div class="panel">
          <div class="panel-head">
            <strong>{{ t('notifications.title') }}</strong>
            @if (unread() > 0) {
              <button type="button" class="link" (click)="markAll()">{{ t('notifications.markAllRead') }}</button>
            }
          </div>
          @for (n of items(); track n.id) {
            <button type="button" class="item" [class.unread]="!n.isRead" (click)="openItem(n)">
              <span class="item-title">{{ n.title }}</span>
              @if (n.body) {
                <span class="item-body">{{ n.body }}</span>
              }
              <span class="item-time">{{ n.createdAt | date: 'short' }}</span>
            </button>
          } @empty {
            <div class="empty">{{ t('notifications.empty') }}</div>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .bell-wrap { position: relative; }
      .bell { position: relative; background: transparent; border: none; cursor: pointer; font-size: 1.15rem; color: #475569; padding: 0.4rem; }
      .badge { position: absolute; top: 0; inset-inline-end: 0; background: #dc2626; color: #fff; border-radius: 999px; font-size: 0.65rem; padding: 0 0.3rem; min-width: 1rem; }
      .panel { position: absolute; top: 2.4rem; inset-inline-end: 0; width: 320px; max-height: 420px; overflow: auto; background: #fff; border: 1px solid #e3e8f0; border-radius: 10px; box-shadow: 0 12px 32px rgba(16, 35, 63, 0.14); z-index: 1000; }
      .panel-head { display: flex; justify-content: space-between; align-items: center; padding: 0.6rem 0.8rem; border-bottom: 1px solid #f1f5f9; }
      .link { background: none; border: none; color: #2563eb; cursor: pointer; font-size: 0.8rem; }
      .item { display: flex; flex-direction: column; align-items: flex-start; gap: 0.15rem; width: 100%; text-align: start; background: #fff; border: none; border-bottom: 1px solid #f8fafc; padding: 0.6rem 0.8rem; cursor: pointer; }
      .item.unread { background: #eff6ff; }
      .item-title { font-weight: 600; font-size: 0.86rem; }
      .item-body { font-size: 0.8rem; color: #475569; }
      .item-time { font-size: 0.72rem; color: #94a3b8; }
      .empty { padding: 1rem; color: #94a3b8; font-size: 0.85rem; text-align: center; }
    `
  ]
})
export class NotificationBellComponent implements OnInit {
  private readonly service = inject(LearningService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly t = inject(TranslateService).t;
  protected readonly open = signal(false);
  protected readonly unread = signal(0);
  protected readonly items = signal<AppNotificationDto[]>([]);

  ngOnInit(): void {
    timer(0, 60_000)
      .pipe(
        switchMap(() => this.service.myNotifications(15).pipe(catchError(() => of<NotificationFeedDto | null>(null)))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((feed) => this.apply(feed));
  }

  protected toggle(): void {
    this.open.set(!this.open());

    if (this.open()) {
      this.refresh();
    }
  }

  protected markAll(): void {
    this.service.markAllRead().subscribe(() => this.refresh());
  }

  protected openItem(n: AppNotificationDto): void {
    const go = () => {
      this.open.set(false);

      if (n.link) {
        void this.router.navigateByUrl(n.link);
      }
    };

    if (n.isRead) {
      go();
      return;
    }

    this.service.markRead(n.id).subscribe(() => {
      this.refresh();
      go();
    });
  }

  private refresh(): void {
    this.service
      .myNotifications(15)
      .pipe(catchError(() => of<NotificationFeedDto | null>(null)))
      .subscribe((feed) => this.apply(feed));
  }

  private apply(feed: NotificationFeedDto | null): void {
    if (feed) {
      this.unread.set(feed.unreadCount);
      this.items.set(feed.items);
    }
  }
}

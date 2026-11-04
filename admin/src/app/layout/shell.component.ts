import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AvatarModule } from 'primeng/avatar';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { TagModule } from 'primeng/tag';
import { ToastModule } from 'primeng/toast';
import { SUPPORTED_LANGUAGES } from '../core/app-config';
import { TranslateService } from '../core/i18n/translate.service';
import { NAV_GROUPS, NavGroup } from '../core/nav';
import { AuthService } from '../core/services/auth.service';
import { NotificationService } from '../core/services/notification.service';
import { NotificationBellComponent } from './notification-bell.component';

@Component({
  selector: 'app-shell',
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    ButtonModule,
    AvatarModule,
    TagModule,
    ToastModule,
    ConfirmDialogModule,
    NotificationBellComponent
  ],
  template: `
    <p-toast position="top-right" />
    <p-confirmDialog />

    <div class="shell" [class.sidebar-collapsed]="collapsed()">
      <aside class="sidebar">
        <div class="brand">
          <i class="pi pi-graduation-cap"></i>
          @if (!collapsed()) {
            <span>EducationHub</span>
          }
        </div>

        <nav class="nav">
          @for (group of visibleGroups(); track group.label) {
            <div class="nav-group">
              @if (!collapsed()) {
                <div class="nav-group-label">{{ t(group.label) }}</div>
              }
              @for (item of group.items; track item.route) {
                <a
                  class="nav-item"
                  [routerLink]="item.route"
                  routerLinkActive="active"
                  [title]="t(item.label)"
                >
                  <i [class]="item.icon"></i>
                  @if (!collapsed()) {
                    <span>{{ t(item.label) }}</span>
                  }
                </a>
              }
            </div>
          }
        </nav>

        <button
          type="button"
          class="collapse-toggle"
          (click)="collapsed.set(!collapsed())"
          [attr.aria-label]="collapsed() ? t('shell.expandMenu') : t('shell.collapseMenu')"
        >
          <i class="pi" [class.pi-angle-right]="collapsed()" [class.pi-angle-left]="!collapsed()"></i>
        </button>
      </aside>

      <div class="main">
        <header class="topbar">
          <div class="topbar-left">
            <span class="app-title">{{ t('shell.adminConsole') }}</span>
            @if (user(); as current) {
              <p-tag [value]="roleLabel(current.roles)" severity="info" />
              <span class="tenant-hint">{{ tenantLabel(current) }}</span>
            }
          </div>

          <div class="topbar-right">
            @if (user()) {
              <app-notification-bell />
            }
            <div class="lang-switch">
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

            @if (user(); as current) {
              <div class="user-chip">
                <p-avatar [label]="initials(current.fullName)" shape="circle" />
                <div class="user-meta">
                  <span class="user-name">{{ current.fullName }}</span>
                  <span class="user-email">{{ current.email }}</span>
                </div>
              </div>
            }

            <p-button
              icon="pi pi-sign-out"
              [label]="t('common.signOut')"
              severity="secondary"
              [text]="true"
              (onClick)="logout()"
            />
          </div>
        </header>

        <main class="content">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
  styles: [
    `
      .shell {
        display: flex;
        min-height: 100vh;
        background: #f4f6fa;
      }
      .sidebar {
        position: relative;
        width: 250px;
        background: #10233f;
        color: #cdd8ea;
        display: flex;
        flex-direction: column;
        transition: width 0.18s ease;
        flex-shrink: 0;
      }
      .sidebar-collapsed .sidebar {
        width: 68px;
      }
      .brand {
        display: flex;
        align-items: center;
        gap: 0.6rem;
        padding: 1.1rem 1rem;
        font-weight: 700;
        font-size: 1.1rem;
        color: #fff;
        border-bottom: 1px solid rgba(255, 255, 255, 0.08);
      }
      .brand i {
        font-size: 1.4rem;
        color: #6ea8fe;
      }
      .nav {
        flex: 1;
        overflow-y: auto;
        padding: 0.75rem 0.5rem;
      }
      .nav-group + .nav-group {
        margin-top: 0.75rem;
      }
      .nav-group-label {
        font-size: 0.68rem;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: #7d8ba6;
        padding: 0.35rem 0.75rem;
      }
      .nav-item {
        display: flex;
        align-items: center;
        gap: 0.7rem;
        padding: 0.6rem 0.75rem;
        border-radius: 8px;
        color: #cdd8ea;
        text-decoration: none;
        font-size: 0.92rem;
        cursor: pointer;
      }
      .nav-item i {
        width: 1.15rem;
        text-align: center;
      }
      .nav-item:hover {
        background: rgba(255, 255, 255, 0.07);
        color: #fff;
      }
      .nav-item.active {
        background: #2563eb;
        color: #fff;
      }
      .collapse-toggle {
        background: transparent;
        border: none;
        color: #7d8ba6;
        padding: 0.75rem;
        cursor: pointer;
        border-top: 1px solid rgba(255, 255, 255, 0.08);
      }
      .main {
        flex: 1;
        display: flex;
        flex-direction: column;
        min-width: 0;
      }
      .topbar {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 1rem;
        padding: 0.75rem 1.25rem;
        background: #fff;
        border-bottom: 1px solid #e3e8f0;
        flex-wrap: wrap;
      }
      .topbar-left {
        display: flex;
        align-items: center;
        gap: 0.75rem;
      }
      .app-title {
        font-weight: 600;
        font-size: 1.05rem;
        color: #10233f;
      }
      .tenant-hint {
        color: #64748b;
        font-size: 0.85rem;
      }
      .topbar-right {
        display: flex;
        align-items: center;
        gap: 1rem;
      }
      .lang-switch {
        display: flex;
        border: 1px solid #d3dbe7;
        border-radius: 8px;
        overflow: hidden;
      }
      .lang-btn {
        background: #fff;
        border: none;
        padding: 0.3rem 0.7rem;
        cursor: pointer;
        font-size: 0.82rem;
        color: #475569;
      }
      .lang-btn.active {
        background: #2563eb;
        color: #fff;
      }
      .user-chip {
        display: flex;
        align-items: center;
        gap: 0.6rem;
      }
      .user-meta {
        display: flex;
        flex-direction: column;
        line-height: 1.1;
      }
      .user-name {
        font-weight: 600;
        font-size: 0.9rem;
        color: #10233f;
      }
      .user-email {
        font-size: 0.78rem;
        color: #64748b;
      }
      .content {
        padding: 1.5rem;
        flex: 1;
        overflow: auto;
      }
    `
  ]
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  protected readonly t = inject(TranslateService).t;
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  protected readonly collapsed = signal(false);
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly user = this.auth.user;

  protected readonly visibleGroups = computed<NavGroup[]>(() => {
    const permissions = this.auth.permissions();
    const roles = this.auth.roles();

    return NAV_GROUPS.map((group) => ({
      label: group.label,
      items: group.items.filter(
        (item) =>
          (!item.permission || permissions.has(item.permission)) &&
          (!item.role || roles.has(item.role))
      )
    })).filter((group) => group.items.length > 0);
  });

  constructor() {
    this.auth.applyStoredLanguage();
  }

  protected setLanguage(code: string): void {
    this.auth.setLanguage(code);
  }

  protected logout(): void {
    this.notifications
      .confirm({
        message: this.t('shell.logoutConfirmMessage'),
        header: this.t('common.signOut'),
        acceptLabel: this.t('common.signOut'),
        icon: 'pi pi-sign-out'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.auth.logout();
        void this.router.navigate(['/login']);
      });
  }

  protected initials(name: string): string {
    return name
      .split(' ')
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]?.toUpperCase() ?? '')
      .join('');
  }

  protected roleLabel(roles: string[]): string {
    return roles[0] ?? this.t('shell.noRole');
  }

  protected tenantLabel(user: { tenantName: string | null; tenantId: string }): string {
    if (user.tenantName) {
      return user.tenantName;
    }

    return user.tenantId === '00000000-0000-0000-0000-000000000000'
      ? this.t('shell.platformScope')
      : this.t('shell.tenantScope');
  }
}

import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { catchError, of } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { PermissionAreaDto } from '../../core/models';
import { RolesService } from '../../core/services/roles.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-profile',
  imports: [CommonModule, RouterLink, ButtonModule, TagModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('profile.title') }}</h1>
        <div class="subtitle">{{ t('profile.subtitle') }}</div>
      </div>
      <p-button
        [label]="t('profile.changePassword')"
        icon="pi pi-key"
        routerLink="/change-password"
      />
    </div>

    @if (user(); as current) {
      <div class="card">
        <div class="profile-grid">
          <div class="field">
            <label>{{ t('profile.fullName') }}</label>
            <span>{{ current.fullName }}</span>
          </div>
          <div class="field">
            <label>{{ t('common.email') }}</label>
            <span>{{ current.email }}</span>
          </div>
          <div class="field">
            <label>{{ t('profile.emailConfirmed') }}</label>
            <p-tag
              [value]="current.emailConfirmed ? t('common.yes') : t('common.no')"
              [severity]="current.emailConfirmed ? 'success' : 'warn'"
            />
          </div>
          <div class="field">
            <label>{{ t('profile.phone') }}</label>
            <span>{{ current.phoneNumber ?? '—' }}</span>
          </div>
          <div class="field">
            <label>{{ t('profile.tenant') }}</label>
            <span>{{ current.tenantName ?? current.tenantId }}</span>
          </div>
          <div class="field">
            <label>{{ t('profile.preferredLanguage') }}</label>
            <span>{{ current.preferredLanguage }}</span>
          </div>
          <div class="field">
            <label>{{ t('profile.roles') }}</label>
            <span class="tag-row">
              @for (role of current.roles; track role) {
                <p-tag [value]="role" severity="info" />
              }
            </span>
          </div>
        </div>
      </div>

      <div class="card" style="margin-top:1.25rem">
        <h3 class="card-title">
          {{ t('profile.effectivePermissions', { count: current.permissions.length }) }}
        </h3>
        @if (areas().length === 0) {
          <div class="state-panel"><i class="pi pi-lock"></i>{{ t('profile.noPermissions') }}</div>
        } @else {
          <div class="perm-areas">
            @for (area of areas(); track area.area) {
              <div class="perm-area">
                <h4>{{ area.area }}</h4>
                <div class="perm-list">
                  @for (permission of area.permissions; track permission) {
                    <span class="perm-chip">{{ permission }}</span>
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>
    }
  `,
  styles: [
    `
      .profile-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
        gap: 1rem;
      }
      .field label {
        display: block;
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: #94a3b8;
        margin-bottom: 0.2rem;
      }
      .card-title {
        margin: 0 0 1rem;
        font-size: 1rem;
      }
      .perm-areas {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
        gap: 0.75rem;
      }
      .perm-chip {
        display: inline-block;
        background: #eef4ff;
        color: #1d4ed8;
        border-radius: 6px;
        padding: 0.15rem 0.5rem;
        font-size: 0.78rem;
        margin: 0.15rem 0;
      }
    `
  ]
})
export class ProfileComponent {
  protected readonly t = inject(TranslateService).t;
  private readonly auth = inject(AuthService);
  private readonly roles = inject(RolesService);

  protected readonly user = this.auth.user;
  protected readonly areas = signal<PermissionAreaDto[]>([]);

  constructor() {
    this.roles
      .catalog()
      .pipe(catchError(() => of<PermissionAreaDto[]>([])))
      .subscribe((areas) => {
        const owned = this.auth.permissions();
        this.areas.set(
          areas
            .map((area) => ({
              area: area.area,
              permissions: area.permissions.filter((permission) => owned.has(permission))
            }))
            .filter((area) => area.permissions.length > 0)
        );
      });
  }
}

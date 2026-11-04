import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MessageModule } from 'primeng/message';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { catchError, of } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { PermissionAreaDto } from '../../core/models';
import { RolesService } from '../../core/services/roles.service';

@Component({
  selector: 'app-permissions',
  imports: [CommonModule, MessageModule, ProgressSpinnerModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('nav.permissionsCatalog') }}</h1>
        <div class="subtitle">
          {{ t('permissions.subtitle') }}
        </div>
      </div>
    </div>

    @if (loading()) {
      <div class="state-panel"><p-progressSpinner strokeWidth="4" /></div>
    } @else if (error()) {
      <p-message severity="error" [text]="error()!" />
    } @else {
      <div class="areas">
        @for (area of areas(); track area.area) {
          <div class="card perm-area">
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
  `,
  styles: [
    `
      .areas {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
        gap: 1rem;
      }
      .perm-area h4 {
        margin: 0 0 0.6rem;
        font-size: 0.95rem;
      }
      .perm-chip {
        display: inline-block;
        background: #eef4ff;
        color: #1d4ed8;
        border-radius: 6px;
        padding: 0.15rem 0.5rem;
        font-size: 0.78rem;
        margin: 0.15rem 0.15rem;
      }
    `
  ]
})
export class PermissionsComponent implements OnInit {
  protected readonly t = inject(TranslateService).t;
  private readonly roles = inject(RolesService);

  protected readonly areas = signal<PermissionAreaDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.roles
      .catalog()
      .pipe(catchError(() => of<PermissionAreaDto[]>([])))
      .subscribe({
        next: (areas) => {
          this.areas.set(areas);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(this.t('permissions.loadFailed'));
          this.loading.set(false);
        }
      });
  }
}

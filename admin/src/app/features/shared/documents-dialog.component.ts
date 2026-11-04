import { CommonModule } from '@angular/common';
import { Component, effect, inject, input, model, signal } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TableModule } from 'primeng/table';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { StoredFileDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FileOwner, FilesService } from '../../core/services/files.service';
import { NotificationService } from '../../core/services/notification.service';

/** Upload / download / delete the documents attached to a student, parent or teacher (Phase 2.5b). */
@Component({
  selector: 'app-documents-dialog',
  imports: [CommonModule, ButtonModule, DialogModule, TableModule],
  template: `
    <p-dialog [visible]="visible()" (visibleChange)="visible.set($event)" [header]="t('documents.title') + ' · ' + title()" [modal]="true" [style]="{ width: '680px' }">
      @if (canUpload()) {
        <div class="upload">
          <input #picker type="file" (change)="upload(picker)" [disabled]="uploading()" accept=".pdf,.png,.jpg,.jpeg,.webp,.doc,.docx,.xls,.xlsx,.txt" />
          @if (uploading()) {
            <i class="pi pi-spin pi-spinner"></i>
          }
          <span class="muted">{{ t('documents.hint') }}</span>
        </div>
      }
      <p-table [value]="files()" [loading]="loading()" styleClass="p-datatable-sm">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('documents.file') }}</th>
            <th>{{ t('documents.size') }}</th>
            <th>{{ t('common.createdAt') }}</th>
            <th style="width:7rem"></th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.originalFileName }}</td>
            <td>{{ size(row.sizeInBytes) }}</td>
            <td>{{ row.createdAt | date: 'short' }}</td>
            <td>
              <p-button icon="pi pi-download" [text]="true" (onClick)="download(row)" />
              @if (canDelete()) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="4"><div class="state-panel"><i class="pi pi-folder-open"></i>{{ t('documents.empty') }}</div></td></tr>
        </ng-template>
      </p-table>
    </p-dialog>
  `,
  styles: [
    `
      .upload { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.75rem; flex-wrap: wrap; }
      .muted { color: #94a3b8; font-size: 0.8rem; }
    `
  ]
})
export class DocumentsDialogComponent {
  private readonly service = inject(FilesService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  readonly visible = model(false);
  readonly owner = input.required<FileOwner>();
  readonly ownerId = input<string | null>(null);
  readonly title = input('');
  readonly category = input('StudentDocument');

  protected readonly t = inject(TranslateService).t;
  protected readonly files = signal<StoredFileDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly uploading = signal(false);
  protected readonly canUpload = signal(this.auth.hasPermission('Files.Upload'));
  protected readonly canDelete = signal(this.auth.hasPermission('Files.Delete'));

  constructor() {
    effect(() => {
      if (this.visible() && this.ownerId()) {
        this.load();
      }
    });
  }

  protected size(bytes: number): string {
    return bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  protected upload(picker: HTMLInputElement): void {
    const file = picker.files?.[0];
    const id = this.ownerId();

    if (!file || !id) {
      return;
    }

    this.uploading.set(true);
    this.service
      .upload(file, this.owner(), id, this.category())
      .pipe(
        finalize(() => {
          this.uploading.set(false);
          picker.value = '';
        })
      )
      .subscribe(() => {
        this.notifications.success(this.t('documents.uploaded'));
        this.load();
      });
  }

  protected download(row: StoredFileDto): void {
    this.service.download(row.id).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = row.originalFileName;
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
  }

  protected remove(row: StoredFileDto): void {
    this.notifications
      .confirm({ message: this.t('documents.deleteMessage', { name: row.originalFileName }), header: this.t('common.delete'), acceptButtonStyleClass: 'p-button-danger' })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.remove(row.id).subscribe(() => this.load());
        }
      });
  }

  private load(): void {
    const id = this.ownerId();

    if (!id) {
      return;
    }

    this.loading.set(true);
    this.service
      .list(this.owner(), id)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((rows) => this.files.set(rows));
  }
}

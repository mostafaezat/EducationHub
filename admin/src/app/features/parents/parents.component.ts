import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ParentDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { StudentsService } from '../../core/services/students.service';

@Component({
  selector: 'app-parents',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    PasswordModule,
    TableModule,
    TagModule,
    TextareaModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('parents.title') }}</h1>
        <div class="subtitle">{{ t('parents.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('parents.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input
        pInputText
        type="text"
        [placeholder]="t('parents.searchPlaceholder')"
        (input)="onSearch($event)"
      />
      <div class="spacer"></div>
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="load()" />
    </div>

    <div class="card">
      <p-table
        [value]="rows()"
        [lazy]="true"
        [paginator]="true"
        [rows]="pageSize()"
        [first]="first()"
        [totalRecords]="total()"
        [loading]="loading()"
        (onLazyLoad)="onLazyLoad($event)"
        styleClass="p-datatable-sm"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('parents.fullName') }}</th>
            <th style="width:10rem">{{ t('parents.phoneNumber') }}</th>
            <th>{{ t('parents.children') }}</th>
            <th style="width:8rem">{{ t('parents.account') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>
              {{ row.fullName }}
              @if (row.email) {
                <div class="muted">{{ row.email }}</div>
              }
            </td>
            <td>{{ row.phoneNumber || '—' }}</td>
            <td>
              <div class="tag-row">
                @for (child of row.children; track child.studentProfileId) {
                  <p-tag [value]="child.studentFullName" [severity]="child.isPrimary ? 'info' : 'secondary'" />
                } @empty {
                  <span class="muted">—</span>
                }
              </div>
            </td>
            <td>
              <p-tag
                [value]="row.userId ? t('parents.hasAccount') : t('parents.noAccount')"
                [severity]="row.userId ? 'success' : 'secondary'"
              />
            </td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
              }
              @if (canDelete()) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5">
              <div class="state-panel"><i class="pi pi-users"></i>{{ t('parents.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('parents.editDialog') : t('parents.newDialog')"
      [modal]="true"
      [style]="{ width: '600px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('parents.fullName') }} *</label>
          <input pInputText formControlName="fullName" />
          @if (form.controls.fullName.touched && form.controls.fullName.invalid) {
            <small class="field-error">{{ t('parents.formIncomplete') }}</small>
          }
        </div>
        <div class="field">
          <label>{{ t('parents.phoneNumber') }}</label>
          <input pInputText formControlName="phoneNumber" />
        </div>
        <div class="field">
          <label>{{ t('parents.email') }}{{ createAccount() ? ' *' : '' }}</label>
          <input pInputText type="email" formControlName="email" />
          @if (form.controls.email.touched && form.controls.email.invalid) {
            <small class="field-error">{{ t('parents.formIncomplete') }}</small>
          }
        </div>
        <div class="field">
          <label>{{ t('parents.notes') }}</label>
          <textarea pTextarea formControlName="notes" rows="2"></textarea>
        </div>
        @if (!editing()) {
          <div class="field">
            <label class="inline">
              <input type="checkbox" [checked]="createAccount()" (change)="toggleAccount($event)" />
              {{ t('parents.createAccount') }}
            </label>
          </div>
          @if (createAccount()) {
            <div class="field">
              <label>{{ t('parents.password') }} *</label>
              <p-password formControlName="password" [toggleMask]="true" [feedback]="false" styleClass="w-full" />
              <small class="muted">{{ t('parents.accountHint') }}</small>
            </div>
          }
        }
      </form>

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted {
        color: #94a3b8;
        font-size: 0.78rem;
      }
      .field-error {
        color: #dc2626;
        font-size: 0.78rem;
      }
      .subtitle {
        color: #64748b;
        font-size: 0.85rem;
      }
      .inline {
        display: flex;
        gap: 0.4rem;
        align-items: center;
      }
    `
  ]
})
export class ParentsComponent implements OnInit {
  private readonly service = inject(StudentsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly rows = signal<ParentDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');

  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<ParentDto | null>(null);
  protected readonly createAccount = signal(false);

  protected readonly canCreate = signal(this.auth.hasPermission('Parents.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Parents.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Parents.Delete'));

  protected readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(3)]],
    phoneNumber: [''],
    email: ['', [Validators.email]],
    notes: [''],
    password: ['']
  });

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.pageNumber.set(1);
      this.first.set(0);
      this.load();
    });
    this.load();
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.pageSize();
    this.first.set(first);
    this.pageSize.set(rows);
    this.pageNumber.set(Math.floor(first / rows) + 1);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .listParents({
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize(),
        search: this.search() || undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected toggleAccount(event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    this.createAccount.set(enabled);

    const email = this.form.controls.email;
    const password = this.form.controls.password;

    email.setValidators(enabled ? [Validators.required, Validators.email] : [Validators.email]);
    password.setValidators(enabled ? [Validators.required, Validators.minLength(8)] : []);
    email.updateValueAndValidity();
    password.updateValueAndValidity();
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.createAccount.set(false);
    this.form.controls.email.setValidators([Validators.email]);
    this.form.controls.password.setValidators([]);
    this.form.reset({ fullName: '', phoneNumber: '', email: '', notes: '', password: '' });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: ParentDto): void {
    this.editing.set(row);
    this.createAccount.set(false);
    this.form.controls.email.setValidators([Validators.email]);
    this.form.controls.password.setValidators([]);
    this.form.reset({
      fullName: row.fullName,
      phoneNumber: row.phoneNumber ?? '',
      email: row.email ?? '',
      notes: row.notes ?? '',
      password: ''
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.notifications.error(this.t('parents.formIncomplete'));
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();

    const request$ = editing
      ? this.service.updateParent(editing.id, {
          fullName: value.fullName,
          phoneNumber: value.phoneNumber,
          email: value.email,
          notes: value.notes
        })
      : this.service.createParent({
          fullName: value.fullName,
          phoneNumber: value.phoneNumber || null,
          email: value.email || null,
          notes: value.notes || null,
          userId: null,
          accountPassword: this.createAccount() ? value.password : null
        });

    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(this.t(editing ? 'parents.updated' : 'parents.created'));
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected remove(row: ParentDto): void {
    this.notifications
      .confirm({
        message: this.t('parents.deleteMessage', { name: row.fullName }),
        header: this.t('parents.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.service.removeParent(row.id).subscribe(() => {
          this.notifications.success(this.t('parents.deleted'));
          this.load();
        });
      });
  }
}

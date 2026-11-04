import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import {
  CreateTenantRequest,
  TENANT_STATUSES,
  TENANT_TYPES,
  TenantDto,
  TenantStatus,
  TenantType,
  UpdateTenantRequest
} from '../../core/models';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { TenantsService } from '../../core/services/tenants.service';

function toDateOnly(value: Date | null | undefined): string | null {
  if (!value) {
    return null;
  }

  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

@Component({
  selector: 'app-tenants',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputTextModule,
    PasswordModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('tenants.title') }}</h1>
        <div class="subtitle">{{ t('tenants.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('tenants.newTenant')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('tenants.searchPlaceholder')" (input)="onSearch($event)" />
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
            <th>{{ t('common.name') }}</th>
            <th>{{ t('tenants.column.slug') }}</th>
            <th>{{ t('tenants.column.type') }}</th>
            <th>{{ t('common.status') }}</th>
            <th>{{ t('tenants.column.subscription') }}</th>
            <th>{{ t('tenants.column.admin') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>
              {{ row.name }}
              <div class="muted">{{ row.nameAr }}</div>
            </td>
            <td class="mono">{{ row.slug }}</td>
            <td>{{ t('tenants.type.' + row.type) }}</td>
            <td>
              <p-tag [value]="t('tenants.status.' + row.status)" [severity]="statusSeverity(row)" />
              @if (!row.isActive) {
                <p-tag [value]="t('common.inactive')" severity="secondary" styleClass="ml" />
              }
            </td>
            <td>
              @if (row.subscriptionExpiryDate) {
                <span>{{ row.subscriptionExpiryDate | date: 'yyyy-MM-dd' }}</span>
                @if (row.isSubscriptionExpired) {
                  <p-tag [value]="t('tenants.expired')" severity="danger" styleClass="ml" />
                }
              } @else {
                <span class="muted">—</span>
              }
            </td>
            <td>{{ row.adminEmail || '—' }}</td>
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
            <td colspan="7">
              <div class="state-panel"><i class="pi pi-building"></i>{{ t('tenants.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('tenants.dialog.editTitle') : t('tenants.dialog.createTitle')"
      [modal]="true"
      [style]="{ width: '720px' }"
    >
      @if (editing()) {
        <form [formGroup]="editForm" class="form-grid">
          <div class="field">
            <label>{{ t('common.name') }} *</label>
            <input pInputText formControlName="name" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.nameAr') }}</label>
            <input pInputText formControlName="nameAr" />
          </div>
          <div class="field">
            <label>{{ t('common.email') }}</label>
            <input pInputText formControlName="email" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.phone') }}</label>
            <input pInputText formControlName="phone" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.city') }}</label>
            <input pInputText formControlName="city" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.country') }}</label>
            <input pInputText formControlName="country" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.currency') }}</label>
            <input pInputText formControlName="currency" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.timeZone') }}</label>
            <input pInputText formControlName="timeZone" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.defaultLanguage') }}</label>
            <input pInputText formControlName="defaultLanguage" />
          </div>
          <div class="field">
            <label>{{ t('common.status') }}</label>
            <p-select [options]="statusOptions()" formControlName="status" optionLabel="label" optionValue="value" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.subscriptionStart') }}</label>
            <p-datepicker formControlName="subscriptionStartDate" dateFormat="yy-mm-dd" [showIcon]="true" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.subscriptionExpiry') }}</label>
            <p-datepicker formControlName="subscriptionExpiryDate" dateFormat="yy-mm-dd" [showIcon]="true" />
          </div>
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
          <div class="field full">
            <label>{{ t('common.description') }}</label>
            <textarea pTextarea formControlName="description" rows="2"></textarea>
          </div>
        </form>
      } @else {
        <form [formGroup]="createForm" class="form-grid">
          <div class="field">
            <label>{{ t('tenants.label.slug') }} *</label>
            <input pInputText formControlName="slug" placeholder="my-center" />
          </div>
          <div class="field">
            <label>{{ t('common.name') }} *</label>
            <input pInputText formControlName="name" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.nameAr') }}</label>
            <input pInputText formControlName="nameAr" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.type') }} *</label>
            <p-select [options]="typeOptions()" formControlName="type" optionLabel="label" optionValue="value" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.currency') }} *</label>
            <input pInputText formControlName="currency" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.timeZone') }} *</label>
            <input pInputText formControlName="timeZone" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.defaultLanguage') }} *</label>
            <input pInputText formControlName="defaultLanguage" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.contactEmail') }}</label>
            <input pInputText formControlName="email" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.contactPhone') }}</label>
            <input pInputText formControlName="phone" />
          </div>
          <div class="field divider full"><span>{{ t('tenants.label.firstAdministrator') }}</span></div>
          <div class="field">
            <label>{{ t('tenants.label.adminFullName') }} *</label>
            <input pInputText formControlName="adminFullName" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.adminEmail') }} *</label>
            <input pInputText formControlName="adminEmail" />
          </div>
          <div class="field">
            <label>{{ t('tenants.label.adminPassword') }} *</label>
            <p-password formControlName="adminPassword" [feedback]="false" [toggleMask]="true" />
          </div>
        </form>
      }

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
      .mono {
        font-family: monospace;
      }
      .ml {
        margin-left: 0.35rem;
      }
      .divider {
        border-top: 1px solid #e2e8f0;
        padding-top: 0.5rem;
        color: #64748b;
        font-size: 0.8rem;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
    `
  ]
})
export class TenantsComponent implements OnInit {
  private readonly service = inject(TenantsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly typeOptions = computed(() =>
    TENANT_TYPES.map((value) => ({ label: this.t('tenants.type.' + value), value }))
  );
  protected readonly statusOptions = computed(() =>
    TENANT_STATUSES.map((value) => ({ label: this.t('tenants.status.' + value), value }))
  );

  protected readonly rows = signal<TenantDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<TenantDto | null>(null);

  protected readonly canCreate = signal(this.auth.hasRole('PlatformAdmin'));
  protected readonly canUpdate = signal(this.auth.hasRole('PlatformAdmin'));
  protected readonly canDelete = signal(this.auth.hasRole('PlatformAdmin'));

  protected readonly createForm = this.fb.nonNullable.group({
    slug: ['', [Validators.required, Validators.pattern(/^[a-z0-9-]+$/)]],
    name: ['', [Validators.required]],
    nameAr: [''],
    type: ['EducationCenter' as TenantType, [Validators.required]],
    email: [''],
    phone: [''],
    currency: ['EGP', [Validators.required]],
    timeZone: ['Africa/Cairo', [Validators.required]],
    defaultLanguage: ['ar', [Validators.required]],
    adminFullName: ['', [Validators.required]],
    adminEmail: ['', [Validators.required, Validators.email]],
    adminPassword: ['', [Validators.required, Validators.minLength(8)]]
  });

  protected readonly editForm = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    nameAr: [''],
    email: [''],
    phone: [''],
    city: [''],
    country: [''],
    currency: [''],
    timeZone: [''],
    defaultLanguage: [''],
    status: ['Active' as TenantStatus],
    subscriptionStartDate: [null as Date | null],
    subscriptionExpiryDate: [null as Date | null],
    isActive: [true],
    description: ['']
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

  protected statusSeverity(row: TenantDto): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (row.status) {
      case 'Active':
        return 'success';
      case 'Trial':
        return 'info';
      case 'Suspended':
        return 'warn';
      case 'Expired':
      case 'Cancelled':
        return 'danger';
      default:
        return 'secondary';
    }
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
      .list({
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize(),
        searchTerm: this.search() || undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.createForm.reset({
      slug: '',
      name: '',
      nameAr: '',
      type: 'EducationCenter',
      email: '',
      phone: '',
      currency: 'EGP',
      timeZone: 'Africa/Cairo',
      defaultLanguage: 'ar',
      adminFullName: '',
      adminEmail: '',
      adminPassword: ''
    });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: TenantDto): void {
    this.editing.set(row);
    this.editForm.reset({
      name: row.name,
      nameAr: row.nameAr ?? '',
      email: row.email ?? '',
      phone: row.phone ?? '',
      city: row.city ?? '',
      country: row.country ?? '',
      currency: row.currency,
      timeZone: row.timeZone,
      defaultLanguage: row.defaultLanguage,
      status: row.status,
      subscriptionStartDate: row.subscriptionStartDate ? new Date(row.subscriptionStartDate) : null,
      subscriptionExpiryDate: row.subscriptionExpiryDate ? new Date(row.subscriptionExpiryDate) : null,
      isActive: row.isActive,
      description: row.description ?? ''
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    const editing = this.editing();
    this.saving.set(true);

    if (editing) {
      if (this.editForm.invalid) {
        this.editForm.markAllAsTouched();
        this.saving.set(false);
        return;
      }

      const value = this.editForm.getRawValue();
      const request: UpdateTenantRequest = {
        name: value.name,
        nameAr: value.nameAr || null,
        description: value.description || null,
        descriptionAr: null,
        email: value.email || null,
        phone: value.phone || null,
        address: null,
        addressAr: null,
        city: value.city || null,
        country: value.country || null,
        currency: value.currency || null,
        timeZone: value.timeZone || null,
        defaultLanguage: value.defaultLanguage || null,
        status: value.status,
        isActive: value.isActive,
        subscriptionStartDate: toDateOnly(value.subscriptionStartDate),
        subscriptionExpiryDate: toDateOnly(value.subscriptionExpiryDate)
      };

      this.service
        .update(editing.id, request)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe(() => {
          this.notifications.success(this.t('tenants.toast.updated'));
          this.dialogVisible.set(false);
          this.load();
        });
      return;
    }

    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      this.saving.set(false);
      return;
    }

    const value = this.createForm.getRawValue();
    const request: CreateTenantRequest = {
      slug: value.slug,
      name: value.name,
      nameAr: value.nameAr || null,
      type: value.type,
      email: value.email || null,
      phone: value.phone || null,
      address: null,
      addressAr: null,
      city: null,
      country: null,
      currency: value.currency,
      timeZone: value.timeZone,
      defaultLanguage: value.defaultLanguage,
      description: null,
      descriptionAr: null,
      adminFullName: value.adminFullName,
      adminEmail: value.adminEmail,
      adminPassword: value.adminPassword
    };

    this.service
      .create(request)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('tenants.toast.created'));
        this.dialogVisible.set(false);
        this.load();
      });
  }

  protected remove(row: TenantDto): void {
    this.notifications
      .confirm({
        message: this.t('tenants.confirm.deleteMessage', { name: row.name }),
        header: this.t('tenants.confirm.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('tenants.toast.deleted'));
          this.load();
        });
      });
  }
}

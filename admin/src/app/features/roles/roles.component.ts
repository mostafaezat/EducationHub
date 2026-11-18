import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { CreateRoleRequest, RoleDto, UpdateRoleRequest } from '../../core/models';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { RolesService } from '../../core/services/roles.service';

interface PermissionOption {
  label: string;
  value: string;
}

interface PermissionGroup {
  label: string;
  items: PermissionOption[];
}

@Component({
  selector: 'app-roles',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MultiSelectModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('roles.title') }}</h1>
        <div class="subtitle">{{ t('roles.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('roles.newRole')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('roles.searchPlaceholder')" (input)="onSearch($event)" />
      <div class="flag">
        <p-toggleswitch [ngModel]="activeOnly()" (ngModelChange)="setActiveOnly($event)" />
        <span>{{ t('roles.activeOnly') }}</span>
      </div>
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
            <th>{{ t('common.description') }}</th>
            <th>{{ t('roles.column.permissions') }}</th>
            <th>{{ t('roles.column.users') }}</th>
            <th>{{ t('roles.column.type') }}</th>
            <th>{{ t('common.status') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.name }}</td>
            <td>{{ row.description || '—' }}</td>
            <td>{{ row.permissions.length }}</td>
            <td>{{ row.userCount }}</td>
            <td>
              @if (row.isSystemRole) {
                <p-tag [value]="t('roles.type.system')" severity="info" />
              } @else {
                <p-tag [value]="t('roles.type.custom')" severity="secondary" />
              }
            </td>
            <td>
              <p-tag
                [value]="row.isActive ? t('common.active') : t('common.inactive')"
                [severity]="row.isActive ? 'success' : 'secondary'"
              />
            </td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
              }
              @if (canAssignPermissions()) {
                <p-button icon="pi pi-shield" [text]="true" (onClick)="openPermissions(row)" />
              }
              @if (canDelete() && !row.isSystemRole) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="7">
              <div class="state-panel"><i class="pi pi-shield"></i>{{ t('roles.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [(visible)]="roleDialog"
      [header]="editing() ? t('roles.dialog.editTitle') : t('roles.dialog.createTitle')"
      [modal]="true"
      [style]="{ width: '640px' }"
    >
      @if (editing()) {
        <form [formGroup]="editForm" class="form-grid">
          <div class="field">
            <label>{{ t('common.name') }}</label>
            <input pInputText [value]="editing()!.name" [disabled]="true" />
          </div>
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
          <div class="field full">
            <label>{{ t('common.description') }}</label>
            <textarea pTextarea formControlName="description" rows="2"></textarea>
          </div>
          <div class="field full">
            <label>{{ t('roles.label.descriptionAr') }}</label>
            <textarea pTextarea formControlName="descriptionAr" rows="2"></textarea>
          </div>
        </form>
      } @else {
        <form [formGroup]="createForm" class="form-grid">
          <div class="field full">
            <label>{{ t('common.name') }} *</label>
            <input pInputText formControlName="name" placeholder="BranchManager" />
          </div>
          <div class="field full">
            <label>{{ t('common.description') }}</label>
            <textarea pTextarea formControlName="description" rows="2"></textarea>
          </div>
          <div class="field full">
            <label>{{ t('roles.label.descriptionAr') }}</label>
            <textarea pTextarea formControlName="descriptionAr" rows="2"></textarea>
          </div>
          <div class="field full">
            <label>{{ t('roles.label.permissions') }}</label>
            <p-multiselect
              [options]="permissionGroups()"
              formControlName="permissions"
              [group]="true"
              [filter]="true"
              optionGroupLabel="label"
              optionGroupChildren="items"
              optionLabel="label"
              optionValue="value"
              [placeholder]="t('roles.selectPermissions')"
              scrollHeight="260px"
            />
          </div>
        </form>
      }

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="roleDialog = false" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
      </ng-template>
    </p-dialog>

    <p-dialog [(visible)]="permissionsDialog" [header]="t('roles.dialog.assignPermissions')" [modal]="true" [style]="{ width: '640px' }">
      <div class="field full">
        <label>{{ t('roles.label.permissionsFor', { name: target()?.name ?? '' }) }}</label>
        <p-multiselect
          [options]="permissionGroups()"
          [ngModel]="selectedPermissions()"
          (ngModelChange)="selectedPermissions.set($event)"
          [group]="true"
          [filter]="true"
          optionGroupLabel="label"
          optionGroupChildren="items"
          optionLabel="label"
          optionValue="value"
          [placeholder]="t('roles.selectPermissions')"
          scrollHeight="300px"
          styleClass="w-full"
        />
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="permissionsDialog = false" />
        <p-button [label]="t('roles.savePermissions')" icon="pi pi-check" [loading]="saving()" (onClick)="savePermissions()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .flag {
        display: flex;
        align-items: center;
        gap: 0.4rem;
        font-size: 0.85rem;
        color: #475569;
      }
    `
  ]
})
export class RolesComponent implements OnInit {
  private readonly service = inject(RolesService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly rows = signal<RoleDto[]>([]);
  protected readonly permissionGroups = signal<PermissionGroup[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly activeOnly = signal(false);

  protected roleDialog = false;
  protected permissionsDialog = false;

  protected readonly editing = signal<RoleDto | null>(null);
  protected readonly target = signal<RoleDto | null>(null);
  protected readonly selectedPermissions = signal<string[]>([]);

  /**
   * Roles belong to a tenant. The platform administrator (no tenant) sees the platform roles
   * read-only: they are defined in code and reconciled at startup, not edited here.
   */
  private readonly isPlatform = (() => {
    const tenantId = this.auth.user()?.tenantId;
    return !tenantId || tenantId === '00000000-0000-0000-0000-000000000000';
  })();

  protected readonly canCreate = signal(!this.isPlatform && this.auth.hasPermission('Roles.Create'));
  protected readonly canUpdate = signal(!this.isPlatform && this.auth.hasPermission('Roles.Update'));
  protected readonly canDelete = signal(!this.isPlatform && this.auth.hasPermission('Roles.Delete'));
  protected readonly canAssignPermissions = signal(!this.isPlatform && this.auth.hasPermission('Roles.AssignPermissions'));

  protected readonly createForm = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    description: [''],
    descriptionAr: [''],
    permissions: [[] as string[]]
  });

  protected readonly editForm = this.fb.nonNullable.group({
    description: [''],
    descriptionAr: [''],
    isActive: [true]
  });

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.pageNumber.set(1);
      this.first.set(0);
      this.load();
    });

    this.loadCatalog();
    this.load();
  }

  private loadCatalog(): void {
    if (!this.canCreate() && !this.canUpdate()) {
      return;
    }

    this.service.catalog().subscribe((areas) => {
      this.permissionGroups.set(
        areas.map((area) => ({
          label: area.area,
          items: area.permissions.map((permission) => ({ label: permission, value: permission }))
        }))
      );
    });
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected setActiveOnly(value: boolean): void {
    this.activeOnly.set(value);
    this.pageNumber.set(1);
    this.first.set(0);
    this.load();
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
        searchTerm: this.search() || undefined,
        activeOnly: this.activeOnly() || undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.createForm.reset({ name: '', description: '', descriptionAr: '', permissions: [] });
    this.roleDialog = true;
  }

  protected openEdit(row: RoleDto): void {
    this.editing.set(row);
    this.editForm.reset({
      description: row.description ?? '',
      descriptionAr: row.descriptionAr ?? '',
      isActive: row.isActive
    });
    this.roleDialog = true;
  }

  protected openPermissions(row: RoleDto): void {
    this.target.set(row);
    this.selectedPermissions.set([...row.permissions]);
    this.permissionsDialog = true;
  }

  protected save(): void {
    const editing = this.editing();
    this.saving.set(true);

    if (editing) {
      const value = this.editForm.getRawValue();
      const request: UpdateRoleRequest = {
        description: value.description || null,
        descriptionAr: value.descriptionAr || null,
        isActive: value.isActive
      };

      this.service
        .update(editing.id, request)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe(() => {
          this.notifications.success(this.t('roles.toast.updated'));
          this.roleDialog = false;
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
    const request: CreateRoleRequest = {
      name: value.name,
      description: value.description || null,
      descriptionAr: value.descriptionAr || null,
      permissions: value.permissions.length ? value.permissions : null
    };

    this.service
      .create(request)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('roles.toast.created'));
        this.roleDialog = false;
        this.load();
      });
  }

  protected savePermissions(): void {
    const target = this.target();

    if (!target) {
      return;
    }

    this.saving.set(true);
    this.service
      .assignPermissions(target.id, { permissions: this.selectedPermissions() })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('roles.toast.permissionsUpdated'));
        this.permissionsDialog = false;
        this.load();
      });
  }

  protected remove(row: RoleDto): void {
    this.notifications
      .confirm({
        message: this.t('roles.confirm.deleteMessage', { name: row.name }),
        header: this.t('roles.confirm.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('roles.toast.deleted'));
          this.load();
        });
      });
  }
}

import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { AbstractControl, FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize, forkJoin, of } from 'rxjs';
import {
  CreateUserRequest,
  UpdateUserRequest,
  UserDto
} from '../../core/models';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { BranchesService } from '../../core/services/branches.service';
import { NotificationService } from '../../core/services/notification.service';
import { RolesService } from '../../core/services/roles.service';
import { UsersService } from '../../core/services/users.service';

interface Option {
  label: string;
  value: string;
}

@Component({
  selector: 'app-users',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    MultiSelectModule,
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
        <h1>{{ t('users.title') }}</h1>
        <div class="subtitle">{{ t('users.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('users.newUser')" icon="pi pi-user-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('users.searchPlaceholder')" (input)="onSearch($event)" />
      <p-select
        [options]="roleOptions()"
        [ngModel]="roleFilter()"
        (ngModelChange)="setRoleFilter($event)"
        optionLabel="label"
        optionValue="value"
        [placeholder]="t('users.anyRole')"
        [showClear]="true"
        [style]="{ minWidth: '12rem' }"
      />
      <div class="flag">
        <p-toggleswitch [ngModel]="activeOnly()" (ngModelChange)="setActiveOnly($event)" />
        <span>{{ t('users.activeOnly') }}</span>
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
            <th>{{ t('common.email') }}</th>
            <th>{{ t('users.column.roles') }}</th>
            <th>{{ t('common.status') }}</th>
            <th>{{ t('users.column.lastLogin') }}</th>
            <th style="width:12rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.fullName }}</td>
            <td>{{ row.email }}</td>
            <td>
              @for (role of row.roles; track role) {
                <p-tag [value]="role" severity="info" styleClass="role-tag" />
              }
            </td>
            <td>
              <p-tag
                [value]="row.isActive ? t('common.active') : t('common.inactive')"
                [severity]="row.isActive ? 'success' : 'secondary'"
              />
              @if (row.isLockedOut) {
                <p-tag [value]="t('users.status.locked')" severity="danger" styleClass="ml" />
              }
              @if (row.mustChangePassword) {
                <p-tag [value]="t('users.status.mustChange')" severity="warn" styleClass="ml" />
              }
            </td>
            <td>{{ row.lastLoginAt ? (row.lastLoginAt | date: 'yyyy-MM-dd HH:mm') : '—' }}</td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
              }
              @if (canAssignRoles()) {
                <p-button icon="pi pi-shield" [text]="true" (onClick)="openRoles(row)" />
              }
              @if (canResetPassword()) {
                <p-button icon="pi pi-key" [text]="true" (onClick)="openReset(row)" />
              }
              @if (canToggleActive()) {
                <p-button
                  [icon]="row.isActive ? 'pi pi-ban' : 'pi pi-check'"
                  [severity]="row.isActive ? 'warn' : 'success'"
                  [text]="true"
                  (onClick)="toggleStatus(row)"
                />
              }
              @if (canDelete()) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="6">
              <div class="state-panel"><i class="pi pi-users"></i>{{ t('users.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [(visible)]="userDialog"
      [header]="editing() ? t('users.dialog.editTitle') : t('users.dialog.createTitle')"
      [modal]="true"
      [style]="{ width: '620px' }"
    >
      @if (editing()) {
        <form [formGroup]="editForm" class="form-grid">
          <div class="field">
            <label>{{ t('users.label.fullName') }} *</label>
            <input pInputText formControlName="fullName" />
          </div>
          <div class="field">
            <label>{{ t('users.label.phone') }}</label>
            <input pInputText formControlName="phoneNumber" />
          </div>
          <div class="field">
            <label>{{ t('users.label.branch') }}</label>
            <p-select
              [options]="branchOptions()"
              formControlName="branchId"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [placeholder]="t('users.branchNone')"
            />
          </div>
          <div class="field">
            <label>{{ t('users.label.preferredLanguage') }}</label>
            <p-select [options]="languageOptions()" formControlName="preferredLanguage" optionLabel="label" optionValue="value" />
          </div>
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
          <div class="field full">
            <label>{{ t('users.label.notes') }}</label>
            <textarea pTextarea formControlName="notes" rows="2"></textarea>
          </div>
        </form>
      } @else {
        <form [formGroup]="createForm" class="form-grid">
          <div class="field">
            <label>{{ t('users.label.fullName') }} *</label>
            <input pInputText formControlName="fullName" />
          </div>
          <div class="field">
            <label>{{ t('common.email') }} *</label>
            <input pInputText formControlName="email" />
          </div>
          <div class="field">
            <label>{{ t('users.label.password') }} *</label>
            <p-password formControlName="password" [toggleMask]="true" [feedback]="false" />
          </div>
          <div class="field">
            <label>{{ t('users.label.phone') }}</label>
            <input pInputText formControlName="phoneNumber" />
          </div>
          <div class="field">
            <label>{{ t('users.label.branch') }}</label>
            <p-select
              [options]="branchOptions()"
              formControlName="branchId"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [placeholder]="t('users.branchNone')"
            />
          </div>
          <div class="field">
            <label>{{ t('users.label.preferredLanguage') }}</label>
            <p-select [options]="languageOptions()" formControlName="preferredLanguage" optionLabel="label" optionValue="value" />
          </div>
          <div class="field">
            <label>{{ t('users.label.roles') }} *</label>
            <p-multiselect
              [options]="roleOptions()"
              formControlName="roles"
              optionLabel="label"
              optionValue="value"
              [placeholder]="t('users.rolesPlaceholder')"
            />
            @if (createForm.controls.roles.touched && createForm.controls.roles.invalid) {
              <small class="field-error">{{ t('users.roleRequiredError') }}</small>
            }
          </div>
          <div class="field">
            <label>{{ t('users.label.mustChangePassword') }}</label>
            <p-toggleswitch formControlName="mustChangePassword" />
          </div>
          <div class="field">
            <label>{{ t('users.label.sendEmailConfirmation') }}</label>
            <p-toggleswitch formControlName="sendEmailConfirmation" />
          </div>
        </form>
      }

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="userDialog = false" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveUser()" />
      </ng-template>
    </p-dialog>

    <p-dialog [(visible)]="rolesDialog" [header]="t('users.dialog.assignRoles')" [modal]="true" [style]="{ width: '460px' }">
      <div class="field full">
        <label>{{ t('users.label.roles') }}</label>
        <p-multiselect
          [options]="roleOptions()"
          [ngModel]="selectedRoles()"
          (ngModelChange)="selectedRoles.set($event)"
          optionLabel="label"
          optionValue="value"
          [placeholder]="t('users.noRolesPlaceholder')"
          styleClass="w-full"
        />
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="rolesDialog = false" />
        <p-button [label]="t('users.saveRoles')" icon="pi pi-check" [loading]="saving()" (onClick)="saveRoles()" />
      </ng-template>
    </p-dialog>

    <p-dialog [(visible)]="resetDialog" [header]="t('users.dialog.resetPassword')" [modal]="true" [style]="{ width: '440px' }">
      <form [formGroup]="resetForm" class="form-grid">
        <div class="field full">
          <label>{{ t('users.label.newPassword') }} *</label>
          <p-password formControlName="newPassword" [toggleMask]="true" [feedback]="false" />
        </div>
        <div class="field full">
          <label>{{ t('users.label.requireChangeOnNextLogin') }}</label>
          <p-toggleswitch formControlName="mustChangePassword" />
        </div>
      </form>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="resetDialog = false" />
        <p-button [label]="t('common.reset')" icon="pi pi-key" [loading]="saving()" (onClick)="saveReset()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .role-tag {
        margin-right: 0.25rem;
      }
      .field-error {
        color: #dc2626;
        font-size: 0.78rem;
      }
      .ml {
        margin-left: 0.3rem;
      }
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
export class UsersComponent implements OnInit {
  private readonly service = inject(UsersService);
  private readonly rolesService = inject(RolesService);
  private readonly branchesService = inject(BranchesService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly languageOptions = computed<Option[]>(() => [
    { label: this.t('users.language.ar'), value: 'ar' },
    { label: this.t('users.language.en'), value: 'en' }
  ]);

  protected readonly rows = signal<UserDto[]>([]);
  protected readonly roleOptions = signal<Option[]>([]);
  protected readonly branchOptions = signal<Option[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly roleFilter = signal<string | null>(null);
  protected readonly activeOnly = signal(false);

  protected userDialog = false;
  protected rolesDialog = false;
  protected resetDialog = false;

  protected readonly editing = signal<UserDto | null>(null);
  protected readonly target = signal<UserDto | null>(null);
  protected readonly selectedRoles = signal<string[]>([]);

  protected readonly canCreate = signal(this.auth.hasPermission('Users.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Users.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Users.Delete'));
  protected readonly canAssignRoles = signal(this.auth.hasPermission('Users.AssignRoles'));
  protected readonly canResetPassword = signal(this.auth.hasPermission('Users.ResetPassword'));
  protected readonly canToggleActive = signal(this.auth.hasPermission('Users.ToggleActive'));

  protected readonly createForm = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    phoneNumber: [''],
    branchId: [null as string | null],
    preferredLanguage: ['ar'],
    roles: [
      [] as string[],
      [(control: AbstractControl) => ((control.value as string[] | null)?.length ? null : { required: true })]
    ],
    mustChangePassword: [false],
    sendEmailConfirmation: [false]
  });

  protected readonly editForm = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    phoneNumber: [''],
    branchId: [null as string | null],
    preferredLanguage: ['ar'],
    isActive: [true],
    notes: ['']
  });

  protected readonly resetForm = this.fb.nonNullable.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    mustChangePassword: [true]
  });

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.pageNumber.set(1);
      this.first.set(0);
      this.load();
    });

    this.loadOptions();
    this.load();
  }

  private loadOptions(): void {
    forkJoin({
      roles: this.canCreate() || this.canUpdate()
        ? this.rolesService.list({ pageNumber: 1, pageSize: 100 })
        : of(null),
      // Branches are tenant data: a platform administrator has no Branches.View and no
      // branch picker, so the request is skipped instead of failing with 403.
      branches: this.auth.hasPermission('Branches.View')
        ? this.branchesService.list({ pageNumber: 1, pageSize: 100 })
        : of(null)
    }).subscribe(({ roles, branches }) => {
      if (roles) {
        this.roleOptions.set(roles.items.map((role) => ({ label: role.name, value: role.name })));
      }
      this.branchOptions.set(branches ? branches.items.map((branch) => ({ label: branch.name, value: branch.id })) : []);
    });
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected setRoleFilter(value: string | null): void {
    this.roleFilter.set(value);
    this.reloadFirstPage();
  }

  protected setActiveOnly(value: boolean): void {
    this.activeOnly.set(value);
    this.reloadFirstPage();
  }

  private reloadFirstPage(): void {
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
        activeOnly: this.activeOnly() || undefined,
        role: this.roleFilter() || undefined
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
      fullName: '',
      email: '',
      password: '',
      phoneNumber: '',
      branchId: null,
      preferredLanguage: 'ar',
      roles: [],
      mustChangePassword: false,
      sendEmailConfirmation: false
    });
    this.userDialog = true;
  }

  protected openEdit(row: UserDto): void {
    this.editing.set(row);
    this.editForm.reset({
      fullName: row.fullName,
      phoneNumber: row.phoneNumber ?? '',
      branchId: row.branchId,
      preferredLanguage: row.preferredLanguage,
      isActive: row.isActive,
      notes: ''
    });
    this.userDialog = true;
  }

  protected openRoles(row: UserDto): void {
    this.target.set(row);
    this.selectedRoles.set([...row.roles]);
    this.rolesDialog = true;
  }

  protected openReset(row: UserDto): void {
    this.target.set(row);
    this.resetForm.reset({ newPassword: '', mustChangePassword: true });
    this.resetDialog = true;
  }

  protected saveUser(): void {
    const editing = this.editing();
    this.saving.set(true);

    if (editing) {
      if (this.editForm.invalid) {
        this.editForm.markAllAsTouched();
        this.saving.set(false);
        return;
      }

      const value = this.editForm.getRawValue();
      const request: UpdateUserRequest = {
        fullName: value.fullName,
        phoneNumber: value.phoneNumber || null,
        branchId: value.branchId,
        preferredLanguage: value.preferredLanguage,
        isActive: value.isActive,
        notes: value.notes || null
      };

      this.service
        .update(editing.id, request)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe(() => {
          this.notifications.success(this.t('users.toast.updated'));
          this.userDialog = false;
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
    const request: CreateUserRequest = {
      fullName: value.fullName,
      email: value.email,
      password: value.password,
      phoneNumber: value.phoneNumber || null,
      branchId: value.branchId,
      preferredLanguage: value.preferredLanguage,
      mustChangePassword: value.mustChangePassword,
      roles: value.roles,
      sendEmailConfirmation: value.sendEmailConfirmation
    };

    this.service
      .create(request)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('users.toast.created'));
        this.userDialog = false;
        this.load();
      });
  }

  protected saveRoles(): void {
    const target = this.target();

    if (!target) {
      return;
    }

    this.saving.set(true);
    this.service
      .assignRoles(target.id, { roles: this.selectedRoles() })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('users.toast.rolesUpdated'));
        this.rolesDialog = false;
        this.load();
      });
  }

  protected saveReset(): void {
    const target = this.target();

    if (!target) {
      return;
    }

    if (this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }

    const value = this.resetForm.getRawValue();
    this.saving.set(true);
    this.service
      .resetPassword(target.id, {
        newPassword: value.newPassword,
        mustChangePassword: value.mustChangePassword
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('users.toast.passwordReset'));
        this.resetDialog = false;
      });
  }

  protected toggleStatus(row: UserDto): void {
    const activate = !row.isActive;
    this.notifications
      .confirm({
        message: activate
          ? this.t('users.confirm.activateMessage', { name: row.fullName })
          : this.t('users.confirm.deactivateMessage', { name: row.fullName }),
        header: activate ? this.t('users.confirm.activateHeader') : this.t('users.confirm.deactivateHeader'),
        acceptLabel: activate ? this.t('users.confirm.activate') : this.t('users.confirm.deactivate'),
        acceptButtonStyleClass: activate ? undefined : 'p-button-danger',
        icon: activate ? 'pi pi-check' : 'pi pi-ban'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.setStatus(row.id, { isActive: activate, reason: null }).subscribe(() => {
          this.notifications.success(activate ? this.t('users.toast.activated') : this.t('users.toast.deactivated'));
          this.load();
        });
      });
  }

  protected remove(row: UserDto): void {
    this.notifications
      .confirm({
        message: this.t('users.confirm.deleteMessage', { name: row.fullName }),
        header: this.t('users.confirm.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('users.toast.deleted'));
          this.load();
        });
      });
  }
}

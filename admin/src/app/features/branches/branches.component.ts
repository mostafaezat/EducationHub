import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { BRANCH_STATUSES, BranchDto, BranchStatus } from '../../core/models';
import { TranslateService } from '../../core/i18n/translate.service';
import { AuthService } from '../../core/services/auth.service';
import { BranchesService } from '../../core/services/branches.service';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-branches',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('branches.title') }}</h1>
        <div class="subtitle">{{ t('branches.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('branches.newBranch')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input
        pInputText
        type="text"
        [placeholder]="t('branches.searchPlaceholder')"
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
            <th>{{ t('common.name') }}</th>
            <th>{{ t('common.code') }}</th>
            <th>{{ t('branches.column.city') }}</th>
            <th>{{ t('branches.column.main') }}</th>
            <th>{{ t('common.status') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.name }}<div class="muted">{{ row.nameAr }}</div></td>
            <td>{{ row.code ?? '—' }}</td>
            <td>{{ row.city ?? '—' }}</td>
            <td>
              @if (row.isMainBranch) {
                <p-tag [value]="t('branches.main')" severity="success" />
              } @else {
                <span class="muted">—</span>
              }
            </td>
            <td>
              <p-tag
                [value]="t('branches.status.' + row.status)"
                [severity]="row.status === 'Active' ? 'success' : 'secondary'"
              />
            </td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
              }
              @if (canDelete()) {
                <p-button
                  icon="pi pi-trash"
                  severity="danger"
                  [text]="true"
                  (onClick)="remove(row)"
                />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="6">
              <div class="state-panel"><i class="pi pi-sitemap"></i>{{ t('branches.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('branches.dialog.editTitle') : t('branches.dialog.createTitle')"
      [modal]="true"
      [style]="{ width: '640px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('common.name') }} *</label>
          <input pInputText formControlName="name" />
        </div>
        <div class="field">
          <label>{{ t('branches.label.nameAr') }}</label>
          <input pInputText formControlName="nameAr" />
        </div>
        <div class="field">
          <label>{{ t('common.code') }}</label>
          <input pInputText formControlName="code" />
        </div>
        <div class="field">
          <label>{{ t('branches.label.city') }}</label>
          <input pInputText formControlName="city" />
        </div>
        <div class="field">
          <label>{{ t('common.email') }}</label>
          <input pInputText formControlName="email" />
        </div>
        <div class="field">
          <label>{{ t('branches.label.phone') }}</label>
          <input pInputText formControlName="phone" />
        </div>
        @if (editing()) {
          <div class="field">
            <label>{{ t('common.status') }}</label>
            <p-select [options]="statuses()" formControlName="status" optionLabel="label" optionValue="value" />
          </div>
        }
        <div class="field">
          <label>{{ t('branches.label.mainBranch') }}</label>
          <p-toggleswitch formControlName="isMainBranch" />
        </div>
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
    `
  ]
})
export class BranchesComponent implements OnInit {
  private readonly service = inject(BranchesService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly statuses = computed(() =>
    BRANCH_STATUSES.map((value) => ({ label: this.t('branches.status.' + value), value }))
  );

  protected readonly rows = signal<BranchDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<BranchDto | null>(null);

  protected readonly canCreate = signal(this.auth.hasPermission('Branches.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Branches.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Branches.Delete'));

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    nameAr: [''],
    code: [''],
    city: [''],
    email: [''],
    phone: [''],
    status: ['Active' as BranchStatus],
    isMainBranch: [false]
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
    this.form.reset({
      name: '',
      nameAr: '',
      code: '',
      city: '',
      email: '',
      phone: '',
      status: 'Active',
      isMainBranch: false
    });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: BranchDto): void {
    this.editing.set(row);
    this.form.reset({
      name: row.name,
      nameAr: row.nameAr ?? '',
      code: row.code ?? '',
      city: row.city ?? '',
      email: row.email ?? '',
      phone: row.phone ?? '',
      status: row.status,
      isMainBranch: row.isMainBranch
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();
    this.saving.set(true);

    const request$ = editing
      ? this.service.update(editing.id, {
          name: value.name,
          nameAr: value.nameAr || null,
          code: value.code || null,
          city: value.city || null,
          email: value.email || null,
          phone: value.phone || null,
          address: null,
          addressAr: null,
          status: value.status,
          isMainBranch: value.isMainBranch
        })
      : this.service.create({
          name: value.name,
          nameAr: value.nameAr || null,
          code: value.code || null,
          city: value.city || null,
          email: value.email || null,
          phone: value.phone || null,
          address: null,
          addressAr: null,
          isMainBranch: value.isMainBranch
        });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(editing ? this.t('branches.toast.updated') : this.t('branches.toast.created'));
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected remove(row: BranchDto): void {
    this.notifications
      .confirm({
        message: this.t('branches.confirm.deleteMessage', { name: row.name }),
        header: this.t('branches.confirm.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('branches.toast.deleted'));
          this.load();
        });
      });
  }
}

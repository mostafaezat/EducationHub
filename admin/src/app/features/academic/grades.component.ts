import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { GradeLevelDto } from '../../core/models';
import { AcademicService } from '../../core/services/academic.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

@Component({
  selector: 'app-grades',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    InputTextModule,
    InputNumberModule,
    TableModule,
    TagModule,
    TextareaModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('academic.grades.title') }}</h1>
        <div class="subtitle">{{ t('academic.grades.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('academic.grades.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('academic.grades.searchPlaceholder')" (input)="onSearch($event)" />
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
            <th style="width:5rem">{{ t('academic.grades.order') }}</th>
            <th>{{ t('common.code') }}</th>
            <th>{{ t('common.name') }}</th>
            <th>{{ t('common.status') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.order }}</td>
            <td>{{ row.code }}</td>
            <td>{{ row.name }}<div class="muted">{{ row.nameAr }}</div></td>
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
              @if (canDelete()) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5">
              <div class="state-panel"><i class="pi pi-list"></i>{{ t('academic.grades.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('academic.grades.editDialog') : t('academic.grades.newDialog')"
      [modal]="true"
      [style]="{ width: '560px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('common.code') }} *</label>
          <input pInputText formControlName="code" />
        </div>
        <div class="field">
          <label>{{ t('academic.grades.order') }} *</label>
          <p-inputNumber formControlName="order" [min]="0" />
        </div>
        <div class="field">
          <label>{{ t('common.name') }} *</label>
          <input pInputText formControlName="name" />
        </div>
        <div class="field">
          <label>{{ t('academic.grades.nameArabic') }}</label>
          <input pInputText formControlName="nameAr" />
        </div>
        <div class="field">
          <label>{{ t('common.description') }}</label>
          <textarea pTextarea formControlName="description" rows="2"></textarea>
        </div>
        @if (editing()) {
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
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
    `
  ]
})
export class GradesComponent implements OnInit {
  private readonly service = inject(AcademicService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly rows = signal<GradeLevelDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<GradeLevelDto | null>(null);

  protected readonly canCreate = signal(this.auth.hasPermission('Grades.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Grades.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Grades.Delete'));

  protected readonly form = this.fb.nonNullable.group({
    code: ['', [Validators.required]],
    order: [0, [Validators.required]],
    name: ['', [Validators.required]],
    nameAr: [''],
    description: [''],
    isActive: [true]
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
      .listGrades({
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
    this.form.reset({ code: '', order: 0, name: '', nameAr: '', description: '', isActive: true });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: GradeLevelDto): void {
    this.editing.set(row);
    this.form.reset({
      code: row.code,
      order: row.order,
      name: row.name,
      nameAr: row.nameAr ?? '',
      description: row.description ?? '',
      isActive: row.isActive
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
      ? this.service.updateGrade(editing.id, {
          name: value.name,
          nameAr: value.nameAr || null,
          order: value.order,
          isActive: value.isActive,
          description: value.description || null,
          descriptionAr: null
        })
      : this.service.createGrade({
          code: value.code,
          name: value.name,
          nameAr: value.nameAr || null,
          order: value.order,
          description: value.description || null,
          descriptionAr: null
        });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(this.t(editing ? 'academic.grades.updated' : 'academic.grades.created'));
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected remove(row: GradeLevelDto): void {
    this.notifications
      .confirm({
        message: this.t('academic.grades.deleteMessage', { name: row.name }),
        header: this.t('academic.grades.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.removeGrade(row.id).subscribe(() => {
          this.notifications.success(this.t('academic.grades.deleted'));
          this.load();
        });
      });
  }
}

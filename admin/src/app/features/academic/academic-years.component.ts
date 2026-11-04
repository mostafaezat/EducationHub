import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ACADEMIC_YEAR_STATUSES, AcademicYearDto, AcademicYearStatus } from '../../core/models';
import { AcademicService } from '../../core/services/academic.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';

function toDateOnly(value: Date | null | undefined): string | null {
  if (!value) {
    return null;
  }

  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

const YEAR_STATUS_KEYS: Record<AcademicYearStatus, string> = {
  Upcoming: 'academic.years.status.upcoming',
  Current: 'academic.years.status.current',
  Archived: 'academic.years.status.archived'
};

@Component({
  selector: 'app-academic-years',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    DatePickerModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('academic.years.title') }}</h1>
        <div class="subtitle">{{ t('academic.years.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('academic.years.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('academic.years.searchPlaceholder')" (input)="onSearch($event)" />
      <div class="include-archived">
        <p-toggleswitch [ngModel]="includeArchived()" (ngModelChange)="toggleArchived($event)" />
        <span>{{ t('academic.years.includeArchived') }}</span>
      </div>
      <div class="spacer"></div>
      <input pInputText type="date" [value]="lookupDate()" (input)="onLookupDate($event)" />
      <p-button [label]="t('academic.years.findForDate')" icon="pi pi-search" [outlined]="true" (onClick)="lookup()" />
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="load()" />
    </div>

    @if (lookupResult()) {
      <div class="card lookup-result">{{ lookupResult() }}</div>
    }

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
            <th>{{ t('academic.years.start') }}</th>
            <th>{{ t('academic.years.end') }}</th>
            <th>{{ t('common.status') }}</th>
            <th>{{ t('academic.years.status.current') }}</th>
            <th style="width:11rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.name }}<div class="muted">{{ row.nameAr }}</div></td>
            <td>{{ row.startDate | date: 'yyyy-MM-dd' }}</td>
            <td>{{ row.endDate | date: 'yyyy-MM-dd' }}</td>
            <td><p-tag [value]="yearStatusLabel(row.status)" severity="secondary" /></td>
            <td>
              @if (row.isCurrent) {
                <p-tag [value]="t('academic.years.status.current')" severity="success" />
              } @else {
                <span class="muted">—</span>
              }
            </td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
                @if (!row.isCurrent) {
                  <p-button icon="pi pi-flag" [text]="true" (onClick)="setCurrent(row)" />
                }
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
              <div class="state-panel"><i class="pi pi-calendar"></i>{{ t('academic.years.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('academic.years.editDialog') : t('academic.years.newDialog')"
      [modal]="true"
      [style]="{ width: '600px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('common.name') }} *</label>
          <input pInputText formControlName="name" />
        </div>
        <div class="field">
          <label>{{ t('academic.years.nameArabic') }}</label>
          <input pInputText formControlName="nameAr" />
        </div>
        <div class="field">
          <label>{{ t('academic.years.startDate') }} *</label>
          <p-datepicker formControlName="startDate" dateFormat="yy-mm-dd" [showIcon]="true" />
        </div>
        <div class="field">
          <label>{{ t('academic.years.endDate') }} *</label>
          <p-datepicker formControlName="endDate" dateFormat="yy-mm-dd" [showIcon]="true" />
        </div>
        @if (editing()) {
          <div class="field">
            <label>{{ t('common.status') }}</label>
            <p-select
              [options]="statusOptions()"
              formControlName="status"
              optionLabel="label"
              optionValue="value"
            />
          </div>
        } @else {
          <div class="field">
            <label>{{ t('academic.years.markAsCurrent') }}</label>
            <p-toggleswitch formControlName="isCurrent" />
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
      .include-archived {
        display: flex;
        align-items: center;
        gap: 0.4rem;
        font-size: 0.85rem;
        color: #475569;
      }
      .lookup-result {
        margin-bottom: 1rem;
        font-size: 0.9rem;
      }
    `
  ]
})
export class AcademicYearsComponent implements OnInit {
  private readonly service = inject(AcademicService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly statusOptions = computed(() =>
    ACADEMIC_YEAR_STATUSES.map((value) => ({ label: this.t(YEAR_STATUS_KEYS[value]), value }))
  );

  protected readonly rows = signal<AcademicYearDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly includeArchived = signal(false);
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<AcademicYearDto | null>(null);
  protected readonly lookupDate = signal('');
  protected readonly lookupResult = signal<string | null>(null);

  protected readonly canCreate = signal(this.auth.hasPermission('AcademicYears.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('AcademicYears.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('AcademicYears.Delete'));

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required]],
    nameAr: [''],
    startDate: [null as Date | null, [Validators.required]],
    endDate: [null as Date | null, [Validators.required]],
    status: ['Upcoming' as AcademicYearStatus],
    isCurrent: [false]
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

  protected yearStatusLabel(status: AcademicYearStatus): string {
    return this.t(YEAR_STATUS_KEYS[status]);
  }

  protected onLookupDate(event: Event): void {
    this.lookupDate.set((event.target as HTMLInputElement).value);
  }

  protected toggleArchived(value: boolean): void {
    this.includeArchived.set(value);
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
      .listAcademicYears({
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize(),
        searchTerm: this.search() || undefined,
        includeArchived: this.includeArchived()
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected lookup(): void {
    const date = this.lookupDate();

    if (!date) {
      this.lookupResult.set(this.t('academic.years.chooseDateFirst'));
      return;
    }

    this.service.academicYearForDate(date).subscribe({
      next: (year) =>
        this.lookupResult.set(this.t('academic.years.lookupFound', { date, name: year.name })),
      error: () => this.lookupResult.set(this.t('academic.years.lookupNone', { date }))
    });
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({
      name: '',
      nameAr: '',
      startDate: null,
      endDate: null,
      status: 'Upcoming',
      isCurrent: false
    });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: AcademicYearDto): void {
    this.editing.set(row);
    this.form.reset({
      name: row.name,
      nameAr: row.nameAr ?? '',
      startDate: new Date(row.startDate),
      endDate: new Date(row.endDate),
      status: row.status,
      isCurrent: row.isCurrent
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const startDate = toDateOnly(value.startDate);
    const endDate = toDateOnly(value.endDate);

    if (!startDate || !endDate) {
      this.notifications.error(this.t('academic.years.datesRequired'));
      return;
    }

    const editing = this.editing();
    this.saving.set(true);

    const request$ = editing
      ? this.service.updateAcademicYear(editing.id, {
          name: value.name,
          nameAr: value.nameAr || null,
          startDate,
          endDate,
          status: value.status
        })
      : this.service.createAcademicYear({
          name: value.name,
          nameAr: value.nameAr || null,
          startDate,
          endDate,
          isCurrent: value.isCurrent
        });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(
          this.t(editing ? 'academic.years.updated' : 'academic.years.created')
        );
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected setCurrent(row: AcademicYearDto): void {
    this.notifications
      .confirm({
        message: this.t('academic.years.setCurrentMessage', { name: row.name }),
        header: this.t('academic.years.setCurrentHeader'),
        acceptLabel: this.t('academic.years.setCurrentAccept'),
        icon: 'pi pi-flag'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.setCurrentAcademicYear(row.id).subscribe(() => {
          this.notifications.success(this.t('academic.years.currentUpdated'));
          this.load();
        });
      });
  }

  protected remove(row: AcademicYearDto): void {
    this.notifications
      .confirm({
        message: this.t('academic.years.deleteMessage', { name: row.name }),
        header: this.t('academic.years.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }

        this.service.removeAcademicYear(row.id).subscribe(() => {
          this.notifications.success(this.t('academic.years.deleted'));
          this.load();
        });
      });
  }
}

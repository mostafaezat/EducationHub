import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { DatePickerModule } from 'primeng/datepicker';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize, switchMap } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  CompensationRateBasis,
  CompensationRuleDto,
  CreateTeacherRequest,
  SubjectDto,
  TeacherDto
} from '../../core/models';
import { AcademicService } from '../../core/services/academic.service';
import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { TeachersService } from '../../core/services/teachers.service';
import { UsersService } from '../../core/services/users.service';

interface Option {
  label: string;
  value: string;
}

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
  selector: 'app-teachers',
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    DatePickerModule,
    InputNumberModule,
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
        <h1>{{ t('teachers.title') }}</h1>
        <div class="subtitle">{{ t('teachers.subtitle') }}</div>
      </div>
      <div class="header-actions">
        <p-button [label]="t('teachers.rules.button')" icon="pi pi-wallet" [outlined]="true" (onClick)="openRules()" />
        @if (canCreate()) {
          <p-button [label]="t('teachers.new')" icon="pi pi-plus" (onClick)="openCreate()" />
        }
      </div>
    </div>

    <div class="toolbar">
      <input
        pInputText
        type="text"
        [placeholder]="t('teachers.searchPlaceholder')"
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
            <th style="width:8rem">{{ t('teachers.employeeNumber') }}</th>
            <th>{{ t('teachers.fullName') }}</th>
            <th>{{ t('teachers.subjects') }}</th>
            <th style="width:8rem">{{ t('teachers.hireDate') }}</th>
            <th style="width:7rem">{{ t('common.status') }}</th>
            <th style="width:9rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.employeeNumber || '—' }}</td>
            <td>
              {{ row.fullName }}
              <div class="muted">{{ row.email }}</div>
            </td>
            <td>
              <div class="tag-row">
                @for (subject of row.subjects; track subject.id) {
                  <p-tag [value]="subject.name" severity="info" />
                }
              </div>
            </td>
            <td>{{ row.hireDate ? (row.hireDate | date: 'yyyy-MM-dd') : '—' }}</td>
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
            <td colspan="6">
              <div class="state-panel"><i class="pi pi-users"></i>{{ t('teachers.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('teachers.editDialog') : t('teachers.newDialog')"
      [modal]="true"
      [style]="{ width: '640px' }"
    >
      @if (editing()) {
        <form [formGroup]="editForm" class="form-grid">
          <div class="field">
            <label>{{ t('teachers.employeeNumber') }}</label>
            <input pInputText formControlName="employeeNumber" />
          </div>
          <div class="field">
            <label>{{ t('teachers.phoneNumber') }}</label>
            <input pInputText formControlName="phoneNumber" />
          </div>
          <div class="field">
            <label>{{ t('teachers.hireDate') }}</label>
            <p-datepicker formControlName="hireDate" dateFormat="yy-mm-dd" [showIcon]="true" />
          </div>
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
          <div class="field">
            <label>{{ t('teachers.subjects') }}</label>
            <p-multiselect
              class="w-full"
              formControlName="subjectIds"
              [options]="subjectSelectOptions()"
              optionLabel="label"
              optionValue="value"
              [placeholder]="t('teachers.subjects')"
            />
          </div>
          <div class="field">
            <label>{{ t('teachers.notes') }}</label>
            <textarea pTextarea formControlName="notes" rows="2"></textarea>
          </div>
        </form>

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveEdit()" />
        </ng-template>
      } @else {
        <div class="mode-toggle">
          <p-button
            [label]="t('teachers.mode.new')"
            [outlined]="createMode() !== 'new'"
            (onClick)="setCreateMode('new')"
          />
          <p-button
            [label]="t('teachers.mode.link')"
            [outlined]="createMode() !== 'link'"
            (onClick)="setCreateMode('link')"
          />
        </div>

        @if (createMode() === 'new') {
          <form [formGroup]="createForm" class="form-grid">
            <div class="field">
              <label>{{ t('teachers.fullName') }} *</label>
              <input pInputText formControlName="fullName" />
              @if (createForm.controls.fullName.touched && createForm.controls.fullName.invalid) {
                <small class="field-error">{{ t('teachers.formIncomplete') }}</small>
              }
            </div>
            <div class="field">
              <label>{{ t('teachers.email') }} *</label>
              <input pInputText type="email" formControlName="email" />
              @if (createForm.controls.email.touched && createForm.controls.email.invalid) {
                <small class="field-error">{{ t('teachers.formIncomplete') }}</small>
              }
            </div>
            <div class="field">
              <label>{{ t('teachers.password') }} *</label>
              <p-password formControlName="password" [toggleMask]="true" [feedback]="false" styleClass="w-full" />
              @if (createForm.controls.password.touched && createForm.controls.password.invalid) {
                <small class="field-error">{{ t('teachers.formIncomplete') }}</small>
              }
            </div>
            <div class="field">
              <label>{{ t('teachers.phoneNumber') }}</label>
              <input pInputText formControlName="phoneNumber" />
            </div>
            <div class="field">
              <label>{{ t('teachers.employeeNumber') }}</label>
              <input pInputText formControlName="employeeNumber" />
            </div>
            <div class="field">
              <label>{{ t('teachers.hireDate') }}</label>
              <p-datepicker formControlName="hireDate" dateFormat="yy-mm-dd" [showIcon]="true" />
            </div>
            <div class="field">
              <label>{{ t('teachers.subjects') }}</label>
              <p-multiselect
                class="w-full"
                formControlName="subjectIds"
                [options]="subjectSelectOptions()"
                optionLabel="label"
                optionValue="value"
                [placeholder]="t('teachers.subjects')"
              />
            </div>
          </form>
        } @else {
          <form [formGroup]="linkForm" class="form-grid">
            <div class="field">
              <label>{{ t('teachers.linkUser') }} *</label>
              <p-select
                [options]="linkUserOptions()"
                formControlName="userId"
                optionLabel="label"
                optionValue="value"
                [showClear]="true"
                [placeholder]="t('teachers.linkUser')"
              />
              @if (linkForm.controls.userId.touched && linkForm.controls.userId.invalid) {
                <small class="field-error">{{ t('teachers.formIncomplete') }}</small>
              }
            </div>
            <div class="field">
              <label>{{ t('teachers.phoneNumber') }}</label>
              <input pInputText formControlName="phoneNumber" />
            </div>
            <div class="field">
              <label>{{ t('teachers.employeeNumber') }}</label>
              <input pInputText formControlName="employeeNumber" />
            </div>
            <div class="field">
              <label>{{ t('teachers.hireDate') }}</label>
              <p-datepicker formControlName="hireDate" dateFormat="yy-mm-dd" [showIcon]="true" />
            </div>
            <div class="field">
              <label>{{ t('teachers.subjects') }}</label>
              <p-multiselect
                class="w-full"
                formControlName="subjectIds"
                [options]="subjectSelectOptions()"
                optionLabel="label"
                optionValue="value"
                [placeholder]="t('teachers.subjects')"
              />
            </div>
          </form>
        }

        <ng-template pTemplate="footer">
          <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveCreate()" />
        </ng-template>
      }
    </p-dialog>

    <p-dialog
      [visible]="rulesVisible()"
      (visibleChange)="rulesVisible.set($event)"
      [header]="t('teachers.rules.title')"
      [modal]="true"
      [style]="{ width: '860px' }"
    >
      <div class="subtitle">{{ t('teachers.rules.subtitle') }}</div>
      <div class="toolbar">
        <div class="spacer"></div>
        @if (canCreate()) {
          <p-button [label]="t('teachers.rules.new')" icon="pi pi-plus" (onClick)="openRuleCreate()" />
        }
        <p-button icon="pi pi-refresh" [text]="true" (onClick)="loadRules()" />
      </div>

      <p-table
        [value]="rulesRows()"
        [lazy]="true"
        [paginator]="true"
        [rows]="rulesPageSize()"
        [first]="rulesFirst()"
        [totalRecords]="rulesTotal()"
        [loading]="rulesLoading()"
        (onLazyLoad)="onRulesLazyLoad($event)"
        styleClass="p-datatable-sm"
      >
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('teachers.rules.scope') }}</th>
            <th style="width:7rem">{{ t('teachers.rules.rate') }}</th>
            <th style="width:8rem">{{ t('teachers.rules.basis') }}</th>
            <th style="width:6rem">{{ t('common.status') }}</th>
            <th style="width:7rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ scopeLabel(row) }}</td>
            <td>{{ row.rate }}</td>
            <td>{{ basisLabel(row.rateBasis) }}</td>
            <td>
              <p-tag
                [value]="row.isActive ? t('common.active') : t('common.inactive')"
                [severity]="row.isActive ? 'success' : 'secondary'"
              />
            </td>
            <td>
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openRuleEdit(row)" />
              }
              @if (canDelete()) {
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="removeRule(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="5">
              <div class="state-panel"><i class="pi pi-wallet"></i>{{ t('teachers.rules.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </p-dialog>

    <p-dialog
      [visible]="ruleDialogVisible()"
      (visibleChange)="ruleDialogVisible.set($event)"
      [header]="ruleEditing() ? t('teachers.rules.editDialog') : t('teachers.rules.newDialog')"
      [modal]="true"
      [style]="{ width: '560px' }"
    >
      <form [formGroup]="ruleForm" class="form-grid">
        @if (ruleEditing(); as rule) {
          <div class="field">
            <label>{{ t('teachers.rules.scope') }}</label>
            <div class="scope-value">{{ scopeLabel(rule) }}</div>
          </div>
        } @else {
          <div class="field">
            <label>{{ t('teachers.rules.teacher') }}</label>
            <p-select
              [options]="ruleTeacherOptions()"
              formControlName="teacherProfileId"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [placeholder]="t('teachers.rules.anyTeacher')"
            />
          </div>
          <div class="field">
            <label>{{ t('teachers.rules.subject') }}</label>
            <p-select
              [options]="subjectSelectOptions()"
              formControlName="subjectId"
              optionLabel="label"
              optionValue="value"
              [showClear]="true"
              [placeholder]="t('teachers.rules.anySubject')"
            />
          </div>
        }
        <div class="field">
          <label>{{ t('teachers.rules.rate') }} *</label>
          <p-inputNumber formControlName="rate" [min]="0" mode="decimal" [useGrouping]="false" />
          @if (ruleForm.controls.rate.touched && ruleForm.controls.rate.invalid) {
            <small class="field-error">{{ t('teachers.formIncomplete') }}</small>
          }
        </div>
        <div class="field">
          <label>{{ t('teachers.rules.basis') }}</label>
          <p-select
            [options]="basisOptions()"
            formControlName="rateBasis"
            optionLabel="label"
            optionValue="value"
          />
        </div>
        @if (ruleEditing()) {
          <div class="field">
            <label>{{ t('common.active') }}</label>
            <p-toggleswitch formControlName="isActive" />
          </div>
        }
      </form>

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="ruleDialogVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="savingRule()" (onClick)="saveRule()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted {
        color: #94a3b8;
        font-size: 0.78rem;
      }
      .header-actions {
        display: flex;
        gap: 0.5rem;
        align-items: center;
      }
      .mode-toggle {
        display: flex;
        gap: 0.5rem;
        margin-bottom: 0.75rem;
      }
      .field-error {
        color: #dc2626;
        font-size: 0.78rem;
      }
      .scope-value {
        font-weight: 600;
      }
      .subtitle {
        color: #64748b;
        font-size: 0.85rem;
        margin-bottom: 0.5rem;
      }
    `
  ]
})
export class TeachersComponent implements OnInit {
  private readonly service = inject(TeachersService);
  private readonly academic = inject(AcademicService);
  private readonly usersService = inject(UsersService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly basisOptions = computed<Option[]>(() => [
    { label: this.t('teachers.rules.perSession'), value: 'PerSession' },
    { label: this.t('teachers.rules.perHour'), value: 'PerHour' }
  ]);

  protected readonly rows = signal<TeacherDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');

  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<TeacherDto | null>(null);
  protected readonly createMode = signal<'new' | 'link'>('new');

  protected readonly subjectOptions = signal<SubjectDto[]>([]);
  protected readonly linkUserOptions = signal<Option[]>([]);
  protected readonly ruleTeacherOptions = signal<Option[]>([]);

  protected readonly rulesVisible = signal(false);
  protected readonly ruleDialogVisible = signal(false);
  protected readonly ruleEditing = signal<CompensationRuleDto | null>(null);
  protected readonly rulesRows = signal<CompensationRuleDto[]>([]);
  protected readonly rulesTotal = signal(0);
  protected readonly rulesLoading = signal(false);
  protected readonly rulesPageNumber = signal(1);
  protected readonly rulesPageSize = signal(10);
  protected readonly rulesFirst = signal(0);
  protected readonly savingRule = signal(false);

  protected readonly canCreate = signal(this.auth.hasPermission('Teachers.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Teachers.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Teachers.Delete'));

  protected readonly subjectSelectOptions = computed<Option[]>(() =>
    this.subjectOptions().map((subject) => ({ label: subject.name, value: subject.id }))
  );

  protected readonly createForm = this.fb.nonNullable.group({
    fullName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
    phoneNumber: [''],
    employeeNumber: [''],
    hireDate: [null as Date | null],
    subjectIds: [new Array<string>()]
  });

  protected readonly linkForm = this.fb.nonNullable.group({
    userId: [null as string | null, [Validators.required]],
    phoneNumber: [''],
    employeeNumber: [''],
    hireDate: [null as Date | null],
    subjectIds: [new Array<string>()]
  });

  protected readonly editForm = this.fb.nonNullable.group({
    employeeNumber: [''],
    phoneNumber: [''],
    hireDate: [null as Date | null],
    isActive: [true],
    notes: [''],
    subjectIds: [new Array<string>()]
  });

  protected readonly ruleForm = this.fb.nonNullable.group({
    teacherProfileId: [null as string | null],
    subjectId: [null as string | null],
    rate: [0, [Validators.required, Validators.min(0)]],
    rateBasis: ['PerSession' as CompensationRateBasis],
    isActive: [true]
  });

  private subjectsLoaded = false;
  private linkUsersLoaded = false;
  private ruleLookupsLoaded = false;

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

  protected setCreateMode(mode: 'new' | 'link'): void {
    this.createMode.set(mode);
    if (mode === 'link') {
      this.ensureLinkUsers();
    }
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.createMode.set('new');
    this.createForm.reset({
      fullName: '',
      email: '',
      password: '',
      phoneNumber: '',
      employeeNumber: '',
      hireDate: null,
      subjectIds: []
    });
    this.linkForm.reset({
      userId: null,
      phoneNumber: '',
      employeeNumber: '',
      hireDate: null,
      subjectIds: []
    });
    this.ensureSubjects();
    this.dialogVisible.set(true);
  }

  protected openEdit(row: TeacherDto): void {
    this.editing.set(row);
    this.editForm.reset({
      employeeNumber: row.employeeNumber ?? '',
      phoneNumber: row.phoneNumber ?? '',
      hireDate: row.hireDate ? new Date(row.hireDate) : null,
      isActive: row.isActive,
      notes: row.notes ?? '',
      subjectIds: row.subjects.map((subject) => subject.id)
    });
    this.ensureSubjects();
    this.dialogVisible.set(true);
  }

  protected saveCreate(): void {
    const mode = this.createMode();
    const form = mode === 'new' ? this.createForm : this.linkForm;

    if (form.invalid) {
      form.markAllAsTouched();
      this.notifications.error(this.t('teachers.formIncomplete'));
      return;
    }

    let request: CreateTeacherRequest;

    if (mode === 'new') {
      const value = this.createForm.getRawValue();
      request = {
        employeeNumber: value.employeeNumber || null,
        email: value.email,
        password: value.password,
        fullName: value.fullName,
        phoneNumber: value.phoneNumber || null,
        userId: null,
        hireDate: toDateOnly(value.hireDate),
        subjectIds: value.subjectIds
      };
    } else {
      const value = this.linkForm.getRawValue();
      request = {
        employeeNumber: value.employeeNumber || null,
        email: null,
        password: null,
        fullName: null,
        phoneNumber: value.phoneNumber || null,
        userId: value.userId,
        hireDate: toDateOnly(value.hireDate),
        subjectIds: value.subjectIds
      };
    }

    this.saving.set(true);
    this.service
      .create(request)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success(this.t('teachers.created'));
          this.dialogVisible.set(false);
          this.load();
        }
      });
  }

  protected saveEdit(): void {
    const form = this.editForm;

    if (form.invalid) {
      form.markAllAsTouched();
      this.notifications.error(this.t('teachers.formIncomplete'));
      return;
    }

    const teacher = this.editing();
    if (!teacher) {
      return;
    }

    const value = form.getRawValue();
    const originalSubjects = teacher.subjects.map((subject) => subject.id);
    const subjectsChanged =
      value.subjectIds.length !== originalSubjects.length ||
      value.subjectIds.some((id) => !originalSubjects.includes(id));

    const update$ = this.service.update(teacher.id, {
      employeeNumber: value.employeeNumber || null,
      phoneNumber: value.phoneNumber || null,
      hireDate: toDateOnly(value.hireDate),
      isActive: value.isActive,
      notes: value.notes || null
    });

    const save$ = subjectsChanged
      ? update$.pipe(switchMap(() => this.service.assignSubjects(teacher.id, value.subjectIds)))
      : update$;

    this.saving.set(true);
    save$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(this.t('teachers.updated'));
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected remove(row: TeacherDto): void {
    this.notifications
      .confirm({
        message: this.t('teachers.deleteMessage', { name: row.fullName }),
        header: this.t('teachers.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('teachers.deleted'));
          this.load();
        });
      });
  }

  protected openRules(): void {
    this.rulesVisible.set(true);
    this.loadRules();
    this.ensureRuleLookups();
  }

  protected onRulesLazyLoad(event: TableLazyLoadEvent): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.rulesPageSize();
    this.rulesFirst.set(first);
    this.rulesPageSize.set(rows);
    this.rulesPageNumber.set(Math.floor(first / rows) + 1);
    this.loadRules();
  }

  protected loadRules(): void {
    this.rulesLoading.set(true);
    this.service
      .listRules({
        pageNumber: this.rulesPageNumber(),
        pageSize: this.rulesPageSize()
      })
      .pipe(finalize(() => this.rulesLoading.set(false)))
      .subscribe((page) => {
        this.rulesRows.set(page.items);
        this.rulesTotal.set(page.totalItems);
      });
  }

  protected openRuleCreate(): void {
    this.ruleEditing.set(null);
    this.ruleForm.reset({
      teacherProfileId: null,
      subjectId: null,
      rate: 0,
      rateBasis: 'PerSession',
      isActive: true
    });
    this.ruleDialogVisible.set(true);
  }

  protected openRuleEdit(rule: CompensationRuleDto): void {
    this.ruleEditing.set(rule);
    this.ruleForm.reset({
      teacherProfileId: rule.teacherProfileId,
      subjectId: rule.subjectId,
      rate: rule.rate,
      rateBasis: rule.rateBasis,
      isActive: rule.isActive
    });
    this.ruleDialogVisible.set(true);
  }

  protected saveRule(): void {
    const form = this.ruleForm;

    if (form.invalid) {
      form.markAllAsTouched();
      this.notifications.error(this.t('teachers.formIncomplete'));
      return;
    }

    const value = form.getRawValue();
    const editing = this.ruleEditing();

    this.savingRule.set(true);

    const request$ = editing
      ? this.service.updateRule(editing.id, {
          rate: value.rate,
          rateBasis: value.rateBasis,
          isActive: value.isActive
        })
      : this.service.createRule({
          teacherProfileId: value.teacherProfileId,
          subjectId: value.subjectId,
          studyGroupId: null,
          rate: value.rate,
          rateBasis: value.rateBasis
        });

    request$.pipe(finalize(() => this.savingRule.set(false))).subscribe({
      next: () => {
        this.notifications.success(
          this.t(editing ? 'teachers.rules.updated' : 'teachers.rules.created')
        );
        this.ruleDialogVisible.set(false);
        this.loadRules();
      }
    });
  }

  protected removeRule(rule: CompensationRuleDto): void {
    this.notifications
      .confirm({
        message: this.t('teachers.rules.deleteMessage'),
        header: this.t('teachers.rules.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.service.removeRule(rule.id).subscribe(() => {
          this.notifications.success(this.t('teachers.rules.deleted'));
          this.loadRules();
        });
      });
  }

  protected scopeLabel(rule: CompensationRuleDto): string {
    if (rule.studyGroupId) {
      return this.t('teachers.rules.scopeGroup');
    }

    const parts: string[] = [];
    if (rule.teacherProfileId) {
      parts.push(this.ruleTeacherName(rule.teacherProfileId));
    }
    if (rule.subjectId) {
      parts.push(this.subjectName(rule.subjectId));
    }

    return parts.length > 0 ? parts.join(' · ') : this.t('teachers.rules.scopeDefault');
  }

  protected basisLabel(basis: CompensationRateBasis): string {
    return basis === 'PerHour' ? this.t('teachers.rules.perHour') : this.t('teachers.rules.perSession');
  }

  private ruleTeacherName(id: string): string {
    return this.ruleTeacherOptions().find((option) => option.value === id)?.label ?? id.slice(0, 8);
  }

  private subjectName(id: string): string {
    return this.subjectOptions().find((subject) => subject.id === id)?.name ?? id.slice(0, 8);
  }

  private ensureSubjects(): void {
    if (this.subjectsLoaded) {
      return;
    }

    this.subjectsLoaded = true;
    this.academic.listSubjects({ pageNumber: 1, pageSize: 100 }).subscribe({
      next: (page) => this.subjectOptions.set(page.items),
      error: () => (this.subjectsLoaded = false)
    });
  }

  private ensureLinkUsers(): void {
    if (this.linkUsersLoaded) {
      return;
    }

    this.linkUsersLoaded = true;
    this.usersService.list({ pageNumber: 1, pageSize: 100 }).subscribe({
      next: (page) =>
        this.linkUserOptions.set(
          page.items
            .filter((user) => user.isActive)
            .map((user) => ({ label: `${user.fullName} (${user.email})`, value: user.id }))
        ),
      error: () => (this.linkUsersLoaded = false)
    });
  }

  private ensureRuleLookups(): void {
    if (this.ruleLookupsLoaded) {
      this.ensureSubjects();
      return;
    }

    this.ruleLookupsLoaded = true;
    this.ensureSubjects();
    this.service.list({ pageNumber: 1, pageSize: 100 }).subscribe({
      next: (page) =>
        this.ruleTeacherOptions.set(
          page.items.map((teacher) => ({
            label: `${teacher.fullName} (${teacher.email})`,
            value: teacher.id
          }))
        ),
      error: () => (this.ruleLookupsLoaded = false)
    });
  }
}

import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  CreateStudentRequest,
  GUARDIAN_RELATIONSHIPS,
  GuardianRelationship,
  STUDENT_STATUSES,
  StudentDto,
  StudentStatus
} from '../../core/models';
import { AcademicService } from '../../core/services/academic.service';
import { AuthService } from '../../core/services/auth.service';
import { BranchesService } from '../../core/services/branches.service';
import { NotificationService } from '../../core/services/notification.service';
import { StudentsService } from '../../core/services/students.service';
import { DocumentsDialogComponent } from '../shared/documents-dialog.component';
import { statusSeverity, toDateOnly } from './student-utils';

interface Option {
  label: string;
  value: string;
}

interface GuardianRow {
  parentProfileId: string | null;
  relationship: GuardianRelationship;
  isPrimary: boolean;
}

type AccountMode = 'none' | 'new';

@Component({
  selector: 'app-students',
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    CheckboxModule,
    DatePickerModule,
    DialogModule,
    InputTextModule,
    PasswordModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    DocumentsDialogComponent
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('students.title') }}</h1>
        <div class="subtitle">{{ t('students.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('students.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input
        pInputText
        type="text"
        [placeholder]="t('students.searchPlaceholder')"
        (input)="onSearch($event)"
      />
      <p-select
        [options]="gradeOptions()"
        [ngModel]="gradeFilter()"
        (ngModelChange)="onGradeFilter($event)"
        optionLabel="label"
        optionValue="value"
        [showClear]="true"
        [placeholder]="t('students.allGrades')"
      />
      <p-select
        [options]="statusOptions()"
        [ngModel]="statusFilter()"
        (ngModelChange)="onStatusFilter($event)"
        optionLabel="label"
        optionValue="value"
        [showClear]="true"
        [placeholder]="t('students.allStatuses')"
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
            <th style="width:8rem">{{ t('students.studentNumber') }}</th>
            <th>{{ t('students.fullName') }}</th>
            <th style="width:9rem">{{ t('students.grade') }}</th>
            <th>{{ t('students.guardians') }}</th>
            <th style="width:8rem">{{ t('common.status') }}</th>
            <th style="width:11rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.studentNumber || '—' }}</td>
            <td>
              {{ row.fullName }}
              @if (row.accountEmail) {
                <div class="muted"><i class="pi pi-user"></i> {{ row.accountEmail }}</div>
              }
            </td>
            <td>{{ row.gradeLevelName || '—' }}</td>
            <td>
              <div class="tag-row">
                @for (guardian of row.guardians; track guardian.parentProfileId) {
                  <p-tag
                    [value]="guardian.parentFullName + ' · ' + t('students.relationship.' + guardian.relationship)"
                    [severity]="guardian.isPrimary ? 'info' : 'secondary'"
                  />
                } @empty {
                  <span class="muted">—</span>
                }
              </div>
            </td>
            <td>
              <p-tag [value]="t('students.status.' + row.status)" [severity]="severity(row.status)" />
            </td>
            <td>
              @if (canViewFiles()) {
                <p-button icon="pi pi-paperclip" [text]="true" [title]="t('documents.title')" (onClick)="openDocuments(row)" />
              }
              @if (canUpdate()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
                <p-button icon="pi pi-users" [text]="true" (onClick)="openGuardians(row)" />
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
              <div class="state-panel"><i class="pi pi-graduation-cap"></i>{{ t('students.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('students.editDialog') : t('students.newDialog')"
      [modal]="true"
      [style]="{ width: '640px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('students.fullName') }} *</label>
          <input pInputText formControlName="fullName" />
          @if (form.controls.fullName.touched && form.controls.fullName.invalid) {
            <small class="field-error">{{ t('students.formIncomplete') }}</small>
          }
        </div>
        <div class="field">
          <label>{{ t('students.studentNumber') }}</label>
          <input pInputText formControlName="studentNumber" />
        </div>
        <div class="field">
          <label>{{ t('students.dateOfBirth') }}</label>
          <p-datepicker formControlName="dateOfBirth" dateFormat="yy-mm-dd" [showIcon]="true" />
        </div>
        <div class="field">
          <label>{{ t('students.enrollmentDate') }}</label>
          <p-datepicker formControlName="enrollmentDate" dateFormat="yy-mm-dd" [showIcon]="true" />
        </div>
        <div class="field">
          <label>{{ t('students.grade') }}</label>
          <p-select
            [options]="gradeOptions()"
            formControlName="gradeLevelId"
            optionLabel="label"
            optionValue="value"
            [showClear]="true"
            [placeholder]="t('students.grade')"
          />
        </div>
        <div class="field">
          <label>{{ t('students.branch') }}</label>
          <p-select
            [options]="branchOptions()"
            formControlName="branchId"
            optionLabel="label"
            optionValue="value"
            [showClear]="true"
            [placeholder]="t('students.branch')"
          />
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
        }
        <div class="field">
          <label>{{ t('students.notes') }}</label>
          <textarea pTextarea formControlName="notes" rows="2"></textarea>
        </div>
      </form>

      @if (!editing()) {
        <div class="account-box">
          <div class="mode-toggle">
            <p-button
              [label]="t('students.account.none')"
              [outlined]="accountMode() !== 'none'"
              (onClick)="accountMode.set('none')"
            />
            <p-button
              [label]="t('students.account.new')"
              [outlined]="accountMode() !== 'new'"
              (onClick)="accountMode.set('new')"
            />
          </div>
          @if (accountMode() === 'new') {
            <form [formGroup]="accountForm" class="form-grid">
              <div class="field">
                <label>{{ t('students.account.email') }} *</label>
                <input pInputText type="email" formControlName="email" />
              </div>
              <div class="field">
                <label>{{ t('students.account.password') }} *</label>
                <p-password formControlName="password" [toggleMask]="true" [feedback]="false" styleClass="w-full" />
              </div>
            </form>
            <small class="muted">{{ t('students.account.hint') }}</small>
          }
        </div>
      }

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
      </ng-template>
    </p-dialog>

    <p-dialog
      [visible]="guardiansVisible()"
      (visibleChange)="guardiansVisible.set($event)"
      [header]="t('students.guardiansDialog', { name: guardianStudent()?.fullName ?? '' })"
      [modal]="true"
      [style]="{ width: '720px' }"
    >
      <div class="subtitle">{{ t('students.guardiansHint') }}</div>
      @for (row of guardianRows(); track $index) {
        <div class="guardian-row">
          <p-select
            [options]="parentOptions()"
            [ngModel]="row.parentProfileId"
            (ngModelChange)="updateGuardian($index, { parentProfileId: $event })"
            optionLabel="label"
            optionValue="value"
            [filter]="true"
            [placeholder]="t('students.parent')"
            class="grow"
          />
          <p-select
            [options]="relationshipOptions()"
            [ngModel]="row.relationship"
            (ngModelChange)="updateGuardian($index, { relationship: $event })"
            optionLabel="label"
            optionValue="value"
          />
          <label class="primary-check">
            <p-checkbox [binary]="true" [ngModel]="row.isPrimary" (ngModelChange)="setPrimary($index, $event)" />
            {{ t('students.primary') }}
          </label>
          <p-button icon="pi pi-times" severity="danger" [text]="true" (onClick)="removeGuardian($index)" />
        </div>
      } @empty {
        <div class="state-panel"><i class="pi pi-users"></i>{{ t('students.noGuardians') }}</div>
      }
      <p-button [label]="t('students.addGuardian')" icon="pi pi-plus" [text]="true" (onClick)="addGuardian()" />

      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="guardiansVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveGuardians()" />
      </ng-template>
    </p-dialog>

    <app-documents-dialog [(visible)]="documentsVisible" owner="Student" [ownerId]="documentsFor()?.id ?? null" [title]="documentsFor()?.fullName ?? ''" />
  `,
  styles: [
    `
      .muted {
        color: #94a3b8;
        font-size: 0.78rem;
      }
      .mode-toggle {
        display: flex;
        gap: 0.5rem;
        margin: 0.75rem 0;
      }
      .field-error {
        color: #dc2626;
        font-size: 0.78rem;
      }
      .subtitle {
        color: #64748b;
        font-size: 0.85rem;
        margin-bottom: 0.5rem;
      }
      .guardian-row {
        display: flex;
        gap: 0.5rem;
        align-items: center;
        margin-bottom: 0.5rem;
      }
      .guardian-row .grow {
        flex: 1;
      }
      .primary-check {
        display: flex;
        gap: 0.35rem;
        align-items: center;
        white-space: nowrap;
      }
    `
  ]
})
export class StudentsComponent implements OnInit {
  private readonly service = inject(StudentsService);
  private readonly academic = inject(AcademicService);
  private readonly branches = inject(BranchesService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;
  protected readonly severity = statusSeverity;

  protected readonly rows = signal<StudentDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly gradeFilter = signal<string | null>(null);
  protected readonly statusFilter = signal<StudentStatus | null>(null);

  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<StudentDto | null>(null);
  protected readonly accountMode = signal<AccountMode>('none');

  protected readonly guardiansVisible = signal(false);
  protected readonly guardianStudent = signal<StudentDto | null>(null);
  protected readonly guardianRows = signal<GuardianRow[]>([]);

  protected readonly gradeOptions = signal<Option[]>([]);
  protected readonly branchOptions = signal<Option[]>([]);
  protected readonly parentOptions = signal<Option[]>([]);

  protected readonly canCreate = signal(this.auth.hasPermission('Students.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Students.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Students.Delete'));
  protected readonly canViewFiles = signal(this.auth.hasPermission('Files.View'));
  protected readonly documentsVisible = signal(false);
  protected readonly documentsFor = signal<StudentDto | null>(null);

  protected openDocuments(row: StudentDto): void {
    this.documentsFor.set(row);
    this.documentsVisible.set(true);
  }

  protected readonly statusOptions = computed<Option[]>(() =>
    STUDENT_STATUSES.map((status) => ({ label: this.t('students.status.' + status), value: status }))
  );

  protected readonly relationshipOptions = computed<Option[]>(() =>
    GUARDIAN_RELATIONSHIPS.map((r) => ({ label: this.t('students.relationship.' + r), value: r }))
  );

  protected readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(3)]],
    studentNumber: [''],
    dateOfBirth: [null as Date | null],
    enrollmentDate: [null as Date | null],
    gradeLevelId: [null as string | null],
    branchId: [null as string | null],
    status: ['Active' as StudentStatus],
    notes: ['']
  });

  protected readonly accountForm = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]]
  });

  private lookupsLoaded = false;

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.resetPaging();
      this.load();
    });
    this.ensureLookups();
    this.load();
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected onGradeFilter(value: string | null): void {
    this.gradeFilter.set(value);
    this.resetPaging();
    this.load();
  }

  protected onStatusFilter(value: StudentStatus | null): void {
    this.statusFilter.set(value);
    this.resetPaging();
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
        search: this.search() || undefined,
        gradeLevelId: this.gradeFilter() ?? undefined,
        status: this.statusFilter() ?? undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.accountMode.set('none');
    this.form.reset({
      fullName: '',
      studentNumber: '',
      dateOfBirth: null,
      enrollmentDate: null,
      gradeLevelId: null,
      branchId: null,
      status: 'Active',
      notes: ''
    });
    this.accountForm.reset({ email: '', password: '' });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: StudentDto): void {
    this.editing.set(row);
    this.form.reset({
      fullName: row.fullName,
      studentNumber: row.studentNumber ?? '',
      dateOfBirth: row.dateOfBirth ? new Date(row.dateOfBirth) : null,
      enrollmentDate: row.enrollmentDate ? new Date(row.enrollmentDate) : null,
      gradeLevelId: row.gradeLevelId ?? null,
      branchId: row.branchId ?? null,
      status: row.status,
      notes: row.notes ?? ''
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    const withAccount = !this.editing() && this.accountMode() === 'new';

    if (this.form.invalid || (withAccount && this.accountForm.invalid)) {
      this.form.markAllAsTouched();
      this.accountForm.markAllAsTouched();
      this.notifications.error(this.t('students.formIncomplete'));
      return;
    }

    const value = this.form.getRawValue();
    const editing = this.editing();

    const request$ = editing
      ? this.service.update(editing.id, {
          fullName: value.fullName,
          // "" clears the number on the server; null would leave it unchanged.
          studentNumber: value.studentNumber,
          dateOfBirth: toDateOnly(value.dateOfBirth),
          gradeLevelId: value.gradeLevelId,
          branchId: value.branchId,
          status: value.status,
          enrollmentDate: toDateOnly(value.enrollmentDate),
          notes: value.notes
        })
      : this.service.create(this.buildCreateRequest(withAccount));

    this.saving.set(true);
    request$.pipe(finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.notifications.success(this.t(editing ? 'students.updated' : 'students.created'));
        this.dialogVisible.set(false);
        this.load();
      }
    });
  }

  protected openGuardians(row: StudentDto): void {
    this.guardianStudent.set(row);
    this.guardianRows.set(
      row.guardians.map((g) => ({
        parentProfileId: g.parentProfileId,
        relationship: g.relationship,
        isPrimary: g.isPrimary
      }))
    );
    this.loadParents();
    this.guardiansVisible.set(true);
  }

  protected addGuardian(): void {
    const rows = this.guardianRows();
    this.guardianRows.set([
      ...rows,
      { parentProfileId: null, relationship: 'Guardian', isPrimary: rows.length === 0 }
    ]);
  }

  protected removeGuardian(index: number): void {
    this.guardianRows.set(this.guardianRows().filter((_, i) => i !== index));
  }

  protected updateGuardian(index: number, patch: Partial<GuardianRow>): void {
    this.guardianRows.set(this.guardianRows().map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  /** Only one primary contact: ticking one clears the others. */
  protected setPrimary(index: number, value: boolean): void {
    this.guardianRows.set(
      this.guardianRows().map((row, i) => ({ ...row, isPrimary: i === index ? value : value ? false : row.isPrimary }))
    );
  }

  protected saveGuardians(): void {
    const student = this.guardianStudent();
    if (!student) {
      return;
    }

    const rows = this.guardianRows();
    const ids = rows.map((r) => r.parentProfileId);

    if (ids.some((id) => !id) || new Set(ids).size !== ids.length) {
      this.notifications.error(this.t('students.guardiansInvalid'));
      return;
    }

    this.saving.set(true);
    this.service
      .setGuardians(
        student.id,
        rows.map((r) => ({ parentProfileId: r.parentProfileId as string, relationship: r.relationship, isPrimary: r.isPrimary }))
      )
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe({
        next: () => {
          this.notifications.success(this.t('students.guardiansSaved'));
          this.guardiansVisible.set(false);
          this.load();
        }
      });
  }

  protected remove(row: StudentDto): void {
    this.notifications
      .confirm({
        message: this.t('students.deleteMessage', { name: row.fullName }),
        header: this.t('students.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.service.remove(row.id).subscribe(() => {
          this.notifications.success(this.t('students.deleted'));
          this.load();
        });
      });
  }

  private buildCreateRequest(withAccount: boolean): CreateStudentRequest {
    const value = this.form.getRawValue();
    const account = this.accountForm.getRawValue();

    return {
      fullName: value.fullName,
      studentNumber: value.studentNumber || null,
      dateOfBirth: toDateOnly(value.dateOfBirth),
      gradeLevelId: value.gradeLevelId,
      branchId: value.branchId,
      enrollmentDate: toDateOnly(value.enrollmentDate),
      notes: value.notes || null,
      userId: null,
      accountEmail: withAccount ? account.email : null,
      accountPassword: withAccount ? account.password : null
    };
  }

  private resetPaging(): void {
    this.pageNumber.set(1);
    this.first.set(0);
  }

  private ensureLookups(): void {
    if (this.lookupsLoaded) {
      return;
    }
    this.lookupsLoaded = true;

    this.academic.listGrades({ pageNumber: 1, pageSize: 100 }).subscribe((page) => {
      this.gradeOptions.set(
        [...page.items]
          .sort((a, b) => a.order - b.order)
          .map((grade) => ({ label: grade.name, value: grade.id }))
      );
    });

    this.branches.list({ pageNumber: 1, pageSize: 100 }).subscribe((page) => {
      this.branchOptions.set(page.items.map((branch) => ({ label: branch.name, value: branch.id })));
    });
  }

  private loadParents(): void {
    this.service.listParents({ pageNumber: 1, pageSize: 100 }).subscribe((page) => {
      this.parentOptions.set(
        page.items.map((parent) => ({
          label: parent.phoneNumber ? `${parent.fullName} (${parent.phoneNumber})` : parent.fullName,
          value: parent.id
        }))
      );
    });
  }
}

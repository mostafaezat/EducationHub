import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { DAYS_OF_WEEK, DayOfWeekName, EnrollmentDto, StudyGroupDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { SchedulingService } from '../../core/services/scheduling.service';
import { Option, shortTime, todayIso, toTimeOnly } from '../shared/ops-utils';

/** Study groups with their roster and weekly timetable (Mission Phase 3). */
@Component({
  selector: 'app-groups',
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    ButtonModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('groups.title') }}</h1>
        <div class="subtitle">{{ t('groups.subtitle') }}</div>
      </div>
      @if (canCreate()) {
        <p-button [label]="t('groups.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="text" [placeholder]="t('groups.searchPlaceholder')" (input)="onSearch($event)" />
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
            <th>{{ t('groups.subject') }}</th>
            <th>{{ t('groups.teacher') }}</th>
            <th>{{ t('groups.schedule') }}</th>
            <th style="width:7rem">{{ t('groups.students') }}</th>
            <th>{{ t('common.status') }}</th>
            <th style="width:10rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>
              <strong>{{ row.name }}</strong>
              @if (row.code) {
                <div class="muted">{{ row.code }}</div>
              }
            </td>
            <td>{{ row.subjectName }}<div class="muted">{{ row.gradeLevelName }}</div></td>
            <td>{{ row.teacherName ?? '—' }}</td>
            <td>
              @for (slot of row.slots; track slot.id) {
                <div class="slot-chip">{{ t('days.' + slot.dayOfWeek) }} {{ time(slot.startTime) }}–{{ time(slot.endTime) }}</div>
              } @empty {
                <span class="muted">{{ t('groups.noSlots') }}</span>
              }
            </td>
            <td>{{ row.activeEnrollments }}@if (row.capacity) {<span class="muted"> / {{ row.capacity }}</span>}</td>
            <td>
              <p-tag [value]="row.isActive ? t('common.active') : t('common.inactive')" [severity]="row.isActive ? 'success' : 'secondary'" />
            </td>
            <td>
              <p-button icon="pi pi-users" [text]="true" [title]="t('groups.manage')" (onClick)="openManage(row)" />
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
              <div class="state-panel"><i class="pi pi-objects-column"></i>{{ t('groups.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog
      [visible]="dialogVisible()"
      (visibleChange)="dialogVisible.set($event)"
      [header]="editing() ? t('groups.editDialog') : t('groups.newDialog')"
      [modal]="true"
      [style]="{ width: '620px' }"
    >
      <form [formGroup]="form" class="form-grid">
        <div class="field">
          <label>{{ t('common.name') }} *</label>
          <input pInputText formControlName="name" />
        </div>
        <div class="field">
          <label>{{ t('common.code') }}</label>
          <input pInputText formControlName="code" />
        </div>
        <div class="field">
          <label>{{ t('groups.subject') }} *</label>
          <p-select [options]="subjects()" formControlName="subjectId" optionLabel="label" optionValue="value" [filter]="true" [placeholder]="t('common.selectOption')" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('groups.grade') }}</label>
          <p-select [options]="grades()" formControlName="gradeLevelId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('common.selectOption')" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('groups.teacher') }}</label>
          <p-select [options]="teachers()" formControlName="teacherProfileId" optionLabel="label" optionValue="value" [showClear]="true" [filter]="true" [placeholder]="t('common.selectOption')" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('groups.capacity') }}</label>
          <p-inputNumber formControlName="capacity" [min]="1" [showButtons]="false" />
        </div>
        <div class="field">
          <label>{{ t('groups.notes') }}</label>
          <textarea pTextarea formControlName="notes" rows="2"></textarea>
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

    <p-dialog
      [visible]="manageVisible()"
      (visibleChange)="manageVisible.set($event)"
      [header]="managed()?.name ?? ''"
      [modal]="true"
      [style]="{ width: '820px' }"
    >
      @if (managed(); as group) {
        <h3 class="section-title">{{ t('groups.weeklySchedule') }}</h3>
        <div class="slot-list">
          @for (slot of group.slots; track slot.id) {
            <div class="slot-row">
              <span><strong>{{ t('days.' + slot.dayOfWeek) }}</strong> {{ time(slot.startTime) }}–{{ time(slot.endTime) }}</span>
              <span class="muted">{{ slot.room }}</span>
              @if (canUpdate()) {
                <p-button icon="pi pi-times" severity="danger" [text]="true" size="small" (onClick)="removeSlot(slot.id)" />
              }
            </div>
          } @empty {
            <div class="muted">{{ t('groups.noSlots') }}</div>
          }
        </div>
        @if (canUpdate()) {
          <div class="inline-form">
            <p-select [options]="dayOptions" [(ngModel)]="slotDay" optionLabel="label" optionValue="value" appendTo="body" />
            <input pInputText type="time" [(ngModel)]="slotStart" />
            <input pInputText type="time" [(ngModel)]="slotEnd" />
            <input pInputText [(ngModel)]="slotRoom" [placeholder]="t('groups.room')" style="width:7rem" />
            <p-button icon="pi pi-plus" [label]="t('groups.addSlot')" size="small" (onClick)="addSlot()" />
          </div>
        }

        <h3 class="section-title">{{ t('groups.roster') }} ({{ activeEnrollments().length }})</h3>
        @if (canUpdate()) {
          <div class="inline-form">
            <p-select
              [options]="studentOptions()"
              [(ngModel)]="enrollStudentId"
              optionLabel="label"
              optionValue="value"
              [filter]="true"
              (onFilter)="searchStudents($event.filter)"
              [placeholder]="t('groups.pickStudent')"
              appendTo="body"
              style="min-width:18rem"
            />
            <input pInputText type="date" [(ngModel)]="enrollDate" />
            <p-button icon="pi pi-user-plus" [label]="t('groups.enroll')" size="small" (onClick)="enroll()" />
          </div>
        }
        <p-table [value]="enrollments()" styleClass="p-datatable-sm" [scrollable]="true" scrollHeight="320px">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('groups.student') }}</th>
              <th>{{ t('groups.enrolledOn') }}</th>
              <th>{{ t('common.status') }}</th>
              <th style="width:4rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.studentName }} <span class="muted">{{ row.studentNumber }}</span></td>
              <td>{{ row.enrolledOn }}@if (row.endedOn) { → {{ row.endedOn }} }</td>
              <td><p-tag [value]="t('enrollmentStatus.' + row.status)" [severity]="row.status === 'Active' ? 'success' : 'secondary'" /></td>
              <td>
                @if (canUpdate() && row.status === 'Active') {
                  <p-button icon="pi pi-sign-out" severity="danger" [text]="true" size="small" [title]="t('groups.withdraw')" (onClick)="withdraw(row)" />
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="4"><div class="muted">{{ t('groups.noStudents') }}</div></td></tr>
          </ng-template>
        </p-table>
      }
    </p-dialog>
  `,
  styles: [
    `
      .muted { color: #94a3b8; font-size: 0.8rem; }
      .slot-chip { font-size: 0.8rem; white-space: nowrap; }
      .section-title { font-size: 0.95rem; margin: 1rem 0 0.5rem; }
      .slot-list { display: flex; flex-direction: column; gap: 0.25rem; margin-bottom: 0.5rem; }
      .slot-row { display: flex; align-items: center; gap: 1rem; padding: 0.25rem 0; border-bottom: 1px solid #f1f5f9; }
      .inline-form { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin: 0.5rem 0 0.75rem; }
    `
  ]
})
export class GroupsComponent implements OnInit {
  private readonly service = inject(SchedulingService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(FormBuilder);
  private readonly search$ = new Subject<string>();
  private readonly studentSearch$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;
  protected readonly time = shortTime;

  protected readonly rows = signal<StudyGroupDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(10);
  protected readonly first = signal(0);
  protected readonly search = signal('');
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<StudyGroupDto | null>(null);
  protected readonly manageVisible = signal(false);
  protected readonly managed = signal<StudyGroupDto | null>(null);
  protected readonly enrollments = signal<EnrollmentDto[]>([]);
  protected readonly activeEnrollments = signal<EnrollmentDto[]>([]);

  protected readonly subjects = signal<Option[]>([]);
  protected readonly grades = signal<Option[]>([]);
  protected readonly teachers = signal<Option[]>([]);
  protected readonly studentOptions = signal<Option[]>([]);

  protected readonly canCreate = signal(this.auth.hasPermission('Groups.Create'));
  protected readonly canUpdate = signal(this.auth.hasPermission('Groups.Update'));
  protected readonly canDelete = signal(this.auth.hasPermission('Groups.Delete'));

  protected readonly dayOptions = DAYS_OF_WEEK.map((d) => ({ label: this.t('days.' + d), value: d }));
  protected slotDay: DayOfWeekName = 'Sunday';
  protected slotStart = '16:00';
  protected slotEnd = '17:00';
  protected slotRoom = '';
  protected enrollStudentId: string | null = null;
  protected enrollDate = todayIso();

  protected readonly form = this.fb.group({
    name: ['', [Validators.required]],
    code: [''],
    subjectId: ['', [Validators.required]],
    gradeLevelId: [null as string | null],
    teacherProfileId: [null as string | null],
    capacity: [null as number | null],
    notes: [''],
    isActive: [true]
  });

  ngOnInit(): void {
    this.search$.pipe(debounceTime(300)).subscribe((value) => {
      this.search.set(value);
      this.pageNumber.set(1);
      this.first.set(0);
      this.load();
    });
    this.studentSearch$
      .pipe(debounceTime(300))
      .subscribe((value) => this.lookups.students(value).subscribe((o) => this.studentOptions.set(o)));

    this.lookups.subjects().subscribe((o) => this.subjects.set(o));
    this.lookups.grades().subscribe((o) => this.grades.set(o));
    this.lookups.teachers().subscribe((o) => this.teachers.set(o));
    this.load();
  }

  protected onSearch(event: Event): void {
    this.search$.next((event.target as HTMLInputElement).value);
  }

  protected searchStudents(value: string): void {
    this.studentSearch$.next(value ?? '');
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
      .listGroups({ pageNumber: this.pageNumber(), pageSize: this.pageSize(), search: this.search() || undefined })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form.reset({ name: '', code: '', subjectId: '', gradeLevelId: null, teacherProfileId: null, capacity: null, notes: '', isActive: true });
    this.dialogVisible.set(true);
  }

  protected openEdit(row: StudyGroupDto): void {
    this.editing.set(row);
    this.form.reset({
      name: row.name,
      code: row.code ?? '',
      subjectId: row.subjectId,
      gradeLevelId: row.gradeLevelId ?? null,
      teacherProfileId: row.teacherProfileId ?? null,
      capacity: row.capacity ?? null,
      notes: row.notes ?? '',
      isActive: row.isActive
    });
    this.dialogVisible.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const editing = this.editing();
    const body = {
      name: v.name ?? '',
      code: v.code || null,
      subjectId: v.subjectId ?? '',
      gradeLevelId: v.gradeLevelId || null,
      teacherProfileId: v.teacherProfileId || null,
      capacity: v.capacity ?? null,
      notes: v.notes || null
    };
    this.saving.set(true);

    const request$ = editing
      ? this.service.updateGroup(editing.id, { ...body, isActive: v.isActive ?? true })
      : this.service.createGroup(body);

    request$.pipe(finalize(() => this.saving.set(false))).subscribe(() => {
      this.notifications.success(this.t(editing ? 'groups.updated' : 'groups.created'));
      this.dialogVisible.set(false);
      this.load();
    });
  }

  protected remove(row: StudyGroupDto): void {
    this.notifications
      .confirm({
        message: this.t('groups.deleteMessage', { name: row.name }),
        header: this.t('groups.deleteHeader'),
        acceptLabel: this.t('common.delete'),
        acceptButtonStyleClass: 'p-button-danger',
        icon: 'pi pi-trash'
      })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.removeGroup(row.id).subscribe(() => {
            this.notifications.success(this.t('groups.deleted'));
            this.load();
          });
        }
      });
  }

  protected openManage(row: StudyGroupDto): void {
    this.managed.set(row);
    this.enrollments.set([]);
    this.enrollStudentId = null;
    this.manageVisible.set(true);
    this.refreshManaged();

    if (this.canUpdate() && this.studentOptions().length === 0) {
      this.lookups.students().subscribe((o) => this.studentOptions.set(o));
    }
  }

  protected addSlot(): void {
    const group = this.managed();

    if (!group || !this.slotStart || !this.slotEnd) {
      return;
    }

    this.service
      .addSlot(group.id, {
        dayOfWeek: this.slotDay,
        startTime: toTimeOnly(this.slotStart),
        endTime: toTimeOnly(this.slotEnd),
        room: this.slotRoom || null
      })
      .subscribe(() => {
        this.notifications.success(this.t('groups.slotAdded'));
        this.refreshManaged();
      });
  }

  protected removeSlot(slotId: string): void {
    const group = this.managed();

    if (group) {
      this.service.removeSlot(group.id, slotId).subscribe(() => this.refreshManaged());
    }
  }

  protected enroll(): void {
    const group = this.managed();

    if (!group || !this.enrollStudentId) {
      this.notifications.warn(this.t('groups.pickStudent'));
      return;
    }

    this.service.enroll(group.id, this.enrollStudentId, this.enrollDate).subscribe(() => {
      this.notifications.success(this.t('groups.enrolled'));
      this.enrollStudentId = null;
      this.refreshManaged();
    });
  }

  protected withdraw(row: EnrollmentDto): void {
    this.notifications
      .confirm({ message: this.t('groups.withdrawMessage', { name: row.studentName ?? '' }), header: this.t('groups.withdraw') })
      .subscribe((confirmed) => {
        const group = this.managed();

        if (confirmed && group) {
          this.service.withdraw(group.id, row.id).subscribe(() => this.refreshManaged());
        }
      });
  }

  private refreshManaged(): void {
    const group = this.managed();

    if (!group) {
      return;
    }

    this.service.getGroup(group.id).subscribe((fresh) => this.managed.set(fresh));
    this.service.enrollments(group.id).subscribe((rows) => {
      this.enrollments.set(rows);
      this.activeEnrollments.set(rows.filter((r) => r.status === 'Active'));
    });
    this.load();
  }
}

import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ATTENDANCE_STATUSES, AttendanceStatus, ClassSessionDto, SessionStatus } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { SchedulingService } from '../../core/services/scheduling.service';
import { Option, shortTime, todayIso } from '../shared/ops-utils';

interface AttendanceEdit {
  studentProfileId: string;
  studentName: string;
  studentNumber?: string | null;
  status: AttendanceStatus | null;
  notes: string;
}

/** Class sessions generated from the weekly timetable, and attendance taking (Mission Phase 3). */
@Component({
  selector: 'app-sessions',
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputTextModule, SelectModule, TableModule, TagModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('sessions.title') }}</h1>
        <div class="subtitle">{{ t('sessions.subtitle') }}</div>
      </div>
      @if (canManage()) {
        <p-button [label]="t('sessions.generate')" icon="pi pi-calendar-plus" (onClick)="generateVisible.set(true)" />
      }
    </div>

    <div class="toolbar">
      <input pInputText type="date" [(ngModel)]="from" (change)="reload()" [title]="t('common.from')" />
      <input pInputText type="date" [(ngModel)]="to" (change)="reload()" [title]="t('common.to')" />
      <p-select [options]="groups()" [(ngModel)]="groupId" optionLabel="label" optionValue="value" [showClear]="true" [filter]="true" [placeholder]="t('sessions.allGroups')" (onChange)="reload()" />
      <p-select [options]="statusOptions" [(ngModel)]="status" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('common.status')" (onChange)="reload()" />
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
            <th>{{ t('sessions.date') }}</th>
            <th>{{ t('sessions.time') }}</th>
            <th>{{ t('sessions.group') }}</th>
            <th>{{ t('groups.teacher') }}</th>
            <th>{{ t('sessions.topic') }}</th>
            <th>{{ t('sessions.attendance') }}</th>
            <th>{{ t('common.status') }}</th>
            <th style="width:11rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.date }}</td>
            <td>{{ time(row.startTime) }}–{{ time(row.endTime) }}</td>
            <td>{{ row.groupName }}</td>
            <td>{{ row.teacherName ?? '—' }}</td>
            <td>{{ row.topic ?? '' }}</td>
            <td>{{ row.presentCount }} / {{ row.markedCount }}</td>
            <td><p-tag [value]="t('sessionStatus.' + row.status)" [severity]="severity(row.status)" /></td>
            <td>
              @if (canViewAttendance() && row.status !== 'Cancelled') {
                <p-button icon="pi pi-check-square" [text]="true" [title]="t('sessions.attendance')" (onClick)="openAttendance(row)" />
              }
              @if (canManage() && row.status === 'Scheduled') {
                <p-button icon="pi pi-check" [text]="true" severity="success" [title]="t('sessions.markCompleted')" (onClick)="setStatus(row, 'Completed')" />
                <p-button icon="pi pi-ban" [text]="true" severity="danger" [title]="t('sessions.cancel')" (onClick)="setStatus(row, 'Cancelled')" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr>
            <td colspan="8">
              <div class="state-panel"><i class="pi pi-calendar"></i>{{ t('sessions.empty') }}</div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog [visible]="generateVisible()" (visibleChange)="generateVisible.set($event)" [header]="t('sessions.generate')" [modal]="true" [style]="{ width: '480px' }">
      <p class="muted">{{ t('sessions.generateHint') }}</p>
      <div class="form-grid">
        <div class="field">
          <label>{{ t('common.from') }}</label>
          <input pInputText type="date" [(ngModel)]="genFrom" />
        </div>
        <div class="field">
          <label>{{ t('common.to') }}</label>
          <input pInputText type="date" [(ngModel)]="genTo" />
        </div>
        <div class="field">
          <label>{{ t('sessions.group') }}</label>
          <p-select [options]="groups()" [(ngModel)]="genGroupId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('sessions.allGroups')" appendTo="body" />
        </div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="generateVisible.set(false)" />
        <p-button [label]="t('sessions.generate')" icon="pi pi-check" [loading]="saving()" (onClick)="generate()" />
      </ng-template>
    </p-dialog>

    <p-dialog [visible]="attendanceVisible()" (visibleChange)="attendanceVisible.set($event)" [header]="attendanceHeader()" [modal]="true" [style]="{ width: '760px' }">
      @if (canMark()) {
        <div class="inline-form">
          <span class="muted">{{ t('sessions.markAll') }}</span>
          @for (s of attendanceStatuses; track s) {
            <p-button [label]="t('attendanceStatus.' + s)" size="small" [outlined]="true" (onClick)="markAll(s)" />
          }
        </div>
      }
      <p-table [value]="roster()" styleClass="p-datatable-sm" [scrollable]="true" scrollHeight="420px">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('groups.student') }}</th>
            <th>{{ t('common.status') }}</th>
            <th>{{ t('sessions.notes') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.studentName }} <span class="muted">{{ row.studentNumber }}</span></td>
            <td>
              <div class="status-buttons">
                @for (s of attendanceStatuses; track s) {
                  <button type="button" class="status-btn" [class.on]="row.status === s" [attr.data-status]="s" [disabled]="!canMark()" (click)="row.status = s">
                    {{ t('attendanceStatus.' + s) }}
                  </button>
                }
              </div>
            </td>
            <td><input pInputText [(ngModel)]="row.notes" [disabled]="!canMark()" style="width:100%" /></td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="3"><div class="muted">{{ t('groups.noStudents') }}</div></td></tr>
        </ng-template>
      </p-table>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.close')" severity="secondary" [text]="true" (onClick)="attendanceVisible.set(false)" />
        @if (canMark()) {
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveAttendance()" />
        }
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted { color: #94a3b8; font-size: 0.8rem; }
      .inline-form { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; margin-bottom: 0.75rem; }
      .status-buttons { display: flex; gap: 0.25rem; flex-wrap: wrap; }
      .status-btn { border: 1px solid #cbd5e1; background: #fff; border-radius: 6px; padding: 0.2rem 0.55rem; font-size: 0.78rem; cursor: pointer; }
      .status-btn:disabled { cursor: default; }
      .status-btn.on[data-status='Present'] { background: #dcfce7; border-color: #16a34a; color: #166534; }
      .status-btn.on[data-status='Absent'] { background: #fee2e2; border-color: #dc2626; color: #991b1b; }
      .status-btn.on[data-status='Late'] { background: #fef3c7; border-color: #d97706; color: #92400e; }
      .status-btn.on[data-status='Excused'] { background: #e0e7ff; border-color: #4f46e5; color: #3730a3; }
    `
  ]
})
export class SessionsComponent implements OnInit {
  private readonly service = inject(SchedulingService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly t = inject(TranslateService).t;
  protected readonly time = shortTime;
  protected readonly attendanceStatuses = ATTENDANCE_STATUSES;
  protected readonly statusOptions = (['Scheduled', 'Completed', 'Cancelled'] as SessionStatus[]).map((s) => ({
    label: this.t('sessionStatus.' + s),
    value: s
  }));

  protected readonly rows = signal<ClassSessionDto[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly first = signal(0);
  protected readonly groups = signal<Option[]>([]);
  protected readonly generateVisible = signal(false);
  protected readonly attendanceVisible = signal(false);
  protected readonly attendanceHeader = signal('');
  protected readonly roster = signal<AttendanceEdit[]>([]);

  protected readonly canManage = signal(this.auth.hasPermission('Sessions.Manage'));
  protected readonly canViewAttendance = signal(this.auth.hasPermission('Attendance.View'));
  protected readonly canMark = signal(this.auth.hasPermission('Attendance.Mark'));

  protected from = todayIso(-7);
  protected to = todayIso(14);
  protected groupId: string | null = null;
  protected status: SessionStatus | null = null;
  protected genFrom = todayIso();
  protected genTo = todayIso(30);
  protected genGroupId: string | null = null;
  private attendanceSessionId: string | null = null;

  ngOnInit(): void {
    this.lookups.groups().subscribe((o) => this.groups.set(o));
    this.load();
  }

  protected severity(status: SessionStatus): 'info' | 'success' | 'danger' {
    return status === 'Completed' ? 'success' : status === 'Cancelled' ? 'danger' : 'info';
  }

  protected onLazyLoad(event: TableLazyLoadEvent): void {
    const first = event.first ?? 0;
    const rows = event.rows ?? this.pageSize();
    this.first.set(first);
    this.pageSize.set(rows);
    this.pageNumber.set(Math.floor(first / rows) + 1);
    this.load();
  }

  protected reload(): void {
    this.pageNumber.set(1);
    this.first.set(0);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .listSessions({
        pageNumber: this.pageNumber(),
        pageSize: this.pageSize(),
        from: this.from || undefined,
        to: this.to || undefined,
        studyGroupId: this.groupId ?? undefined,
        status: this.status ?? undefined
      })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((page) => {
        this.rows.set(page.items);
        this.total.set(page.totalItems);
      });
  }

  protected generate(): void {
    if (!this.genFrom || !this.genTo) {
      return;
    }

    this.saving.set(true);
    this.service
      .generateSessions(this.genFrom, this.genTo, this.genGroupId)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe((result) => {
        this.notifications.success(this.t('sessions.generated', { created: result.created, skipped: result.skipped }));
        this.generateVisible.set(false);
        this.load();
      });
  }

  protected setStatus(row: ClassSessionDto, status: SessionStatus): void {
    this.service.updateSession(row.id, { status }).subscribe(() => {
      this.notifications.success(this.t('sessions.updated'));
      this.load();
    });
  }

  protected openAttendance(row: ClassSessionDto): void {
    this.attendanceSessionId = row.id;
    this.attendanceHeader.set(`${row.groupName ?? ''} · ${row.date} ${shortTime(row.startTime)}`);
    this.roster.set([]);
    this.attendanceVisible.set(true);
    this.service.attendance(row.id).subscribe((rows) =>
      this.roster.set(
        rows.map((r) => ({
          studentProfileId: r.studentProfileId,
          studentName: r.studentName,
          studentNumber: r.studentNumber,
          status: r.status ?? null,
          notes: r.notes ?? ''
        }))
      )
    );
  }

  protected markAll(status: AttendanceStatus): void {
    this.roster.set(this.roster().map((r) => ({ ...r, status })));
  }

  protected saveAttendance(): void {
    const id = this.attendanceSessionId;
    const records = this.roster()
      .filter((r) => r.status !== null)
      .map((r) => ({ studentProfileId: r.studentProfileId, status: r.status as AttendanceStatus, notes: r.notes || null }));

    if (!id || records.length === 0) {
      this.notifications.warn(this.t('sessions.nothingMarked'));
      return;
    }

    this.saving.set(true);
    this.service
      .markAttendance(id, records)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('sessions.attendanceSaved'));
        this.attendanceVisible.set(false);
        this.load();
      });
  }
}

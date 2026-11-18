import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  AttendanceStatus,
  FamilyExamDto,
  FamilyHomeworkDto,
  FamilyStudentOverviewDto,
  SubmissionFileDto,
  SubmissionStatus
} from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FamilyService } from '../../core/services/family.service';
import { NotificationService } from '../../core/services/notification.service';
import { money, shortTime } from '../shared/ops-utils';
import { saveBlob } from '../shared/save-blob';

type FamilyTab = 'overview' | 'sessions' | 'homework' | 'exams';
type Severity = 'success' | 'info' | 'warn' | 'danger' | 'secondary';

/**
 * A student's own learning, or a parent's view of each child: groups, attendance, sessions,
 * homework with the child's own submission, exams with the child's own result, and fees.
 * Everything comes from GET /api/my/family, which the server scopes to the caller's account.
 */
@Component({
  selector: 'app-my-family',
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputTextModule, TableModule, TagModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t(isParent() ? 'family.titleParent' : 'family.titleStudent') }}</h1>
        <div class="subtitle">{{ t(isParent() ? 'family.subtitleParent' : 'family.subtitleStudent') }}</div>
      </div>
      <p-button icon="pi pi-refresh" [text]="true" [loading]="loading()" (onClick)="load()" />
    </div>

    @if (!loading() && children().length === 0) {
      <div class="card">
        <div class="state-panel"><i class="pi pi-users"></i>{{ t('family.empty') }}</div>
      </div>
    }

    @if (children().length > 1) {
      <div class="child-picker">
        @for (child of children(); track child.studentProfileId) {
          <button
            type="button"
            class="child-chip"
            [class.on]="child.studentProfileId === selected()?.studentProfileId"
            (click)="select(child.studentProfileId)"
          >
            <i class="pi pi-user"></i>{{ child.fullName }}
          </button>
        }
      </div>
    }

    @if (selected(); as c) {
      <div class="summary-grid">
        <div class="summary">
          <span>{{ t('family.attendanceRate') }}</span>
          <strong>{{ c.attendance.attendanceRate !== null && c.attendance.attendanceRate !== undefined ? c.attendance.attendanceRate + '%' : '—' }}</strong>
          <small>{{ t('family.attendanceBreakdown', { present: c.attendance.present, absent: c.attendance.absent, late: c.attendance.late }) }}</small>
        </div>
        <div class="summary">
          <span>{{ t('family.nextSession') }}</span>
          @if (c.upcomingSessions[0]; as next) {
            <strong>{{ next.session.date }}</strong>
            <small>{{ next.session.groupName }} · {{ time(next.session.startTime) }}</small>
          } @else {
            <strong>—</strong>
          }
        </div>
        <div class="summary">
          <span>{{ t('family.pendingHomework') }}</span>
          <strong>{{ pendingHomework(c) }}</strong>
          <small>{{ t('family.ofTotal', { total: c.homework.length }) }}</small>
        </div>
        <div class="summary" [class.warn]="(c.balance?.outstanding ?? 0) > 0">
          <span>{{ t('family.outstandingFees') }}</span>
          <strong>{{ money(c.balance?.outstanding) }}</strong>
          @if (c.balance && c.balance.credit > 0) {
            <small>{{ t('family.credit') }}: {{ money(c.balance.credit) }}</small>
          }
        </div>
      </div>

      <div class="tabs">
        <button type="button" [class.on]="tab() === 'overview'" (click)="tab.set('overview')">{{ t('family.tabs.overview') }}</button>
        <button type="button" [class.on]="tab() === 'sessions'" (click)="tab.set('sessions')">{{ t('family.tabs.sessions') }}</button>
        <button type="button" [class.on]="tab() === 'homework'" (click)="tab.set('homework')">{{ t('family.tabs.homework') }}</button>
        <button type="button" [class.on]="tab() === 'exams'" (click)="tab.set('exams')">{{ t('family.tabs.exams') }}</button>
      </div>

      @switch (tab()) {
        @case ('overview') {
          <div class="card">
            <h3>{{ t('family.groups') }}</h3>
            <p-table [value]="c.groups" styleClass="p-datatable-sm">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ t('family.group') }}</th>
                  <th>{{ t('family.subject') }}</th>
                  <th>{{ t('family.teacher') }}</th>
                  <th>{{ t('family.schedule') }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-g>
                <tr>
                  <td>{{ g.name }}</td>
                  <td>{{ g.subjectName || '—' }}</td>
                  <td>{{ g.teacherName || '—' }}</td>
                  <td>
                    @for (slot of g.slots; track slot.id) {
                      <div>{{ t('days.' + slot.dayOfWeek) }} {{ time(slot.startTime) }}–{{ time(slot.endTime) }}</div>
                    } @empty {
                      —
                    }
                  </td>
                </tr>
              </ng-template>
              <ng-template pTemplate="emptymessage">
                <tr><td colspan="4"><div class="state-panel">{{ t('family.noGroups') }}</div></td></tr>
              </ng-template>
            </p-table>
          </div>

          <div class="card">
            <h3>{{ t('family.upcoming') }}</h3>
            <ng-container *ngTemplateOutlet="sessionTable; context: { $implicit: c.upcomingSessions, showAttendance: false }" />
          </div>
        }

        @case ('sessions') {
          <div class="card">
            <h3>{{ t('family.recent') }}</h3>
            <ng-container *ngTemplateOutlet="sessionTable; context: { $implicit: c.recentSessions, showAttendance: true }" />
          </div>
          <div class="card">
            <h3>{{ t('family.upcoming') }}</h3>
            <ng-container *ngTemplateOutlet="sessionTable; context: { $implicit: c.upcomingSessions, showAttendance: false }" />
          </div>
        }

        @case ('homework') {
          <div class="card">
            <p-table [value]="c.homework" styleClass="p-datatable-sm">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ t('family.homeworkTitle') }}</th>
                  <th>{{ t('family.group') }}</th>
                  <th style="width:8rem">{{ t('family.dueDate') }}</th>
                  <th style="width:8rem">{{ t('common.status') }}</th>
                  <th style="width:7rem">{{ t('family.grade') }}</th>
                  <th>{{ t('family.feedback') }}</th>
                  <th style="width:13rem">{{ t('family.answer') }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-h>
                <tr>
                  <td>
                    <strong>{{ h.title }}</strong>
                    @if (h.description) {
                      <div class="muted">{{ h.description }}</div>
                    }
                  </td>
                  <td>{{ h.groupName || '—' }}</td>
                  <td>{{ h.dueDate }}</td>
                  <td><p-tag [value]="t('submissionStatus.' + h.status)" [severity]="submissionSeverity(h)" /></td>
                  <td>{{ gradeText(h) }}</td>
                  <td>{{ h.feedback || '—' }}</td>
                  <td>
                    @for (file of h.files; track file.id) {
                      <div class="answer-file">
                        <button type="button" class="file-link" [title]="file.note ?? ''" (click)="download(file)">
                          <i class="pi pi-paperclip"></i> {{ file.fileName }}
                        </button>
                        @if (h.canSubmit) {
                          <button type="button" class="file-remove" [title]="t('family.removeAnswer')" (click)="removeAnswer(h, file)">
                            <i class="pi pi-times"></i>
                          </button>
                        }
                      </div>
                    }
                    @if (h.canSubmit) {
                      <p-button
                        [label]="t(h.files.length ? 'family.addAnswer' : 'family.uploadAnswer')"
                        icon="pi pi-upload"
                        size="small"
                        [text]="h.files.length > 0"
                        (onClick)="openUpload(h)"
                      />
                    } @else if (h.files.length === 0) {
                      <span class="muted">—</span>
                    }
                  </td>
                </tr>
              </ng-template>
              <ng-template pTemplate="emptymessage">
                <tr><td colspan="7"><div class="state-panel">{{ t('family.noHomework') }}</div></td></tr>
              </ng-template>
            </p-table>
          </div>
        }

        @case ('exams') {
          <div class="card">
            <p-table [value]="c.exams" styleClass="p-datatable-sm">
              <ng-template pTemplate="header">
                <tr>
                  <th>{{ t('family.examTitle') }}</th>
                  <th>{{ t('family.group') }}</th>
                  <th style="width:8rem">{{ t('family.examDate') }}</th>
                  <th style="width:9rem">{{ t('family.result') }}</th>
                  <th>{{ t('family.notes') }}</th>
                </tr>
              </ng-template>
              <ng-template pTemplate="body" let-e>
                <tr>
                  <td><strong>{{ e.title }}</strong></td>
                  <td>{{ e.groupName || '—' }}</td>
                  <td>{{ e.examDate }}</td>
                  <td>
                    @if (e.isAbsent) {
                      <p-tag [value]="t('family.absent')" severity="danger" />
                    } @else if (e.recorded && e.score !== null && e.score !== undefined) {
                      <strong>{{ e.score }}</strong> / {{ e.maxScore }}
                    } @else {
                      <span class="muted">{{ t(isUpcoming(e) ? 'family.upcomingExam' : 'family.notRecorded') }}</span>
                    }
                  </td>
                  <td>{{ e.notes || '—' }}</td>
                </tr>
              </ng-template>
              <ng-template pTemplate="emptymessage">
                <tr><td colspan="5"><div class="state-panel">{{ t('family.noExams') }}</div></td></tr>
              </ng-template>
            </p-table>
          </div>
        }
      }
    }

    <p-dialog
      [visible]="uploadFor() !== null"
      (visibleChange)="!$event && closeUpload()"
      [modal]="true"
      [header]="t('family.uploadTitle', { title: uploadFor()?.title ?? '' })"
      [style]="{ width: '30rem' }"
    >
      <div class="field">
        <label>{{ t('family.answerFile') }} *</label>
        <input type="file" accept="image/*,.pdf,.doc,.docx,.txt" (change)="pickFile($event)" />
        <small class="muted">{{ t('family.answerFileHint') }}</small>
      </div>
      <div class="field">
        <label>{{ t('family.answerNote') }}</label>
        <input pInputText [(ngModel)]="uploadNote" maxlength="500" class="w-full" />
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="closeUpload()" />
        <p-button [label]="t('family.send')" icon="pi pi-upload" [loading]="uploading()" [disabled]="!uploadFile" (onClick)="upload()" />
      </ng-template>
    </p-dialog>

    <ng-template #sessionTable let-rows let-showAttendance="showAttendance">
      <p-table [value]="rows" styleClass="p-datatable-sm">
        <ng-template pTemplate="header">
          <tr>
            <th style="width:8rem">{{ t('family.date') }}</th>
            <th style="width:8rem">{{ t('family.time') }}</th>
            <th>{{ t('family.group') }}</th>
            <th>{{ t('family.topic') }}</th>
            <th style="width:8rem">{{ t('common.status') }}</th>
            @if (showAttendance) {
              <th style="width:8rem">{{ t('family.attendance') }}</th>
            }
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.session.date }}</td>
            <td>{{ time(row.session.startTime) }}–{{ time(row.session.endTime) }}</td>
            <td>{{ row.session.groupName || '—' }}</td>
            <td>{{ row.session.topic || '—' }}</td>
            <td>{{ t('sessionStatus.' + row.session.status) }}</td>
            @if (showAttendance) {
              <td>
                @if (row.attendance) {
                  <p-tag [value]="t('attendanceStatus.' + row.attendance)" [severity]="attendanceSeverity(row.attendance)" />
                } @else {
                  <span class="muted">—</span>
                }
              </td>
            }
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td [attr.colspan]="showAttendance ? 6 : 5"><div class="state-panel">{{ t('family.noSessions') }}</div></td></tr>
        </ng-template>
      </p-table>
    </ng-template>
  `,
  styles: [
    `
      .subtitle,
      .muted {
        color: #64748b;
        font-size: 0.85rem;
      }
      h3 {
        margin: 0 0 0.75rem;
        font-size: 1rem;
      }
      .card + .card {
        margin-top: 1rem;
      }
      .child-picker {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        margin-bottom: 1rem;
      }
      .child-chip {
        display: inline-flex;
        align-items: center;
        gap: 0.4rem;
        border: 1px solid #d5dce8;
        background: #fff;
        border-radius: 999px;
        padding: 0.4rem 0.9rem;
        font: inherit;
        cursor: pointer;
        color: #334155;
      }
      .child-chip.on {
        border-color: #2563eb;
        color: #2563eb;
        font-weight: 600;
        background: #eff6ff;
      }
      .summary-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 0.75rem;
        margin-bottom: 1rem;
      }
      .summary {
        background: #fff;
        border: 1px solid #e3e8f0;
        border-radius: 10px;
        padding: 0.9rem 1rem;
        display: flex;
        flex-direction: column;
        gap: 0.2rem;
      }
      .summary span {
        color: #64748b;
        font-size: 0.8rem;
      }
      .summary strong {
        font-size: 1.35rem;
      }
      .summary small {
        color: #64748b;
      }
      .summary.warn strong {
        color: #b45309;
      }
      .answer-file {
        display: flex;
        align-items: center;
        gap: 0.25rem;
      }
      .file-link,
      .file-remove {
        background: none;
        border: none;
        cursor: pointer;
        font: inherit;
        font-size: 0.82rem;
        padding: 0.1rem 0;
      }
      .file-link {
        color: #2563eb;
        text-align: start;
      }
      .file-remove {
        color: #94a3b8;
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 0.35rem;
        margin-bottom: 1rem;
      }
    `
  ]
})
export class MyFamilyComponent implements OnInit {
  private readonly service = inject(FamilyService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly t = inject(TranslateService).t;
  protected readonly money = money;
  protected readonly time = shortTime;

  protected readonly loading = signal(false);
  protected readonly children = signal<FamilyStudentOverviewDto[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly tab = signal<FamilyTab>('overview');
  protected readonly isParent = computed(() => this.auth.hasRole('Parent'));

  protected readonly uploadFor = signal<FamilyHomeworkDto | null>(null);
  protected readonly uploading = signal(false);
  protected uploadFile: File | null = null;
  protected uploadNote = '';

  protected readonly selected = computed<FamilyStudentOverviewDto | null>(() => {
    const list = this.children();
    return list.find((c) => c.studentProfileId === this.selectedId()) ?? list[0] ?? null;
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .overview()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((rows) => this.children.set(rows ?? []));
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected pendingHomework(child: FamilyStudentOverviewDto): number {
    return child.homework.filter((h) => h.status === 'Pending' || h.status === 'Missing').length;
  }

  protected gradeText(h: FamilyHomeworkDto): string {
    if (h.grade === null || h.grade === undefined) {
      return '—';
    }

    return h.maxGrade ? `${h.grade} / ${h.maxGrade}` : String(h.grade);
  }

  protected isUpcoming(e: FamilyExamDto): boolean {
    const today = new Date().toISOString().slice(0, 10);
    return e.examDate >= today;
  }

  protected submissionSeverity(h: FamilyHomeworkDto): Severity {
    const map: Record<SubmissionStatus, Severity> = {
      Pending: 'info',
      Submitted: 'secondary',
      Graded: 'success',
      Missing: 'danger'
    };
    return map[h.status] ?? 'secondary';
  }

  protected attendanceSeverity(status: AttendanceStatus): Severity {
    const map: Record<AttendanceStatus, Severity> = {
      Present: 'success',
      Late: 'warn',
      Excused: 'info',
      Absent: 'danger'
    };
    return map[status] ?? 'secondary';
  }

  protected openUpload(h: FamilyHomeworkDto): void {
    this.uploadFile = null;
    this.uploadNote = '';
    this.uploadFor.set(h);
  }

  protected closeUpload(): void {
    this.uploadFor.set(null);
  }

  protected pickFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFile = input.files && input.files.length > 0 ? input.files[0] : null;
  }

  protected upload(): void {
    const homework = this.uploadFor();
    const child = this.selected();

    if (!homework || !child || !this.uploadFile) {
      return;
    }

    this.uploading.set(true);
    this.service
      .submitHomework(homework.id, child.studentProfileId, this.uploadFile, this.uploadNote.trim() || null)
      .pipe(finalize(() => this.uploading.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('family.answerSent'));
        this.closeUpload();
        this.load();
      });
  }

  protected removeAnswer(h: FamilyHomeworkDto, file: SubmissionFileDto): void {
    this.service.removeHomeworkFile(h.id, file.id).subscribe(() => {
      this.notifications.success(this.t('family.answerRemoved'));
      this.load();
    });
  }

  protected download(file: SubmissionFileDto): void {
    this.service.downloadOwnFile(file.id).subscribe((blob) => saveBlob(blob, file.fileName));
  }
}

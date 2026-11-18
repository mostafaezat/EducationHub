import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { HomeworkDto, SUBMISSION_STATUSES, SubmissionFileDto, SubmissionRowDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FilesService } from '../../core/services/files.service';
import { LearningService } from '../../core/services/learning.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { Option, todayIso } from '../shared/ops-utils';
import { saveBlob } from '../shared/save-blob';

/** Homework per group, with a submissions/grading sheet (Mission Phase 6). */
@Component({
  selector: 'app-homework',
  imports: [CommonModule, FormsModule, ButtonModule, DialogModule, InputNumberModule, InputTextModule, SelectModule, TableModule, TagModule, TextareaModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('homework.title') }}</h1>
        <div class="subtitle">{{ t('homework.subtitle') }}</div>
      </div>
      @if (canManage()) {
        <p-button [label]="t('homework.new')" icon="pi pi-plus" (onClick)="openCreate()" />
      }
    </div>

    <div class="toolbar">
      <p-select [options]="groups()" [(ngModel)]="groupId" optionLabel="label" optionValue="value" [showClear]="true" [filter]="true" [placeholder]="t('sessions.allGroups')" (onChange)="load()" />
      <div class="spacer"></div>
      <p-button icon="pi pi-refresh" [text]="true" (onClick)="load()" />
    </div>

    <div class="card">
      <p-table [value]="rows()" [loading]="loading()" styleClass="p-datatable-sm">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('homework.titleField') }}</th>
            <th>{{ t('sessions.group') }}</th>
            <th>{{ t('homework.assignedOn') }}</th>
            <th>{{ t('homework.dueDate') }}</th>
            <th>{{ t('homework.progress') }}</th>
            <th style="width:10rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td><strong>{{ row.title }}</strong><div class="muted">{{ row.description }}</div></td>
            <td>{{ row.groupName }}</td>
            <td>{{ row.assignedOn }}</td>
            <td>
              {{ row.dueDate }}
              @if (row.dueDate < today) { <p-tag [value]="t('homework.pastDue')" severity="secondary" /> }
            </td>
            <td>{{ t('homework.progressValue', { submitted: row.submitted, graded: row.graded, total: row.rosterSize }) }}</td>
            <td>
              <p-button icon="pi pi-list-check" [text]="true" [title]="t('homework.submissions')" (onClick)="openSubmissions(row)" />
              @if (canManage()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="6"><div class="state-panel"><i class="pi pi-pencil"></i>{{ t('homework.empty') }}</div></td></tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog [visible]="dialogVisible()" (visibleChange)="dialogVisible.set($event)" [header]="editing() ? t('homework.edit') : t('homework.new')" [modal]="true" [style]="{ width: '540px' }">
      <div class="form-grid">
        @if (!editing()) {
          <div class="field">
            <label>{{ t('sessions.group') }} *</label>
            <p-select [options]="groups()" [(ngModel)]="form.studyGroupId" optionLabel="label" optionValue="value" [filter]="true" [placeholder]="t('common.selectOption')" appendTo="body" />
          </div>
        }
        <div class="field"><label>{{ t('homework.titleField') }} *</label><input pInputText [(ngModel)]="form.title" /></div>
        <div class="field"><label>{{ t('common.description') }}</label><textarea pTextarea rows="3" [(ngModel)]="form.description"></textarea></div>
        <div class="field"><label>{{ t('homework.dueDate') }} *</label><input pInputText type="date" [(ngModel)]="form.dueDate" /></div>
        <div class="field"><label>{{ t('homework.maxGrade') }}</label><p-inputNumber [(ngModel)]="form.maxGrade" [min]="1" [maxFractionDigits]="2" /></div>
      </div>
      @if (!editing()) {
        <p class="muted">{{ t('homework.notifyHint') }}</p>
      }
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
      </ng-template>
    </p-dialog>

    <p-dialog [visible]="sheetVisible()" (visibleChange)="sheetVisible.set($event)" [header]="sheetTitle()" [modal]="true" [style]="{ width: '820px' }">
      <p-table [value]="sheet()" styleClass="p-datatable-sm" [scrollable]="true" scrollHeight="440px">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('groups.student') }}</th>
            <th style="width:11rem">{{ t('common.status') }}</th>
            <th style="width:8rem">{{ t('homework.grade') }}</th>
            <th>{{ t('homework.feedback') }}</th>
            <th style="width:11rem">{{ t('homework.answers') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.studentName }}</td>
            <td><p-select [options]="statusOptions" [(ngModel)]="row.status" optionLabel="label" optionValue="value" [disabled]="!canManage()" appendTo="body" /></td>
            <td><p-inputNumber [(ngModel)]="row.grade" [min]="0" [max]="maxGrade() ?? undefined" [maxFractionDigits]="2" [disabled]="!canManage()" inputStyleClass="w-grade" /></td>
            <td><input pInputText [(ngModel)]="row.feedback" [disabled]="!canManage()" style="width:100%" /></td>
            <td>
              @for (file of row.files ?? []; track file.id) {
                <button type="button" class="file-link" [title]="file.note ?? ''" (click)="downloadAnswer(file)">
                  <i class="pi pi-paperclip"></i> {{ file.fileName }}
                </button>
              } @empty {
                <span class="muted">—</span>
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="5"><div class="muted">{{ t('groups.noStudents') }}</div></td></tr>
        </ng-template>
      </p-table>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.close')" severity="secondary" [text]="true" (onClick)="sheetVisible.set(false)" />
        @if (canManage()) {
          <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveSheet()" />
        }
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted { color: #94a3b8; font-size: 0.8rem; }
      .file-link { display: block; background: none; border: none; color: #2563eb; cursor: pointer; padding: 0.1rem 0; font: inherit; font-size: 0.82rem; text-align: start; }
      :host ::ng-deep .w-grade { width: 6rem; }
    `
  ]
})
export class HomeworkComponent implements OnInit {
  private readonly service = inject(LearningService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly files = inject(FilesService);

  protected readonly t = inject(TranslateService).t;
  protected readonly today = todayIso();
  protected readonly statusOptions = SUBMISSION_STATUSES.map((s) => ({ label: this.t('submissionStatus.' + s), value: s }));

  protected readonly rows = signal<HomeworkDto[]>([]);
  protected readonly groups = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<HomeworkDto | null>(null);
  protected readonly sheetVisible = signal(false);
  protected readonly sheetTitle = signal('');
  protected readonly sheet = signal<SubmissionRowDto[]>([]);
  protected readonly maxGrade = signal<number | null>(null);
  protected readonly canManage = signal(this.auth.hasPermission('Homework.Manage'));

  protected groupId: string | null = null;
  protected form = this.empty();
  private sheetId: string | null = null;

  ngOnInit(): void {
    this.lookups.groups().subscribe((o) => this.groups.set(o));
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.service
      .homework({ studyGroupId: this.groupId ?? undefined })
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((rows) => this.rows.set(rows));
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form = this.empty();
    this.form.studyGroupId = this.groupId;
    this.dialogVisible.set(true);
  }

  protected openEdit(row: HomeworkDto): void {
    this.editing.set(row);
    this.form = { studyGroupId: row.studyGroupId, title: row.title, description: row.description ?? '', dueDate: row.dueDate, maxGrade: row.maxGrade ?? null };
    this.dialogVisible.set(true);
  }

  protected save(): void {
    const f = this.form;
    const editing = this.editing();

    if (!f.title.trim() || !f.dueDate || (!editing && !f.studyGroupId)) {
      this.notifications.warn(this.t('homework.incomplete'));
      return;
    }

    this.saving.set(true);
    const request$ = editing
      ? this.service.updateHomework(editing.id, { title: f.title, description: f.description, dueDate: f.dueDate, maxGrade: f.maxGrade })
      : this.service.createHomework({ studyGroupId: f.studyGroupId as string, title: f.title, description: f.description || null, dueDate: f.dueDate, maxGrade: f.maxGrade });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe(() => {
      this.notifications.success(this.t(editing ? 'homework.updated' : 'homework.created'));
      this.dialogVisible.set(false);
      this.load();
    });
  }

  protected remove(row: HomeworkDto): void {
    this.notifications
      .confirm({ message: this.t('homework.deleteMessage', { name: row.title }), header: this.t('common.delete'), acceptButtonStyleClass: 'p-button-danger' })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.removeHomework(row.id).subscribe(() => this.load());
        }
      });
  }

  protected openSubmissions(row: HomeworkDto): void {
    this.sheetId = row.id;
    this.sheetTitle.set(`${row.title} · ${row.groupName ?? ''}`);
    this.maxGrade.set(row.maxGrade ?? null);
    this.sheet.set([]);
    this.sheetVisible.set(true);
    this.service.submissions(row.id).subscribe((rows) => this.sheet.set(rows.map((r) => ({ ...r }))));
  }

  protected saveSheet(): void {
    if (!this.sheetId) {
      return;
    }

    const submissions = this.sheet()
      .filter((r) => r.status !== 'Pending' || (r.grade !== null && r.grade !== undefined) || !!r.feedback)
      .map((r) => ({
        studentProfileId: r.studentProfileId,
        status: r.grade !== null && r.grade !== undefined && r.status !== 'Missing' ? 'Graded' as const : r.status,
        grade: r.grade ?? null,
        feedback: r.feedback || null
      }));

    this.saving.set(true);
    this.service
      .saveSubmissions(this.sheetId, submissions)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('homework.submissionsSaved'));
        this.sheetVisible.set(false);
        this.load();
      });
  }

  private empty() {
    return { studyGroupId: null as string | null, title: '', description: '', dueDate: todayIso(7), maxGrade: null as number | null };
  }

  protected downloadAnswer(file: SubmissionFileDto): void {
    this.files.download(file.id).subscribe((blob) => saveBlob(blob, file.fileName));
  }
}

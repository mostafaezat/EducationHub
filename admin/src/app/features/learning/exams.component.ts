import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import { ExamDto, ExamResultRowDto } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { LearningService } from '../../core/services/learning.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { Option, todayIso } from '../shared/ops-utils';

/** Exams per group and their results sheet (Mission Phase 6). */
@Component({
  selector: 'app-exams',
  imports: [CommonModule, FormsModule, ButtonModule, CheckboxModule, DialogModule, InputNumberModule, InputTextModule, SelectModule, TableModule],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('exams.title') }}</h1>
        <div class="subtitle">{{ t('exams.subtitle') }}</div>
      </div>
      @if (canManage()) {
        <p-button [label]="t('exams.new')" icon="pi pi-plus" (onClick)="openCreate()" />
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
            <th>{{ t('exams.titleField') }}</th>
            <th>{{ t('sessions.group') }}</th>
            <th>{{ t('exams.date') }}</th>
            <th>{{ t('exams.maxScore') }}</th>
            <th>{{ t('exams.recorded') }}</th>
            <th>{{ t('exams.average') }}</th>
            <th style="width:10rem">{{ t('common.actions') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td><strong>{{ row.title }}</strong><div class="muted">{{ row.notes }}</div></td>
            <td>{{ row.groupName }}</td>
            <td>{{ row.examDate }}</td>
            <td>{{ row.maxScore }}</td>
            <td>{{ row.resultsRecorded }}</td>
            <td>{{ row.averageScore ?? '—' }}</td>
            <td>
              <p-button icon="pi pi-table" [text]="true" [title]="t('exams.results')" (onClick)="openResults(row)" />
              @if (canManage()) {
                <p-button icon="pi pi-pencil" [text]="true" (onClick)="openEdit(row)" />
                <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="remove(row)" />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="7"><div class="state-panel"><i class="pi pi-file-edit"></i>{{ t('exams.empty') }}</div></td></tr>
        </ng-template>
      </p-table>
    </div>

    <p-dialog [visible]="dialogVisible()" (visibleChange)="dialogVisible.set($event)" [header]="editing() ? t('exams.edit') : t('exams.new')" [modal]="true" [style]="{ width: '500px' }">
      <div class="form-grid">
        @if (!editing()) {
          <div class="field">
            <label>{{ t('sessions.group') }} *</label>
            <p-select [options]="groups()" [(ngModel)]="form.studyGroupId" optionLabel="label" optionValue="value" [filter]="true" [placeholder]="t('common.selectOption')" appendTo="body" />
          </div>
        }
        <div class="field"><label>{{ t('exams.titleField') }} *</label><input pInputText [(ngModel)]="form.title" /></div>
        <div class="field"><label>{{ t('exams.date') }} *</label><input pInputText type="date" [(ngModel)]="form.examDate" /></div>
        <div class="field"><label>{{ t('exams.maxScore') }} *</label><p-inputNumber [(ngModel)]="form.maxScore" [min]="1" [maxFractionDigits]="2" /></div>
        <div class="field"><label>{{ t('groups.notes') }}</label><input pInputText [(ngModel)]="form.notes" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="t('common.cancel')" severity="secondary" [text]="true" (onClick)="dialogVisible.set(false)" />
        <p-button [label]="t('common.save')" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />
      </ng-template>
    </p-dialog>

    <p-dialog [visible]="sheetVisible()" (visibleChange)="sheetVisible.set($event)" [header]="sheetTitle()" [modal]="true" [style]="{ width: '760px' }">
      <p-table [value]="sheet()" styleClass="p-datatable-sm" [scrollable]="true" scrollHeight="440px">
        <ng-template pTemplate="header">
          <tr>
            <th>{{ t('groups.student') }}</th>
            <th style="width:9rem">{{ t('exams.score') }} / {{ sheetMax() }}</th>
            <th style="width:6rem">{{ t('exams.absent') }}</th>
            <th>{{ t('groups.notes') }}</th>
          </tr>
        </ng-template>
        <ng-template pTemplate="body" let-row>
          <tr>
            <td>{{ row.studentName }}</td>
            <td><p-inputNumber [(ngModel)]="row.score" [min]="0" [max]="sheetMax()" [maxFractionDigits]="2" [disabled]="!canManage() || row.isAbsent" inputStyleClass="w-score" /></td>
            <td><p-checkbox [(ngModel)]="row.isAbsent" [binary]="true" [disabled]="!canManage()" /></td>
            <td><input pInputText [(ngModel)]="row.notes" [disabled]="!canManage()" style="width:100%" /></td>
          </tr>
        </ng-template>
        <ng-template pTemplate="emptymessage">
          <tr><td colspan="4"><div class="muted">{{ t('groups.noStudents') }}</div></td></tr>
        </ng-template>
      </p-table>
      @if (canManage()) {
        <p class="muted">{{ t('exams.notifyHint') }}</p>
      }
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
      :host ::ng-deep .w-score { width: 6rem; }
    `
  ]
})
export class ExamsComponent implements OnInit {
  private readonly service = inject(LearningService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly t = inject(TranslateService).t;

  protected readonly rows = signal<ExamDto[]>([]);
  protected readonly groups = signal<Option[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly dialogVisible = signal(false);
  protected readonly editing = signal<ExamDto | null>(null);
  protected readonly sheetVisible = signal(false);
  protected readonly sheetTitle = signal('');
  protected readonly sheetMax = signal(100);
  protected readonly sheet = signal<ExamResultRowDto[]>([]);
  protected readonly canManage = signal(this.auth.hasPermission('Exams.Manage'));

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
      .exams(this.groupId ?? undefined)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe((rows) => this.rows.set(rows));
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.form = this.empty();
    this.form.studyGroupId = this.groupId;
    this.dialogVisible.set(true);
  }

  protected openEdit(row: ExamDto): void {
    this.editing.set(row);
    this.form = { studyGroupId: row.studyGroupId, title: row.title, examDate: row.examDate, maxScore: row.maxScore, notes: row.notes ?? '' };
    this.dialogVisible.set(true);
  }

  protected save(): void {
    const f = this.form;
    const editing = this.editing();

    if (!f.title.trim() || !f.examDate || !f.maxScore || (!editing && !f.studyGroupId)) {
      this.notifications.warn(this.t('exams.incomplete'));
      return;
    }

    this.saving.set(true);
    const request$ = editing
      ? this.service.updateExam(editing.id, { title: f.title, examDate: f.examDate, maxScore: f.maxScore, notes: f.notes })
      : this.service.createExam({ studyGroupId: f.studyGroupId as string, title: f.title, examDate: f.examDate, maxScore: f.maxScore, notes: f.notes || null });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe(() => {
      this.notifications.success(this.t(editing ? 'exams.updated' : 'exams.created'));
      this.dialogVisible.set(false);
      this.load();
    });
  }

  protected remove(row: ExamDto): void {
    this.notifications
      .confirm({ message: this.t('exams.deleteMessage', { name: row.title }), header: this.t('common.delete'), acceptButtonStyleClass: 'p-button-danger' })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.removeExam(row.id).subscribe(() => this.load());
        }
      });
  }

  protected openResults(row: ExamDto): void {
    this.sheetId = row.id;
    this.sheetTitle.set(`${row.title} · ${row.groupName ?? ''} · ${row.examDate}`);
    this.sheetMax.set(row.maxScore);
    this.sheet.set([]);
    this.sheetVisible.set(true);
    this.service.results(row.id).subscribe((rows) => this.sheet.set(rows.map((r) => ({ ...r }))));
  }

  protected saveSheet(): void {
    if (!this.sheetId) {
      return;
    }

    const results = this.sheet()
      .filter((r) => r.isAbsent || (r.score !== null && r.score !== undefined))
      .map((r) => ({ studentProfileId: r.studentProfileId, score: r.isAbsent ? null : r.score ?? null, isAbsent: r.isAbsent, notes: r.notes || null }));

    if (results.length === 0) {
      this.notifications.warn(this.t('exams.nothingEntered'));
      return;
    }

    this.saving.set(true);
    this.service
      .saveResults(this.sheetId, results)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('exams.resultsSaved'));
        this.sheetVisible.set(false);
        this.load();
      });
  }

  private empty() {
    return { studyGroupId: null as string | null, title: '', examDate: todayIso(7), maxScore: 100, notes: '' };
  }
}

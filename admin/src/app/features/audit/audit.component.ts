import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DatePickerModule } from 'primeng/datepicker';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  AUDIT_ACTIONS,
  AuditAction,
  AuditLogDto,
  AuditSummaryDto,
  LoginHistoryDto
} from '../../core/models';
import { AuditService } from '../../core/services/audit.service';

type TabKey = 'logs' | 'summary' | 'logins';

const AUDIT_ACTION_KEYS: Record<AuditAction, string> = {
  Created: 'audit.action.Created',
  Updated: 'audit.action.Updated',
  Deleted: 'audit.action.Deleted',
  LoggedIn: 'audit.action.LoggedIn',
  LoggedOut: 'audit.action.LoggedOut',
  PasswordChanged: 'audit.action.PasswordChanged',
  PasswordReset: 'audit.action.PasswordReset',
  PermissionChanged: 'audit.action.PermissionChanged',
  RoleAssigned: 'audit.action.RoleAssigned',
  RoleRemoved: 'audit.action.RoleRemoved',
  AccountActivated: 'audit.action.AccountActivated',
  AccountDeactivated: 'audit.action.AccountDeactivated'
};

function toDateOnly(value: Date | null | undefined): string | undefined {
  if (!value) {
    return undefined;
  }

  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

@Component({
  selector: 'app-audit',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DatePickerModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('audit.title') }}</h1>
        <div class="subtitle">{{ t('audit.subtitle') }}</div>
      </div>
    </div>

    <div class="tabs">
      <button class="tab" [class.active]="tab() === 'logs'" (click)="select('logs')">
        <i class="pi pi-list"></i> {{ t('audit.tab.logs') }}
      </button>
      <button class="tab" [class.active]="tab() === 'summary'" (click)="select('summary')">
        <i class="pi pi-chart-bar"></i> {{ t('audit.tab.summary') }}
      </button>
      <button class="tab" [class.active]="tab() === 'logins'" (click)="select('logins')">
        <i class="pi pi-sign-in"></i> {{ t('audit.tab.logins') }}
      </button>
    </div>

    @if (tab() === 'logs') {
      <div class="toolbar">
        <p-select
          [options]="actionOptions()"
          [ngModel]="actionFilter()"
          (ngModelChange)="setActionFilter($event)"
          optionLabel="label"
          optionValue="value"
          [placeholder]="t('audit.anyAction')"
          [showClear]="true"
          [style]="{ minWidth: '12rem' }"
        />
        <input pInputText type="text" [placeholder]="t('audit.entityPlaceholder')" (input)="onEntityFilter($event)" />
        <p-datepicker [ngModel]="fromLogs()" (ngModelChange)="setFromLogs($event)" [placeholder]="t('common.from')" dateFormat="yy-mm-dd" [showIcon]="true" />
        <p-datepicker [ngModel]="toLogs()" (ngModelChange)="setToLogs($event)" [placeholder]="t('common.to')" dateFormat="yy-mm-dd" [showIcon]="true" />
        <div class="spacer"></div>
        <p-button icon="pi pi-refresh" [text]="true" (onClick)="loadLogs()" />
      </div>

      <div class="card">
        <p-table
          [value]="logs()"
          [lazy]="true"
          [paginator]="true"
          [rows]="pageSize()"
          [first]="logsFirst()"
          [totalRecords]="logsTotal()"
          [loading]="logsLoading()"
          (onLazyLoad)="onLogsLazy($event)"
          (onRowSelect)="openDetail($event.data)"
          selectionMode="single"
          dataKey="id"
          styleClass="p-datatable-sm"
        >
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('audit.headers.occurred') }}</th>
              <th>{{ t('audit.headers.action') }}</th>
              <th>{{ t('audit.headers.entity') }}</th>
              <th>{{ t('audit.headers.user') }}</th>
              <th>{{ t('audit.headers.ip') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr [pSelectableRow]="row">
              <td>{{ row.occurredAt | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td><p-tag [value]="actionLabel(row.action)" severity="info" /></td>
              <td>{{ row.entityName }}</td>
              <td>{{ row.userName || '—' }}</td>
              <td>{{ row.ipAddress || '—' }}</td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="5">
                <div class="state-panel"><i class="pi pi-history"></i>{{ t('audit.emptyLogs') }}</div>
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    @if (tab() === 'summary') {
      <div class="toolbar">
        <p-datepicker [ngModel]="fromSummary()" (ngModelChange)="setFromSummary($event)" [placeholder]="t('common.from')" dateFormat="yy-mm-dd" [showIcon]="true" />
        <p-datepicker [ngModel]="toSummary()" (ngModelChange)="setToSummary($event)" [placeholder]="t('common.to')" dateFormat="yy-mm-dd" [showIcon]="true" />
        <div class="spacer"></div>
        <p-button [label]="t('audit.loadSummaryButton')" icon="pi pi-search" [loading]="summaryLoading()" (onClick)="loadSummary()" />
      </div>

      @if (summaryError()) {
        <div class="card error-note">
          <i class="pi pi-exclamation-triangle"></i>
          <span>{{ summaryError() }}</span>
        </div>
      }

      <div class="card">
        <p-table [value]="summary()" [paginator]="summary().length > 10" [rows]="10" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('audit.headers.action') }}</th>
              <th>{{ t('audit.headers.user') }}</th>
              <th>{{ t('audit.headers.count') }}</th>
              <th>{{ t('audit.headers.lastOccurred') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td><p-tag [value]="actionLabel(row.action)" severity="info" /></td>
              <td>{{ row.userName || '—' }}</td>
              <td>{{ row.count }}</td>
              <td>{{ row.lastOccurredAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="4">
                <div class="state-panel"><i class="pi pi-chart-bar"></i>{{ t('audit.emptySummary') }}</div>
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    @if (tab() === 'logins') {
      <div class="toolbar">
        <p-select
          [options]="resultOptions()"
          [ngModel]="loginResult()"
          (ngModelChange)="setLoginResult($event)"
          optionLabel="label"
          optionValue="value"
          [placeholder]="t('audit.result.any')"
          [style]="{ minWidth: '12rem' }"
        />
        <div class="spacer"></div>
        <p-button icon="pi pi-refresh" [text]="true" (onClick)="loadLogins()" />
      </div>

      <div class="card">
        <p-table
          [value]="logins()"
          [lazy]="true"
          [paginator]="true"
          [rows]="pageSize()"
          [first]="loginsFirst()"
          [totalRecords]="loginsTotal()"
          [loading]="loginsLoading()"
          (onLazyLoad)="onLoginsLazy($event)"
          styleClass="p-datatable-sm"
        >
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('audit.headers.attempted') }}</th>
              <th>{{ t('audit.headers.user') }}</th>
              <th>{{ t('audit.headers.result') }}</th>
              <th>{{ t('audit.headers.reason') }}</th>
              <th>{{ t('audit.headers.ip') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.attemptedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td>{{ row.userName || row.userId }}</td>
              <td>
                <p-tag
                  [value]="row.succeeded ? t('audit.succeeded') : t('audit.failed')"
                  [severity]="row.succeeded ? 'success' : 'danger'"
                />
              </td>
              <td>{{ row.failureReason || '—' }}</td>
              <td>{{ row.ipAddress || '—' }}</td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="5">
                <div class="state-panel"><i class="pi pi-sign-in"></i>{{ t('audit.emptyLogins') }}</div>
              </td>
            </tr>
          </ng-template>
        </p-table>
      </div>
    }

    <p-dialog [header]="t('audit.detailTitle')" [(visible)]="detailVisible" [modal]="true" [style]="{ width: '640px' }">
      @if (detail()) {
        <div class="detail">
          <div class="detail-row"><span>{{ t('audit.headers.action') }}</span><b>{{ actionLabel(detail()!.action) }}</b></div>
          <div class="detail-row"><span>{{ t('audit.headers.entity') }}</span><b>{{ detail()!.entityName }}</b></div>
          <div class="detail-row"><span>{{ t('audit.entityId') }}</span><b>{{ detail()!.entityId || '—' }}</b></div>
          <div class="detail-row"><span>{{ t('audit.headers.user') }}</span><b>{{ detail()!.userName || '—' }}</b></div>
          <div class="detail-row"><span>{{ t('audit.headers.occurred') }}</span><b>{{ detail()!.occurredAt | date: 'yyyy-MM-dd HH:mm:ss' }}</b></div>
          <div class="detail-row"><span>{{ t('audit.headers.ip') }}</span><b>{{ detail()!.ipAddress || '—' }}</b></div>
          @if (detail()!.description) {
            <div class="detail-row"><span>{{ t('common.description') }}</span><b>{{ detail()!.description }}</b></div>
          }
          @if (detail()!.oldValue) {
            <div class="json-block"><span>{{ t('audit.oldValue') }}</span><pre>{{ pretty(detail()!.oldValue) }}</pre></div>
          }
          @if (detail()!.newValue) {
            <div class="json-block"><span>{{ t('audit.newValue') }}</span><pre>{{ pretty(detail()!.newValue) }}</pre></div>
          }
        </div>
      }
    </p-dialog>
  `,
  styles: [
    `
      .tabs {
        display: flex;
        gap: 0.25rem;
        margin-bottom: 1rem;
      }
      .tab {
        border: 1px solid #e2e8f0;
        background: #fff;
        color: #475569;
        padding: 0.5rem 0.9rem;
        border-radius: 6px;
        cursor: pointer;
        font-size: 0.9rem;
      }
      .tab.active {
        background: #2563eb;
        border-color: #2563eb;
        color: #fff;
      }
      .tab i {
        margin-right: 0.35rem;
      }
      .error-note {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        color: #b91c1c;
        font-size: 0.9rem;
      }
      .detail {
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
      }
      .detail-row {
        display: flex;
        justify-content: space-between;
        gap: 1rem;
        font-size: 0.9rem;
        border-bottom: 1px solid #f1f5f9;
        padding-bottom: 0.35rem;
      }
      .detail-row span {
        color: #64748b;
      }
      .json-block {
        display: flex;
        flex-direction: column;
        gap: 0.25rem;
        font-size: 0.85rem;
      }
      .json-block span {
        color: #64748b;
      }
      .json-block pre {
        background: #0f172a;
        color: #e2e8f0;
        padding: 0.75rem;
        border-radius: 6px;
        overflow: auto;
        max-height: 220px;
        margin: 0;
        font-size: 0.78rem;
      }
    `
  ]
})
export class AuditComponent implements OnInit {
  private readonly service = inject(AuditService);
  private readonly entityFilter$ = new Subject<string>();

  protected readonly t = inject(TranslateService).t;

  protected readonly actionOptions = computed(() =>
    AUDIT_ACTIONS.map((value) => ({ label: this.t(AUDIT_ACTION_KEYS[value]), value }))
  );
  protected readonly resultOptions = computed(() => [
    { label: this.t('audit.result.any'), value: 'any' },
    { label: this.t('audit.result.succeeded'), value: 'succeeded' },
    { label: this.t('audit.result.failed'), value: 'failed' }
  ]);

  protected readonly tab = signal<TabKey>('logs');

  protected readonly logs = signal<AuditLogDto[]>([]);
  protected readonly logsTotal = signal(0);
  protected readonly logsLoading = signal(false);
  protected readonly logsFirst = signal(0);
  protected readonly actionFilter = signal<AuditAction | null>(null);
  protected readonly entityFilter = signal('');
  protected readonly fromLogs = signal<Date | null>(null);
  protected readonly toLogs = signal<Date | null>(null);

  protected readonly summary = signal<AuditSummaryDto[]>([]);
  protected readonly summaryLoading = signal(false);
  protected readonly summaryError = signal<string | null>(null);
  protected readonly fromSummary = signal<Date | null>(null);
  protected readonly toSummary = signal<Date | null>(null);

  protected readonly logins = signal<LoginHistoryDto[]>([]);
  protected readonly loginsTotal = signal(0);
  protected readonly loginsLoading = signal(false);
  protected readonly loginsFirst = signal(0);
  protected readonly loginResult = signal<'any' | 'succeeded' | 'failed'>('any');

  protected readonly pageSize = signal(15);

  protected detailVisible = false;
  protected readonly detail = signal<AuditLogDto | null>(null);

  ngOnInit(): void {
    this.entityFilter$.pipe(debounceTime(300)).subscribe((value) => {
      this.entityFilter.set(value);
      this.logsFirst.set(0);
      this.loadLogs();
    });

    this.loadLogs();
  }

  protected select(tab: TabKey): void {
    this.tab.set(tab);

    if (tab === 'summary' && this.summary().length === 0) {
      this.loadSummary();
    }

    if (tab === 'logins' && this.logins().length === 0) {
      this.loadLogins();
    }
  }

  protected actionLabel(action: AuditAction): string {
    return this.t(AUDIT_ACTION_KEYS[action]);
  }

  protected setActionFilter(value: AuditAction | null): void {
    this.actionFilter.set(value);
    this.logsFirst.set(0);
    this.loadLogs();
  }

  protected onEntityFilter(event: Event): void {
    this.entityFilter$.next((event.target as HTMLInputElement).value);
  }

  protected setFromLogs(value: Date | null): void {
    this.fromLogs.set(value);
    this.loadLogs();
  }

  protected setToLogs(value: Date | null): void {
    this.toLogs.set(value);
    this.loadLogs();
  }

  protected onLogsLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize();
    this.pageSize.set(rows);
    this.logsFirst.set(event.first ?? 0);
    this.loadLogs();
  }

  protected loadLogs(): void {
    this.logsLoading.set(true);
    this.service
      .logs({
        pageNumber: Math.floor(this.logsFirst() / this.pageSize()) + 1,
        pageSize: this.pageSize(),
        action: this.actionFilter() ?? undefined,
        entityName: this.entityFilter() || undefined,
        from: toDateOnly(this.fromLogs()),
        to: toDateOnly(this.toLogs())
      })
      .pipe(finalize(() => this.logsLoading.set(false)))
      .subscribe((page) => {
        this.logs.set(page.items);
        this.logsTotal.set(page.totalItems);
      });
  }

  protected openDetail(row: AuditLogDto | AuditLogDto[] | undefined): void {
    const entry = Array.isArray(row) ? row[0] : row;

    if (!entry) {
      return;
    }

    this.detail.set(entry);
    this.detailVisible = true;
  }

  protected pretty(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }

  protected setFromSummary(value: Date | null): void {
    this.fromSummary.set(value);
  }

  protected setToSummary(value: Date | null): void {
    this.toSummary.set(value);
  }

  protected loadSummary(): void {
    this.summaryLoading.set(true);
    this.summaryError.set(null);
    this.service
      .summary({ from: toDateOnly(this.fromSummary()), to: toDateOnly(this.toSummary()), top: 20 })
      .pipe(finalize(() => this.summaryLoading.set(false)))
      .subscribe({
        next: (rows) => this.summary.set(rows),
        error: (problem: { error?: { detail?: string }; message?: string }) => {
          this.summary.set([]);
          this.summaryError.set(
            problem?.error?.detail ?? problem?.message ?? this.t('audit.loadSummaryError')
          );
        }
      });
  }

  protected setLoginResult(value: 'any' | 'succeeded' | 'failed'): void {
    this.loginResult.set(value);
    this.loginsFirst.set(0);
    this.loadLogins();
  }

  protected onLoginsLazy(event: TableLazyLoadEvent): void {
    const rows = event.rows ?? this.pageSize();
    this.pageSize.set(rows);
    this.loginsFirst.set(event.first ?? 0);
    this.loadLogins();
  }

  protected loadLogins(): void {
    const result = this.loginResult();
    this.loginsLoading.set(true);
    this.service
      .logins({
        pageNumber: Math.floor(this.loginsFirst() / this.pageSize()) + 1,
        pageSize: this.pageSize(),
        succeededOnly: result === 'succeeded' ? true : undefined,
        failedOnly: result === 'failed' ? true : undefined
      })
      .pipe(finalize(() => this.loginsLoading.set(false)))
      .subscribe((page) => {
        this.logins.set(page.items);
        this.loginsTotal.set(page.totalItems);
      });
  }
}

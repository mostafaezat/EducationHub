import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { Subject, debounceTime, finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  ChargeDto,
  ChargeStatus,
  FeeFrequency,
  FeeRuleDto,
  PAYMENT_METHODS,
  PaymentDto,
  PaymentMethod,
  StudentBalanceDto
} from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FinanceService } from '../../core/services/finance.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { currentPeriod, money, Option, todayIso } from '../shared/ops-utils';

type FeesTab = 'charges' | 'payments' | 'rules';

/** Fee rules, student charges, payments and balances (Mission Phase 4). */
@Component({
  selector: 'app-fees',
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DialogModule,
    InputNumberModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    ToggleSwitchModule
  ],
  template: `
    <div class="page-header">
      <div>
        <h1>{{ t('fees.title') }}</h1>
        <div class="subtitle">{{ t('fees.subtitle') }}</div>
      </div>
      <div class="header-actions">
        @if (canPay()) {
          <p-button [label]="t('fees.recordPayment')" icon="pi pi-wallet" (onClick)="openPayment()" />
        }
      </div>
    </div>

    <div class="tabs">
      <button type="button" [class.on]="tab() === 'charges'" (click)="setTab('charges')">{{ t('fees.tabs.charges') }}</button>
      @if (canViewPayments()) {
        <button type="button" [class.on]="tab() === 'payments'" (click)="setTab('payments')">{{ t('fees.tabs.payments') }}</button>
      }
      <button type="button" [class.on]="tab() === 'rules'" (click)="setTab('rules')">{{ t('fees.tabs.rules') }}</button>
    </div>

    @if (tab() !== 'rules') {
      <div class="toolbar">
        <p-select
          [options]="students()"
          [(ngModel)]="studentId"
          optionLabel="label"
          optionValue="value"
          [filter]="true"
          [showClear]="true"
          (onFilter)="studentSearch$.next($event.filter)"
          (onChange)="reload()"
          [placeholder]="t('fees.allStudents')"
          style="min-width:18rem"
        />
        @if (tab() === 'charges') {
          <p-select [options]="chargeStatusOptions" [(ngModel)]="chargeStatus" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('common.status')" (onChange)="reload()" />
          <input pInputText type="month" [(ngModel)]="periodFilter" (change)="reload()" [title]="t('fees.period')" />
        }
        <div class="spacer"></div>
        @if (tab() === 'charges' && canManage()) {
          <p-button [label]="t('fees.generateMonthly')" icon="pi pi-cog" [outlined]="true" (onClick)="generateVisible.set(true)" />
          <p-button [label]="t('fees.newCharge')" icon="pi pi-plus" [outlined]="true" (onClick)="openCharge()" />
        }
        <p-button icon="pi pi-refresh" [text]="true" (onClick)="reload()" />
      </div>

      @if (balance(); as b) {
        <div class="balance-strip">
          <div><span>{{ t('fees.totalCharged') }}</span><strong>{{ money(b.totalCharged) }}</strong></div>
          <div><span>{{ t('fees.totalPaid') }}</span><strong>{{ money(b.totalPaid) }}</strong></div>
          <div class="due"><span>{{ t('fees.outstanding') }}</span><strong>{{ money(b.outstanding) }}</strong></div>
          <div><span>{{ t('fees.credit') }}</span><strong>{{ money(b.credit) }}</strong></div>
        </div>
      }
    }

    @if (tab() === 'charges') {
      <div class="card">
        <p-table [value]="charges()" [lazy]="true" [paginator]="true" [rows]="pageSize()" [first]="first()" [totalRecords]="total()" [loading]="loading()" (onLazyLoad)="onLazyLoad($event)" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('fees.student') }}</th>
              <th>{{ t('common.description') }}</th>
              <th>{{ t('fees.dueDate') }}</th>
              <th class="num">{{ t('fees.amount') }}</th>
              <th class="num">{{ t('fees.paid') }}</th>
              <th class="num">{{ t('fees.outstanding') }}</th>
              <th>{{ t('common.status') }}</th>
              <th style="width:5rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.studentName }}</td>
              <td>{{ row.description }}<div class="muted">{{ row.studyGroupName }}</div></td>
              <td>{{ row.dueDate }}</td>
              <td class="num">{{ money(row.amount) }}</td>
              <td class="num">{{ money(row.amountPaid) }}</td>
              <td class="num"><strong>{{ money(row.outstanding) }}</strong></td>
              <td><p-tag [value]="t('chargeStatus.' + row.status)" [severity]="chargeSeverity(row.status)" /></td>
              <td>
                @if (canManage() && row.status === 'Open') {
                  <p-button icon="pi pi-ban" severity="danger" [text]="true" [title]="t('fees.cancelCharge')" (onClick)="cancelCharge(row)" />
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="8"><div class="state-panel"><i class="pi pi-receipt"></i>{{ t('fees.noCharges') }}</div></td></tr>
          </ng-template>
        </p-table>
      </div>
    }

    @if (tab() === 'payments') {
      <div class="toolbar">
        <label class="check"><p-toggleswitch [(ngModel)]="includeVoided" (onChange)="reload()" /> {{ t('fees.includeVoided') }}</label>
      </div>
      <div class="card">
        <p-table [value]="payments()" [lazy]="true" [paginator]="true" [rows]="pageSize()" [first]="first()" [totalRecords]="total()" [loading]="loading()" (onLazyLoad)="onLazyLoad($event)" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('fees.paidOn') }}</th>
              <th>{{ t('fees.student') }}</th>
              <th>{{ t('fees.method') }}</th>
              <th class="num">{{ t('fees.amount') }}</th>
              <th>{{ t('fees.appliedTo') }}</th>
              <th>{{ t('fees.reference') }}</th>
              <th style="width:5rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr [class.voided]="row.isVoided">
              <td>{{ row.paidOn }}</td>
              <td>{{ row.studentName }}</td>
              <td>{{ t('paymentMethod.' + row.method) }}</td>
              <td class="num"><strong>{{ money(row.amount) }}</strong></td>
              <td>
                @for (a of row.allocations; track a.chargeId) {
                  <div class="muted">{{ a.chargeDescription }}: {{ money(a.amount) }}</div>
                }
                @if (row.unallocated > 0) {
                  <div class="muted">{{ t('fees.credit') }}: {{ money(row.unallocated) }}</div>
                }
              </td>
              <td>{{ row.reference }}@if (row.isVoided) { <p-tag [value]="t('fees.voided')" severity="danger" /> }</td>
              <td>
                @if (canVoid() && !row.isVoided) {
                  <p-button icon="pi pi-undo" severity="danger" [text]="true" [title]="t('fees.void')" (onClick)="voidPayment(row)" />
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="7"><div class="state-panel"><i class="pi pi-wallet"></i>{{ t('fees.noPayments') }}</div></td></tr>
          </ng-template>
        </p-table>
      </div>
    }

    @if (tab() === 'rules') {
      <div class="toolbar">
        <div class="spacer"></div>
        @if (canManage()) {
          <p-button [label]="t('fees.newRule')" icon="pi pi-plus" [outlined]="true" (onClick)="openRule()" />
        }
      </div>
      <div class="card">
        <p-table [value]="rules()" [loading]="loading()" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ common('name') }}</th>
              <th>{{ t('fees.frequency') }}</th>
              <th>{{ t('fees.appliesTo') }}</th>
              <th class="num">{{ t('fees.amount') }}</th>
              <th>{{ common('status') }}</th>
              <th style="width:8rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.name }}</td>
              <td>{{ t('feeFrequency.' + row.frequency) }}</td>
              <td>{{ row.studyGroupName ?? row.gradeLevelName ?? t('fees.tenantDefault') }}</td>
              <td class="num">{{ money(row.amount) }}</td>
              <td><p-tag [value]="row.isActive ? common('active') : common('inactive')" [severity]="row.isActive ? 'success' : 'secondary'" /></td>
              <td>
                @if (canManage()) {
                  <p-button icon="pi pi-pencil" [text]="true" (onClick)="openRule(row)" />
                  <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="removeRule(row)" />
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="6"><div class="state-panel"><i class="pi pi-tags"></i>{{ t('fees.noRules') }}</div></td></tr>
          </ng-template>
        </p-table>
        <p class="muted hint">{{ t('fees.rulesHint') }}</p>
      </div>
    }

    <!-- Payment -->
    <p-dialog [visible]="paymentVisible()" (visibleChange)="paymentVisible.set($event)" [header]="t('fees.recordPayment')" [modal]="true" [style]="{ width: '560px' }">
      <div class="form-grid">
        <div class="field">
          <label>{{ t('fees.student') }} *</label>
          <p-select [options]="students()" [(ngModel)]="pay.studentProfileId" optionLabel="label" optionValue="value" [filter]="true" (onFilter)="studentSearch$.next($event.filter)" (onChange)="loadPayBalance()" [placeholder]="t('common.selectOption')" appendTo="body" />
          @if (payBalance(); as b) {
            <small class="muted">{{ t('fees.outstanding') }}: {{ money(b.outstanding) }} · {{ t('fees.openCharges') }}: {{ b.openCharges }}</small>
          }
        </div>
        <div class="field">
          <label>{{ t('fees.amount') }} *</label>
          <p-inputNumber [(ngModel)]="pay.amount" [min]="0" [minFractionDigits]="0" [maxFractionDigits]="2" />
        </div>
        <div class="field">
          <label>{{ t('fees.paidOn') }}</label>
          <input pInputText type="date" [(ngModel)]="pay.paidOn" />
        </div>
        <div class="field">
          <label>{{ t('fees.method') }}</label>
          <p-select [options]="methodOptions" [(ngModel)]="pay.method" optionLabel="label" optionValue="value" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('finance.cashAccount') }}</label>
          <p-select [options]="cashAccounts()" [(ngModel)]="pay.cashAccountId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('finance.defaultAccount')" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('fees.reference') }}</label>
          <input pInputText [(ngModel)]="pay.reference" />
        </div>
      </div>
      <p class="muted">{{ t('fees.fifoHint') }}</p>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="paymentVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="savePayment()" />
      </ng-template>
    </p-dialog>

    <!-- Charge -->
    <p-dialog [visible]="chargeVisible()" (visibleChange)="chargeVisible.set($event)" [header]="t('fees.newCharge')" [modal]="true" [style]="{ width: '520px' }">
      <div class="form-grid">
        <div class="field">
          <label>{{ t('fees.student') }} *</label>
          <p-select [options]="students()" [(ngModel)]="charge.studentProfileId" optionLabel="label" optionValue="value" [filter]="true" (onFilter)="studentSearch$.next($event.filter)" [placeholder]="common('selectOption')" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ common('description') }} *</label>
          <input pInputText [(ngModel)]="charge.description" />
        </div>
        <div class="field">
          <label>{{ t('fees.amount') }} *</label>
          <p-inputNumber [(ngModel)]="charge.amount" [min]="0" [maxFractionDigits]="2" />
        </div>
        <div class="field">
          <label>{{ t('fees.dueDate') }} *</label>
          <input pInputText type="date" [(ngModel)]="charge.dueDate" />
        </div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="chargeVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveCharge()" />
      </ng-template>
    </p-dialog>

    <!-- Generate -->
    <p-dialog [visible]="generateVisible()" (visibleChange)="generateVisible.set($event)" [header]="t('fees.generateMonthly')" [modal]="true" [style]="{ width: '460px' }">
      <p class="muted">{{ t('fees.generateHint') }}</p>
      <div class="form-grid">
        <div class="field">
          <label>{{ t('fees.period') }}</label>
          <input pInputText type="month" [(ngModel)]="genPeriod" />
        </div>
        <div class="field">
          <label>{{ t('sessions.group') }}</label>
          <p-select [options]="groups()" [(ngModel)]="genGroupId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('sessions.allGroups')" appendTo="body" />
        </div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="generateVisible.set(false)" />
        <p-button [label]="t('fees.generate')" icon="pi pi-check" [loading]="saving()" (onClick)="generate()" />
      </ng-template>
    </p-dialog>

    <!-- Rule -->
    <p-dialog [visible]="ruleVisible()" (visibleChange)="ruleVisible.set($event)" [header]="editingRule() ? t('fees.editRule') : t('fees.newRule')" [modal]="true" [style]="{ width: '520px' }">
      <div class="form-grid">
        <div class="field">
          <label>{{ common('name') }} *</label>
          <input pInputText [(ngModel)]="rule.name" />
        </div>
        <div class="field">
          <label>{{ t('fees.amount') }} *</label>
          <p-inputNumber [(ngModel)]="rule.amount" [min]="0" [maxFractionDigits]="2" />
        </div>
        @if (!editingRule()) {
          <div class="field">
            <label>{{ t('fees.frequency') }}</label>
            <p-select [options]="frequencyOptions" [(ngModel)]="rule.frequency" optionLabel="label" optionValue="value" appendTo="body" />
          </div>
          <div class="field">
            <label>{{ t('sessions.group') }}</label>
            <p-select [options]="groups()" [(ngModel)]="rule.studyGroupId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('fees.anyGroup')" appendTo="body" />
          </div>
          <div class="field">
            <label>{{ t('groups.grade') }}</label>
            <p-select [options]="grades()" [(ngModel)]="rule.gradeLevelId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('fees.anyGrade')" appendTo="body" />
          </div>
        } @else {
          <div class="field">
            <label>{{ common('active') }}</label>
            <p-toggleswitch [(ngModel)]="rule.isActive" />
          </div>
        }
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="ruleVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveRule()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted { color: #94a3b8; font-size: 0.8rem; }
      .hint { margin-top: 0.75rem; }
      .num { text-align: end; }
      .voided td { text-decoration: line-through; color: #94a3b8; }
      .check { display: inline-flex; align-items: center; gap: 0.5rem; font-size: 0.9rem; }
      .header-actions { display: flex; gap: 0.5rem; }
      .balance-strip { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
      .balance-strip > div { background: #fff; border: 1px solid #e3e8f0; border-radius: 10px; padding: 0.6rem 0.9rem; display: flex; flex-direction: column; }
      .balance-strip span { font-size: 0.78rem; color: #64748b; }
      .balance-strip strong { font-size: 1.15rem; }
      .balance-strip .due strong { color: #b91c1c; }
    `
  ]
})
export class FeesComponent implements OnInit {
  private readonly service = inject(FinanceService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);

  protected readonly t = inject(TranslateService).t;
  protected readonly common = (key: string) => this.t('common.' + key);
  protected readonly money = money;
  protected readonly studentSearch$ = new Subject<string>();

  protected readonly tab = signal<FeesTab>('charges');
  protected readonly charges = signal<ChargeDto[]>([]);
  protected readonly payments = signal<PaymentDto[]>([]);
  protected readonly rules = signal<FeeRuleDto[]>([]);
  protected readonly balance = signal<StudentBalanceDto | null>(null);
  protected readonly payBalance = signal<StudentBalanceDto | null>(null);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly first = signal(0);

  protected readonly students = signal<Option[]>([]);
  protected readonly groups = signal<Option[]>([]);
  protected readonly grades = signal<Option[]>([]);
  protected readonly cashAccounts = signal<Option[]>([]);

  protected readonly paymentVisible = signal(false);
  protected readonly chargeVisible = signal(false);
  protected readonly generateVisible = signal(false);
  protected readonly ruleVisible = signal(false);
  protected readonly editingRule = signal<FeeRuleDto | null>(null);

  protected readonly canManage = signal(this.auth.hasPermission('Fees.Manage'));
  protected readonly canViewPayments = signal(this.auth.hasPermission('Payments.View'));
  protected readonly canPay = signal(this.auth.hasPermission('Payments.Create'));
  protected readonly canVoid = signal(this.auth.hasPermission('Payments.Void'));

  protected readonly methodOptions = PAYMENT_METHODS.map((m) => ({ label: this.t('paymentMethod.' + m), value: m }));
  protected readonly frequencyOptions = (['Monthly', 'OneTime'] as FeeFrequency[]).map((f) => ({ label: this.t('feeFrequency.' + f), value: f }));
  protected readonly chargeStatusOptions = (['Open', 'PartiallyPaid', 'Paid', 'Cancelled'] as ChargeStatus[]).map((s) => ({
    label: this.t('chargeStatus.' + s),
    value: s
  }));

  protected studentId: string | null = null;
  protected chargeStatus: ChargeStatus | null = null;
  protected periodFilter = '';
  protected includeVoided = false;
  protected genPeriod = currentPeriod();
  protected genGroupId: string | null = null;

  protected pay = this.emptyPayment();
  protected charge = { studentProfileId: null as string | null, description: '', amount: 0, dueDate: todayIso() };
  protected rule = {
    name: '',
    amount: 0,
    frequency: 'Monthly' as FeeFrequency,
    studyGroupId: null as string | null,
    gradeLevelId: null as string | null,
    isActive: true
  };

  ngOnInit(): void {
    this.studentSearch$
      .pipe(debounceTime(300))
      .subscribe((value) => this.lookups.students(value).subscribe((o) => this.students.set(o)));
    this.lookups.students().subscribe((o) => this.students.set(o));
    this.lookups.groups().subscribe((o) => this.groups.set(o));
    this.lookups.grades().subscribe((o) => this.grades.set(o));
    this.lookups.cashAccounts().subscribe((o) => this.cashAccounts.set(o));
    this.load();
  }

  protected chargeSeverity(status: ChargeStatus): 'success' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'Paid':
        return 'success';
      case 'PartiallyPaid':
        return 'warn';
      case 'Open':
        return 'danger';
      default:
        return 'secondary';
    }
  }

  protected setTab(tab: FeesTab): void {
    this.tab.set(tab);
    this.reload();
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
    const studentProfileId = this.studentId ?? undefined;

    if (this.studentId) {
      this.service.balance(this.studentId).subscribe((b) => this.balance.set(b));
    } else {
      this.balance.set(null);
    }

    switch (this.tab()) {
      case 'charges':
        this.service
          .charges({
            pageNumber: this.pageNumber(),
            pageSize: this.pageSize(),
            studentProfileId,
            status: this.chargeStatus ?? undefined,
            periodKey: this.periodFilter || undefined
          })
          .pipe(finalize(() => this.loading.set(false)))
          .subscribe((page) => {
            this.charges.set(page.items);
            this.total.set(page.totalItems);
          });
        break;
      case 'payments':
        this.service
          .payments({ pageNumber: this.pageNumber(), pageSize: this.pageSize(), studentProfileId, includeVoided: this.includeVoided })
          .pipe(finalize(() => this.loading.set(false)))
          .subscribe((page) => {
            this.payments.set(page.items);
            this.total.set(page.totalItems);
          });
        break;
      default:
        this.service
          .feeRules()
          .pipe(finalize(() => this.loading.set(false)))
          .subscribe((rows) => this.rules.set(rows));
    }
  }

  // Payments
  protected openPayment(): void {
    this.pay = this.emptyPayment();
    this.pay.studentProfileId = this.studentId;
    this.payBalance.set(null);
    this.loadPayBalance();
    this.paymentVisible.set(true);
  }

  protected loadPayBalance(): void {
    if (this.pay.studentProfileId) {
      this.service.balance(this.pay.studentProfileId).subscribe((b) => {
        this.payBalance.set(b);

        if (!this.pay.amount && b.outstanding > 0) {
          this.pay.amount = b.outstanding;
        }
      });
    } else {
      this.payBalance.set(null);
    }
  }

  protected savePayment(): void {
    if (!this.pay.studentProfileId || !this.pay.amount || this.pay.amount <= 0) {
      this.notifications.warn(this.t('fees.paymentIncomplete'));
      return;
    }

    this.saving.set(true);
    this.service
      .createPayment({
        studentProfileId: this.pay.studentProfileId,
        amount: this.pay.amount,
        paidOn: this.pay.paidOn || null,
        method: this.pay.method,
        cashAccountId: this.pay.cashAccountId,
        reference: this.pay.reference || null
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('fees.paymentRecorded'));
        this.paymentVisible.set(false);
        this.load();
      });
  }

  protected voidPayment(row: PaymentDto): void {
    this.notifications
      .confirm({
        message: this.t('fees.voidMessage', { amount: money(row.amount) }),
        header: this.t('fees.void'),
        acceptButtonStyleClass: 'p-button-danger'
      })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.voidPayment(row.id, null).subscribe(() => {
            this.notifications.success(this.t('fees.paymentVoided'));
            this.load();
          });
        }
      });
  }

  // Charges
  protected openCharge(): void {
    this.charge = { studentProfileId: this.studentId, description: '', amount: 0, dueDate: todayIso() };
    this.chargeVisible.set(true);
  }

  protected saveCharge(): void {
    const c = this.charge;

    if (!c.studentProfileId || !c.description.trim() || !c.amount || !c.dueDate) {
      this.notifications.warn(this.t('fees.chargeIncomplete'));
      return;
    }

    this.saving.set(true);
    this.service
      .createCharge({ studentProfileId: c.studentProfileId, description: c.description, amount: c.amount, dueDate: c.dueDate })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('fees.chargeCreated'));
        this.chargeVisible.set(false);
        this.load();
      });
  }

  protected cancelCharge(row: ChargeDto): void {
    this.notifications
      .confirm({ message: this.t('fees.cancelChargeMessage', { name: row.description }), header: this.t('fees.cancelCharge') })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.cancelCharge(row.id).subscribe(() => this.load());
        }
      });
  }

  protected generate(): void {
    if (!this.genPeriod) {
      return;
    }

    this.saving.set(true);
    this.service
      .generateCharges(this.genPeriod, this.genGroupId)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe((r) => {
        this.notifications.success(this.t('fees.generated', { created: r.created, skipped: r.skipped, withoutRule: r.withoutRule }));
        this.generateVisible.set(false);
        this.load();
      });
  }

  // Rules
  protected openRule(row?: FeeRuleDto): void {
    this.editingRule.set(row ?? null);
    this.rule = row
      ? { name: row.name, amount: row.amount, frequency: row.frequency, studyGroupId: row.studyGroupId ?? null, gradeLevelId: row.gradeLevelId ?? null, isActive: row.isActive }
      : { name: '', amount: 0, frequency: 'Monthly', studyGroupId: null, gradeLevelId: null, isActive: true };
    this.ruleVisible.set(true);
  }

  protected saveRule(): void {
    const r = this.rule;
    const editing = this.editingRule();

    if (!r.name.trim() || r.amount === null || r.amount < 0) {
      this.notifications.warn(this.t('fees.ruleIncomplete'));
      return;
    }

    this.saving.set(true);
    const request$ = editing
      ? this.service.updateFeeRule(editing.id, { name: r.name, amount: r.amount, isActive: r.isActive })
      : this.service.createFeeRule({ name: r.name, amount: r.amount, frequency: r.frequency, studyGroupId: r.studyGroupId, gradeLevelId: r.gradeLevelId });

    request$.pipe(finalize(() => this.saving.set(false))).subscribe(() => {
      this.notifications.success(this.t('fees.ruleSaved'));
      this.ruleVisible.set(false);
      this.load();
    });
  }

  protected removeRule(row: FeeRuleDto): void {
    this.notifications
      .confirm({ message: this.t('fees.deleteRuleMessage', { name: row.name }), header: this.common('delete'), acceptButtonStyleClass: 'p-button-danger' })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.removeFeeRule(row.id).subscribe(() => this.load());
        }
      });
  }

  private emptyPayment() {
    return {
      studentProfileId: null as string | null,
      amount: 0 as number | null,
      paidOn: todayIso(),
      method: 'Cash' as PaymentMethod,
      cashAccountId: null as string | null,
      reference: ''
    };
  }
}

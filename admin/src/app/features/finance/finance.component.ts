import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableLazyLoadEvent, TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { finalize } from 'rxjs';
import { TranslateService } from '../../core/i18n/translate.service';
import {
  CashAccountDto,
  CashAccountType,
  CashDirection,
  CashTransactionDto,
  ExpenseCategoryDto,
  ExpenseDto,
  FinanceSummaryDto,
  TeacherPaymentDto,
  TeacherSettlementDto
} from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { FinanceService } from '../../core/services/finance.service';
import { LookupsService } from '../../core/services/lookups.service';
import { NotificationService } from '../../core/services/notification.service';
import { currentMonthRange, money, Option, todayIso } from '../shared/ops-utils';

type FinanceTab = 'summary' | 'cash' | 'expenses' | 'teachers';

/** Cash accounts, expenses, teacher settlements and the income/expense report (Mission Phase 5). */
@Component({
  selector: 'app-finance',
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
        <h1>{{ t('finance.title') }}</h1>
        <div class="subtitle">{{ t('finance.subtitle') }}</div>
      </div>
    </div>

    <div class="tabs">
      @if (canReports()) {
        <button type="button" [class.on]="tab() === 'summary'" (click)="setTab('summary')">{{ t('finance.tabs.summary') }}</button>
      }
      @if (canView()) {
        <button type="button" [class.on]="tab() === 'cash'" (click)="setTab('cash')">{{ t('finance.tabs.cash') }}</button>
        <button type="button" [class.on]="tab() === 'expenses'" (click)="setTab('expenses')">{{ t('finance.tabs.expenses') }}</button>
        <button type="button" [class.on]="tab() === 'teachers'" (click)="setTab('teachers')">{{ t('finance.tabs.teachers') }}</button>
      }
    </div>

    @if (tab() !== 'cash') {
      <div class="toolbar">
        <input pInputText type="date" [(ngModel)]="from" [title]="common('from')" />
        <input pInputText type="date" [(ngModel)]="to" [title]="common('to')" />
        <p-button [label]="common('apply')" icon="pi pi-filter" [outlined]="true" (onClick)="reload()" />
        <div class="spacer"></div>
        @if (tab() === 'expenses' && canManage()) {
          <p-button [label]="t('finance.newCategory')" icon="pi pi-tag" [outlined]="true" (onClick)="categoryName = ''; categoryVisible.set(true)" />
          <p-button [label]="t('finance.newExpense')" icon="pi pi-plus" (onClick)="openExpense()" />
        }
        @if (tab() === 'teachers' && canManage()) {
          <p-button [label]="t('finance.computeCompensations')" icon="pi pi-calculator" [outlined]="true" (onClick)="compute()" />
        }
      </div>
    }

    <!-- Summary -->
    @if (tab() === 'summary') {
      @if (summary(); as s) {
        <div class="kpi-grid">
          <div class="kpi in"><span>{{ t('finance.income') }}</span><strong>{{ money(s.income) }}</strong></div>
          <div class="kpi out"><span>{{ t('finance.expenses') }}</span><strong>{{ money(s.expenses) }}</strong></div>
          <div class="kpi out"><span>{{ t('finance.teacherPayments') }}</span><strong>{{ money(s.teacherPayments) }}</strong></div>
          <div class="kpi" [class.in]="s.net >= 0" [class.out]="s.net < 0"><span>{{ t('finance.net') }}</span><strong>{{ money(s.net) }}</strong></div>
          <div class="kpi"><span>{{ t('finance.chargesIssued') }}</span><strong>{{ money(s.chargesIssued) }}</strong></div>
          <div class="kpi out"><span>{{ t('fees.outstanding') }}</span><strong>{{ money(s.outstandingFees) }}</strong></div>
        </div>
        <div class="two-col">
          <div class="card">
            <h3 class="card-title">{{ t('finance.expensesByCategory') }}</h3>
            @for (c of s.expensesByCategory; track c.categoryId) {
              <div class="bar-row">
                <span>{{ c.name }}</span>
                <div class="bar"><div [style.width.%]="share(c.total, s.expenses)"></div></div>
                <strong>{{ money(c.total) }}</strong>
              </div>
            } @empty {
              <div class="muted">{{ t('finance.noExpenses') }}</div>
            }
          </div>
          <div class="card">
            <h3 class="card-title">{{ t('finance.cashBalances') }}</h3>
            @for (a of s.cashAccounts; track a.id) {
              <div class="bar-row"><span>{{ a.name }}</span><span class="muted">{{ t('cashAccountType.' + a.type) }}</span><strong>{{ money(a.balance) }}</strong></div>
            }
          </div>
        </div>
      } @else {
        <div class="state-panel"><i class="pi pi-chart-bar"></i>{{ common('loading') }}</div>
      }
    }

    <!-- Cash -->
    @if (tab() === 'cash') {
      <div class="toolbar">
        <p-select [options]="accountOptions()" [(ngModel)]="accountFilter" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('finance.allAccounts')" (onChange)="reload()" />
        <input pInputText type="date" [(ngModel)]="from" (change)="reload()" />
        <input pInputText type="date" [(ngModel)]="to" (change)="reload()" />
        <div class="spacer"></div>
        @if (canManage()) {
          <p-button [label]="t('finance.newAccount')" icon="pi pi-building-columns" [outlined]="true" (onClick)="accountVisible.set(true)" />
          <p-button [label]="t('finance.newTransaction')" icon="pi pi-arrow-right-arrow-left" (onClick)="openTransaction()" />
        }
      </div>
      <div class="account-cards">
        @for (a of accounts(); track a.id) {
          <div class="account-card" [class.inactive]="!a.isActive">
            <div class="account-head">
              <strong>{{ a.name }}</strong>
              @if (a.isDefault) { <p-tag [value]="t('finance.default')" severity="info" /> }
            </div>
            <div class="muted">{{ t('cashAccountType.' + a.type) }}</div>
            <div class="account-balance">{{ money(a.balance) }}</div>
            @if (canManage() && !a.isDefault && a.isActive) {
              <p-button [label]="t('finance.makeDefault')" size="small" [text]="true" (onClick)="makeDefault(a)" />
            }
          </div>
        }
      </div>
      <div class="card">
        <p-table [value]="transactions()" [lazy]="true" [paginator]="true" [rows]="pageSize()" [first]="first()" [totalRecords]="total()" [loading]="loading()" (onLazyLoad)="onLazyLoad($event)" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('finance.date') }}</th>
              <th>{{ t('finance.cashAccount') }}</th>
              <th>{{ t('finance.source') }}</th>
              <th>{{ common('description') }}</th>
              <th class="num">{{ t('finance.in') }}</th>
              <th class="num">{{ t('finance.out') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.occurredOn }}</td>
              <td>{{ row.cashAccountName }}</td>
              <td><p-tag [value]="t('cashSource.' + row.sourceType)" severity="secondary" /></td>
              <td>{{ row.description }}</td>
              <td class="num in-text">{{ row.direction === 'In' ? money(row.amount) : '' }}</td>
              <td class="num out-text">{{ row.direction === 'Out' ? money(row.amount) : '' }}</td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="6"><div class="state-panel"><i class="pi pi-money-bill"></i>{{ t('finance.noTransactions') }}</div></td></tr>
          </ng-template>
        </p-table>
      </div>
    }

    <!-- Expenses -->
    @if (tab() === 'expenses') {
      <div class="card">
        <p-table [value]="expenses()" [lazy]="true" [paginator]="true" [rows]="pageSize()" [first]="first()" [totalRecords]="total()" [loading]="loading()" (onLazyLoad)="onLazyLoad($event)" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('finance.date') }}</th>
              <th>{{ t('finance.category') }}</th>
              <th>{{ common('description') }}</th>
              <th>{{ t('fees.reference') }}</th>
              <th class="num">{{ t('fees.amount') }}</th>
              <th style="width:4rem"></th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.spentOn }}</td>
              <td>{{ row.categoryName }}</td>
              <td>{{ row.description }}</td>
              <td>{{ row.reference }}</td>
              <td class="num">{{ money(row.amount) }}</td>
              <td>
                @if (canManage()) {
                  <p-button icon="pi pi-trash" severity="danger" [text]="true" (onClick)="removeExpense(row)" />
                }
              </td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="6"><div class="state-panel"><i class="pi pi-shopping-cart"></i>{{ t('finance.noExpenses') }}</div></td></tr>
          </ng-template>
        </p-table>
      </div>
    }

    <!-- Teachers -->
    @if (tab() === 'teachers') {
      <div class="toolbar">
        <p-select [options]="teachers()" [(ngModel)]="teacherId" optionLabel="label" optionValue="value" [filter]="true" [placeholder]="t('finance.pickTeacher')" (onChange)="loadSettlement()" style="min-width:16rem" />
      </div>
      @if (settlement(); as s) {
        <div class="kpi-grid">
          <div class="kpi out"><span>{{ t('finance.unsettled') }}</span><strong>{{ money(s.unsettled) }}</strong></div>
          <div class="kpi"><span>{{ t('finance.unsettledSessions') }}</span><strong>{{ s.unsettledSessions }}</strong></div>
          <div class="kpi in"><span>{{ t('finance.paidInRange') }}</span><strong>{{ money(s.paidInRange) }}</strong></div>
        </div>
        <div class="card">
          <div class="card-head">
            <h3 class="card-title">{{ t('finance.sessionsToSettle') }}</h3>
            @if (canManage() && s.unsettled > 0) {
              <p-button [label]="t('finance.payTeacher')" icon="pi pi-send" (onClick)="openTeacherPayment()" />
            }
          </div>
          <p-table [value]="s.items" styleClass="p-datatable-sm" [scrollable]="true" scrollHeight="360px">
            <ng-template pTemplate="header">
              <tr>
                <th>{{ t('finance.date') }}</th>
                <th>{{ t('sessions.group') }}</th>
                <th class="num">{{ t('fees.amount') }}</th>
                <th>{{ common('status') }}</th>
              </tr>
            </ng-template>
            <ng-template pTemplate="body" let-row>
              <tr>
                <td>{{ row.sessionDate }}</td>
                <td>{{ row.groupName }}</td>
                <td class="num">{{ money(row.amount) }}</td>
                <td><p-tag [value]="row.teacherPaymentId ? t('finance.settled') : t('finance.unsettledTag')" [severity]="row.teacherPaymentId ? 'success' : 'warn'" /></td>
              </tr>
            </ng-template>
            <ng-template pTemplate="emptymessage">
              <tr><td colspan="4"><div class="muted">{{ t('finance.noCompensations') }}</div></td></tr>
            </ng-template>
          </p-table>
        </div>
      }
      <div class="card">
        <h3 class="card-title">{{ t('finance.teacherPaymentsHistory') }}</h3>
        <p-table [value]="teacherPayments()" styleClass="p-datatable-sm">
          <ng-template pTemplate="header">
            <tr>
              <th>{{ t('fees.paidOn') }}</th>
              <th>{{ t('groups.teacher') }}</th>
              <th>{{ t('finance.period') }}</th>
              <th>{{ t('finance.sessionsCount') }}</th>
              <th class="num">{{ t('fees.amount') }}</th>
            </tr>
          </ng-template>
          <ng-template pTemplate="body" let-row>
            <tr>
              <td>{{ row.paidOn }}</td>
              <td>{{ row.teacherName }}</td>
              <td>{{ row.periodFrom ?? '' }} → {{ row.periodTo ?? '' }}</td>
              <td>{{ row.settledSessions }}</td>
              <td class="num">{{ money(row.amount) }}</td>
            </tr>
          </ng-template>
          <ng-template pTemplate="emptymessage">
            <tr><td colspan="5"><div class="muted">{{ t('finance.noTeacherPayments') }}</div></td></tr>
          </ng-template>
        </p-table>
      </div>
    }

    <!-- Account dialog -->
    <p-dialog [visible]="accountVisible()" (visibleChange)="accountVisible.set($event)" [header]="t('finance.newAccount')" [modal]="true" [style]="{ width: '460px' }">
      <div class="form-grid">
        <div class="field"><label>{{ common('name') }} *</label><input pInputText [(ngModel)]="account.name" /></div>
        <div class="field">
          <label>{{ t('finance.accountType') }}</label>
          <p-select [options]="accountTypeOptions" [(ngModel)]="account.type" optionLabel="label" optionValue="value" appendTo="body" />
        </div>
        <div class="field"><label>{{ t('finance.openingBalance') }}</label><p-inputNumber [(ngModel)]="account.openingBalance" [maxFractionDigits]="2" /></div>
        <div class="field"><label>{{ t('finance.default') }}</label><p-toggleswitch [(ngModel)]="account.isDefault" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="accountVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveAccount()" />
      </ng-template>
    </p-dialog>

    <!-- Transaction dialog -->
    <p-dialog [visible]="transactionVisible()" (visibleChange)="transactionVisible.set($event)" [header]="t('finance.newTransaction')" [modal]="true" [style]="{ width: '480px' }">
      <div class="form-grid">
        <div class="field">
          <label>{{ t('finance.cashAccount') }} *</label>
          <p-select [options]="accountOptions()" [(ngModel)]="txn.cashAccountId" optionLabel="label" optionValue="value" appendTo="body" />
        </div>
        <div class="field">
          <label>{{ t('finance.direction') }}</label>
          <p-select [options]="directionOptions" [(ngModel)]="txn.direction" optionLabel="label" optionValue="value" appendTo="body" />
        </div>
        <div class="field"><label>{{ t('fees.amount') }} *</label><p-inputNumber [(ngModel)]="txn.amount" [min]="0" [maxFractionDigits]="2" /></div>
        <div class="field"><label>{{ t('finance.date') }}</label><input pInputText type="date" [(ngModel)]="txn.occurredOn" /></div>
        <div class="field"><label>{{ common('description') }}</label><input pInputText [(ngModel)]="txn.description" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="transactionVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveTransaction()" />
      </ng-template>
    </p-dialog>

    <!-- Expense dialog -->
    <p-dialog [visible]="expenseVisible()" (visibleChange)="expenseVisible.set($event)" [header]="t('finance.newExpense')" [modal]="true" [style]="{ width: '500px' }">
      <div class="form-grid">
        <div class="field">
          <label>{{ t('finance.category') }} *</label>
          <p-select [options]="categoryOptions()" [(ngModel)]="expense.expenseCategoryId" optionLabel="label" optionValue="value" appendTo="body" [placeholder]="common('selectOption')" />
        </div>
        <div class="field"><label>{{ common('description') }} *</label><input pInputText [(ngModel)]="expense.description" /></div>
        <div class="field"><label>{{ t('fees.amount') }} *</label><p-inputNumber [(ngModel)]="expense.amount" [min]="0" [maxFractionDigits]="2" /></div>
        <div class="field"><label>{{ t('finance.date') }}</label><input pInputText type="date" [(ngModel)]="expense.spentOn" /></div>
        <div class="field">
          <label>{{ t('finance.cashAccount') }}</label>
          <p-select [options]="accountOptions()" [(ngModel)]="expense.cashAccountId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('finance.defaultAccount')" appendTo="body" />
        </div>
        <div class="field"><label>{{ t('fees.reference') }}</label><input pInputText [(ngModel)]="expense.reference" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="expenseVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" [loading]="saving()" (onClick)="saveExpense()" />
      </ng-template>
    </p-dialog>

    <!-- Category dialog -->
    <p-dialog [visible]="categoryVisible()" (visibleChange)="categoryVisible.set($event)" [header]="t('finance.newCategory')" [modal]="true" [style]="{ width: '400px' }">
      <div class="form-grid">
        <div class="field"><label>{{ t('finance.categoryName') }} *</label><input pInputText [(ngModel)]="categoryName" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="categoryVisible.set(false)" />
        <p-button [label]="common('save')" icon="pi pi-check" (onClick)="addCategory()" />
      </ng-template>
    </p-dialog>

    <!-- Teacher payment dialog -->
    <p-dialog [visible]="teacherPayVisible()" (visibleChange)="teacherPayVisible.set($event)" [header]="t('finance.payTeacher')" [modal]="true" [style]="{ width: '460px' }">
      <p class="muted">{{ t('finance.payTeacherHint') }}</p>
      <div class="form-grid">
        <div class="field"><label>{{ t('fees.amount') }}</label><p-inputNumber [(ngModel)]="teacherPay.amount" [min]="0" [maxFractionDigits]="2" /></div>
        <div class="field"><label>{{ t('fees.paidOn') }}</label><input pInputText type="date" [(ngModel)]="teacherPay.paidOn" /></div>
        <div class="field">
          <label>{{ t('finance.cashAccount') }}</label>
          <p-select [options]="accountOptions()" [(ngModel)]="teacherPay.cashAccountId" optionLabel="label" optionValue="value" [showClear]="true" [placeholder]="t('finance.defaultAccount')" appendTo="body" />
        </div>
        <div class="field"><label>{{ t('groups.notes') }}</label><input pInputText [(ngModel)]="teacherPay.notes" /></div>
      </div>
      <ng-template pTemplate="footer">
        <p-button [label]="common('cancel')" severity="secondary" [text]="true" (onClick)="teacherPayVisible.set(false)" />
        <p-button [label]="t('finance.payTeacher')" icon="pi pi-check" [loading]="saving()" (onClick)="saveTeacherPayment()" />
      </ng-template>
    </p-dialog>
  `,
  styles: [
    `
      .muted { color: #94a3b8; font-size: 0.8rem; }
      .num { text-align: end; }
      .in-text { color: #15803d; }
      .out-text { color: #b91c1c; }
      .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
      .kpi { background: #fff; border: 1px solid #e3e8f0; border-radius: 10px; padding: 0.75rem 1rem; display: flex; flex-direction: column; }
      .kpi span { font-size: 0.78rem; color: #64748b; }
      .kpi strong { font-size: 1.25rem; }
      .kpi.in strong { color: #15803d; }
      .kpi.out strong { color: #b91c1c; }
      .two-col { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 1rem; }
      .card-title { margin: 0 0 0.75rem; font-size: 1rem; }
      .card-head { display: flex; align-items: center; justify-content: space-between; }
      .bar-row { display: grid; grid-template-columns: 1fr 1.2fr auto; align-items: center; gap: 0.75rem; padding: 0.35rem 0; font-size: 0.88rem; }
      .bar { background: #f1f5f9; border-radius: 4px; height: 8px; overflow: hidden; }
      .bar > div { background: #2563eb; height: 100%; }
      .account-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem; margin-bottom: 1rem; }
      .account-card { background: #fff; border: 1px solid #e3e8f0; border-radius: 10px; padding: 0.75rem 1rem; }
      .account-card.inactive { opacity: 0.55; }
      .account-head { display: flex; justify-content: space-between; align-items: center; gap: 0.5rem; }
      .account-balance { font-size: 1.3rem; font-weight: 700; margin-top: 0.25rem; }
    `
  ]
})
export class FinanceComponent implements OnInit {
  private readonly service = inject(FinanceService);
  private readonly lookups = inject(LookupsService);
  private readonly auth = inject(AuthService);
  private readonly notifications = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);

  protected readonly t = inject(TranslateService).t;
  protected readonly common = (key: string) => this.t('common.' + key);
  protected readonly money = money;

  protected readonly canView = signal(this.auth.hasPermission('Finance.View'));
  protected readonly canManage = signal(this.auth.hasPermission('Finance.Manage'));
  protected readonly canReports = signal(this.auth.hasPermission('Reports.View'));

  protected readonly tab = signal<FinanceTab>(this.initialTab());
  protected readonly summary = signal<FinanceSummaryDto | null>(null);
  protected readonly accounts = signal<CashAccountDto[]>([]);
  protected readonly accountOptions = signal<Option[]>([]);
  protected readonly transactions = signal<CashTransactionDto[]>([]);
  protected readonly expenses = signal<ExpenseDto[]>([]);
  protected readonly categories = signal<ExpenseCategoryDto[]>([]);
  protected readonly categoryOptions = signal<Option[]>([]);
  protected readonly teachers = signal<Option[]>([]);
  protected readonly settlement = signal<TeacherSettlementDto | null>(null);
  protected readonly teacherPayments = signal<TeacherPaymentDto[]>([]);

  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly pageNumber = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly first = signal(0);

  protected readonly accountVisible = signal(false);
  protected readonly transactionVisible = signal(false);
  protected readonly expenseVisible = signal(false);
  protected readonly teacherPayVisible = signal(false);
  protected readonly categoryVisible = signal(false);

  protected readonly accountTypeOptions = (['Cash', 'Bank'] as CashAccountType[]).map((v) => ({ label: this.t('cashAccountType.' + v), value: v }));
  protected readonly directionOptions = (['In', 'Out'] as CashDirection[]).map((v) => ({ label: this.t('finance.' + v.toLowerCase()), value: v }));

  protected from = currentMonthRange().from;
  protected to = currentMonthRange().to;
  protected accountFilter: string | null = null;
  protected categoryName = '';
  protected teacherId: string | null = null;

  protected account = { name: '', type: 'Cash' as CashAccountType, openingBalance: 0, isDefault: false };
  protected txn = { cashAccountId: null as string | null, direction: 'In' as CashDirection, amount: 0, occurredOn: todayIso(), description: '' };
  protected expense = this.emptyExpense();
  protected teacherPay = { amount: null as number | null, paidOn: todayIso(), cashAccountId: null as string | null, notes: '' };

  ngOnInit(): void {
    if (this.canView()) {
      this.loadAccounts();
      this.loadCategories();
      this.lookups.teachers().subscribe((o) => this.teachers.set(o));
    }

    this.load();
  }

  protected share(value: number, total: number): number {
    return total > 0 ? Math.round((value / total) * 100) : 0;
  }

  protected setTab(tab: FinanceTab): void {
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
    const page = { pageNumber: this.pageNumber(), pageSize: this.pageSize(), from: this.from || undefined, to: this.to || undefined };

    switch (this.tab()) {
      case 'summary':
        if (this.from && this.to) {
          this.summary.set(null);
          this.service.summary(this.from, this.to).subscribe((s) => this.summary.set(s));
        }
        break;
      case 'cash':
        this.loading.set(true);
        this.service
          .transactions({ ...page, cashAccountId: this.accountFilter ?? undefined })
          .pipe(finalize(() => this.loading.set(false)))
          .subscribe((p) => {
            this.transactions.set(p.items);
            this.total.set(p.totalItems);
          });
        break;
      case 'expenses':
        this.loading.set(true);
        this.service
          .expenses(page)
          .pipe(finalize(() => this.loading.set(false)))
          .subscribe((p) => {
            this.expenses.set(p.items);
            this.total.set(p.totalItems);
          });
        break;
      case 'teachers':
        this.loadSettlement();
        this.service
          .teacherPayments({ pageNumber: 1, pageSize: 50, teacherProfileId: this.teacherId ?? undefined })
          .subscribe((p) => this.teacherPayments.set(p.items));
        break;
    }
  }

  // Cash
  protected saveAccount(): void {
    if (!this.account.name.trim()) {
      return;
    }

    this.saving.set(true);
    this.service
      .createCashAccount(this.account)
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.common('savedSuccessfully'));
        this.accountVisible.set(false);
        this.account = { name: '', type: 'Cash', openingBalance: 0, isDefault: false };
        this.loadAccounts();
      });
  }

  protected makeDefault(a: CashAccountDto): void {
    this.service.updateCashAccount(a.id, { isDefault: true }).subscribe(() => this.loadAccounts());
  }

  protected openTransaction(): void {
    this.txn = { cashAccountId: this.accountFilter, direction: 'In', amount: 0, occurredOn: todayIso(), description: '' };
    this.transactionVisible.set(true);
  }

  protected saveTransaction(): void {
    if (!this.txn.cashAccountId || !this.txn.amount) {
      this.notifications.warn(this.t('finance.transactionIncomplete'));
      return;
    }

    this.saving.set(true);
    this.service
      .createTransaction({ ...this.txn, cashAccountId: this.txn.cashAccountId, description: this.txn.description || null })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.common('savedSuccessfully'));
        this.transactionVisible.set(false);
        this.loadAccounts();
        this.load();
      });
  }

  // Expenses
  protected addCategory(): void {
    const name = this.categoryName.trim();

    if (name) {
      this.service.createExpenseCategory(name).subscribe(() => {
        this.notifications.success(this.common('savedSuccessfully'));
        this.categoryVisible.set(false);
        this.loadCategories();
      });
    }
  }

  protected openExpense(): void {
    this.expense = this.emptyExpense();
    this.expenseVisible.set(true);
  }

  protected saveExpense(): void {
    const e = this.expense;

    if (!e.expenseCategoryId || !e.description.trim() || !e.amount) {
      this.notifications.warn(this.t('finance.expenseIncomplete'));
      return;
    }

    this.saving.set(true);
    this.service
      .createExpense({
        expenseCategoryId: e.expenseCategoryId,
        cashAccountId: e.cashAccountId,
        amount: e.amount,
        spentOn: e.spentOn || null,
        description: e.description,
        reference: e.reference || null
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.common('savedSuccessfully'));
        this.expenseVisible.set(false);
        this.loadAccounts();
        this.load();
      });
  }

  protected removeExpense(row: ExpenseDto): void {
    this.notifications
      .confirm({ message: this.t('finance.deleteExpenseMessage', { name: row.description }), header: this.common('delete'), acceptButtonStyleClass: 'p-button-danger' })
      .subscribe((confirmed) => {
        if (confirmed) {
          this.service.removeExpense(row.id).subscribe(() => {
            this.loadAccounts();
            this.load();
          });
        }
      });
  }

  // Teachers
  protected compute(): void {
    if (!this.from || !this.to) {
      return;
    }

    this.service.computeCompensations(this.from, this.to, this.teacherId).subscribe((r) => {
      this.notifications.success(this.t('finance.computed', { created: r.created, already: r.alreadyPriced, withoutRule: r.withoutRule }));
      this.load();
    });
  }

  protected loadSettlement(): void {
    if (!this.teacherId) {
      this.settlement.set(null);
      return;
    }

    this.service.settlement(this.teacherId, this.from || undefined, this.to || undefined).subscribe((s) => this.settlement.set(s));
    this.service
      .teacherPayments({ pageNumber: 1, pageSize: 50, teacherProfileId: this.teacherId })
      .subscribe((p) => this.teacherPayments.set(p.items));
  }

  protected openTeacherPayment(): void {
    this.teacherPay = { amount: this.settlement()?.unsettled ?? null, paidOn: todayIso(), cashAccountId: null, notes: '' };
    this.teacherPayVisible.set(true);
  }

  protected saveTeacherPayment(): void {
    if (!this.teacherId) {
      return;
    }

    this.saving.set(true);
    this.service
      .payTeacher({
        teacherProfileId: this.teacherId,
        from: this.from || null,
        to: this.to || null,
        amount: this.teacherPay.amount,
        paidOn: this.teacherPay.paidOn || null,
        cashAccountId: this.teacherPay.cashAccountId,
        notes: this.teacherPay.notes || null
      })
      .pipe(finalize(() => this.saving.set(false)))
      .subscribe(() => {
        this.notifications.success(this.t('finance.teacherPaid'));
        this.teacherPayVisible.set(false);
        this.loadAccounts();
        this.loadSettlement();
      });
  }

  private initialTab(): FinanceTab {
    const requested = this.route.snapshot.data['tab'] as FinanceTab | undefined;

    if (requested === 'summary' && this.canReports()) {
      return 'summary';
    }

    return this.canView() ? (requested ?? 'cash') : 'summary';
  }

  private loadAccounts(): void {
    this.service.cashAccounts().subscribe((rows) => {
      this.accounts.set(rows);
      this.accountOptions.set(rows.filter((a) => a.isActive).map((a) => ({ label: a.name, value: a.id })));
    });
  }

  private loadCategories(): void {
    this.service.expenseCategories().subscribe((rows) => {
      this.categories.set(rows);
      this.categoryOptions.set(rows.filter((c) => c.isActive).map((c) => ({ label: c.name, value: c.id })));
    });
  }

  private emptyExpense() {
    return {
      expenseCategoryId: null as string | null,
      cashAccountId: null as string | null,
      amount: 0,
      spentOn: todayIso(),
      description: '',
      reference: ''
    };
  }
}

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CashAccountDto,
  CashAccountType,
  CashDirection,
  CashTransactionDto,
  ChargeDto,
  ChargeStatus,
  ComputeCompensationsResultDto,
  CreatePaymentRequest,
  DashboardKpisDto,
  ExpenseCategoryDto,
  ExpenseDto,
  FeeFrequency,
  FeeRuleDto,
  FinanceSummaryDto,
  GenerateChargesResultDto,
  PaginatedList,
  PaginationQuery,
  PaymentDto,
  StudentBalanceDto,
  TeacherPaymentDto,
  TeacherSettlementDto
} from '../models';
import { ApiService } from './api.service';

export interface ChargeListQuery extends PaginationQuery {
  studentProfileId?: string;
  status?: ChargeStatus;
  periodKey?: string;
  outstandingOnly?: boolean;
}

export interface DateRangeQuery extends PaginationQuery {
  from?: string;
  to?: string;
}

/** Fees, payments, cash, expenses, teacher settlements and reports (Mission Phases 4-5). */
@Injectable({ providedIn: 'root' })
export class FinanceService {
  private readonly api = inject(ApiService);

  // Fee rules & charges
  feeRules(activeOnly = false): Observable<FeeRuleDto[]> {
    return this.api.get<FeeRuleDto[]>('/api/fee-rules', { activeOnly });
  }

  createFeeRule(request: {
    name: string;
    amount: number;
    frequency: FeeFrequency;
    studyGroupId?: string | null;
    gradeLevelId?: string | null;
    notes?: string | null;
  }): Observable<FeeRuleDto> {
    return this.api.post<FeeRuleDto>('/api/fee-rules', request);
  }

  updateFeeRule(
    id: string,
    request: { name?: string; amount?: number; isActive?: boolean; notes?: string | null }
  ): Observable<FeeRuleDto> {
    return this.api.put<FeeRuleDto>(`/api/fee-rules/${id}`, request);
  }

  removeFeeRule(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/fee-rules/${id}`);
  }

  charges(query: ChargeListQuery): Observable<PaginatedList<ChargeDto>> {
    return this.api.get<PaginatedList<ChargeDto>>('/api/charges', { ...query });
  }

  createCharge(request: {
    studentProfileId: string;
    description: string;
    amount: number;
    dueDate: string;
    studyGroupId?: string | null;
  }): Observable<ChargeDto> {
    return this.api.post<ChargeDto>('/api/charges', request);
  }

  cancelCharge(id: string): Observable<ChargeDto> {
    return this.api.post<ChargeDto>(`/api/charges/${id}/cancel`);
  }

  generateCharges(period: string, studyGroupId?: string | null): Observable<GenerateChargesResultDto> {
    return this.api.post<GenerateChargesResultDto>('/api/charges/generate', {
      period,
      studyGroupId: studyGroupId || null
    });
  }

  balance(studentId: string): Observable<StudentBalanceDto> {
    return this.api.get<StudentBalanceDto>(`/api/students/${studentId}/balance`);
  }

  // Payments
  payments(query: DateRangeQuery & { studentProfileId?: string; includeVoided?: boolean }): Observable<PaginatedList<PaymentDto>> {
    return this.api.get<PaginatedList<PaymentDto>>('/api/payments', { ...query });
  }

  createPayment(request: CreatePaymentRequest): Observable<PaymentDto> {
    return this.api.post<PaymentDto>('/api/payments', request);
  }

  voidPayment(id: string, reason: string | null): Observable<PaymentDto> {
    return this.api.post<PaymentDto>(`/api/payments/${id}/void`, { reason });
  }

  // Cash
  cashAccounts(): Observable<CashAccountDto[]> {
    return this.api.get<CashAccountDto[]>('/api/finance/cash-accounts');
  }

  createCashAccount(request: {
    name: string;
    type: CashAccountType;
    openingBalance: number;
    isDefault: boolean;
  }): Observable<CashAccountDto> {
    return this.api.post<CashAccountDto>('/api/finance/cash-accounts', request);
  }

  updateCashAccount(
    id: string,
    request: { name?: string; isActive?: boolean; isDefault?: boolean }
  ): Observable<CashAccountDto> {
    return this.api.put<CashAccountDto>(`/api/finance/cash-accounts/${id}`, request);
  }

  transactions(query: DateRangeQuery & { cashAccountId?: string }): Observable<PaginatedList<CashTransactionDto>> {
    return this.api.get<PaginatedList<CashTransactionDto>>('/api/finance/transactions', { ...query });
  }

  createTransaction(request: {
    cashAccountId: string;
    direction: CashDirection;
    amount: number;
    occurredOn?: string | null;
    description?: string | null;
  }): Observable<CashTransactionDto> {
    return this.api.post<CashTransactionDto>('/api/finance/transactions', request);
  }

  // Expenses
  expenseCategories(): Observable<ExpenseCategoryDto[]> {
    return this.api.get<ExpenseCategoryDto[]>('/api/finance/expense-categories');
  }

  createExpenseCategory(name: string): Observable<ExpenseCategoryDto> {
    return this.api.post<ExpenseCategoryDto>('/api/finance/expense-categories', { name });
  }

  expenses(query: DateRangeQuery & { expenseCategoryId?: string }): Observable<PaginatedList<ExpenseDto>> {
    return this.api.get<PaginatedList<ExpenseDto>>('/api/finance/expenses', { ...query });
  }

  createExpense(request: {
    expenseCategoryId: string;
    cashAccountId?: string | null;
    amount: number;
    spentOn?: string | null;
    description: string;
    reference?: string | null;
  }): Observable<ExpenseDto> {
    return this.api.post<ExpenseDto>('/api/finance/expenses', request);
  }

  removeExpense(id: string): Observable<boolean> {
    return this.api.delete<boolean>(`/api/finance/expenses/${id}`);
  }

  // Teacher settlements
  computeCompensations(from: string, to: string, teacherProfileId?: string | null): Observable<ComputeCompensationsResultDto> {
    return this.api.post<ComputeCompensationsResultDto>('/api/finance/compensations/compute', {
      from,
      to,
      teacherProfileId: teacherProfileId || null
    });
  }

  settlement(teacherId: string, from?: string, to?: string): Observable<TeacherSettlementDto> {
    return this.api.get<TeacherSettlementDto>(`/api/finance/teachers/${teacherId}/settlement`, { from, to });
  }

  teacherPayments(query: PaginationQuery & { teacherProfileId?: string }): Observable<PaginatedList<TeacherPaymentDto>> {
    return this.api.get<PaginatedList<TeacherPaymentDto>>('/api/finance/teacher-payments', { ...query });
  }

  payTeacher(request: {
    teacherProfileId: string;
    from?: string | null;
    to?: string | null;
    amount?: number | null;
    paidOn?: string | null;
    cashAccountId?: string | null;
    notes?: string | null;
  }): Observable<TeacherPaymentDto> {
    return this.api.post<TeacherPaymentDto>('/api/finance/teacher-payments', request);
  }

  // Reports
  summary(from: string, to: string): Observable<FinanceSummaryDto> {
    return this.api.get<FinanceSummaryDto>('/api/finance/summary', { from, to });
  }

  kpis(): Observable<DashboardKpisDto> {
    return this.api.get<DashboardKpisDto>('/api/dashboard/kpis');
  }
}

using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Finance;

public sealed record FeeRuleDto(
    Guid Id, string Name, decimal Amount, FeeFrequency Frequency, Guid? StudyGroupId, string? StudyGroupName,
    Guid? GradeLevelId, string? GradeLevelName, bool IsActive, string? Notes);

public sealed record CreateFeeRuleRequest(
    string? Name, decimal Amount, FeeFrequency Frequency, Guid? StudyGroupId, Guid? GradeLevelId, string? Notes);

public sealed record UpdateFeeRuleRequest(string? Name, decimal? Amount, bool? IsActive, string? Notes);

public sealed record ChargeDto(
    Guid Id, Guid StudentProfileId, string? StudentName, Guid? FeeRuleId, Guid? StudyGroupId, string? StudyGroupName,
    string Description, decimal Amount, decimal AmountPaid, decimal Outstanding, DateOnly DueDate, string? PeriodKey,
    ChargeStatus Status, DateTime CreatedAt);

public sealed record CreateChargeRequest(
    Guid StudentProfileId, string? Description, decimal Amount, DateOnly DueDate, Guid? StudyGroupId);

/// <summary>Generates one monthly charge per active enrollment for <see cref="Period"/> (yyyy-MM).</summary>
public sealed record GenerateChargesRequest(string? Period, Guid? StudyGroupId);

public sealed record GenerateChargesResultDto(int Created, int Skipped, int WithoutRule);

public sealed record AllocationDto(Guid ChargeId, string? ChargeDescription, decimal Amount);

public sealed record PaymentDto(
    Guid Id, Guid StudentProfileId, string? StudentName, Guid? CashAccountId, decimal Amount, decimal Allocated,
    decimal Unallocated, DateOnly PaidOn, PaymentMethod Method, string? Reference, string? Notes, bool IsVoided,
    string? VoidReason, IReadOnlyList<AllocationDto> Allocations, DateTime CreatedAt);

public sealed record AllocationRequest(Guid ChargeId, decimal Amount);

/// <summary>
/// Records a payment. With no <see cref="Allocations"/>, the amount settles the student's
/// open charges oldest-due first; any remainder stays as student credit.
/// </summary>
public sealed record CreatePaymentRequest(
    Guid StudentProfileId, decimal Amount, DateOnly? PaidOn, PaymentMethod Method, Guid? CashAccountId,
    string? Reference, string? Notes, IReadOnlyList<AllocationRequest>? Allocations);

public sealed record VoidPaymentRequest(string? Reason);

public sealed record StudentBalanceDto(
    Guid StudentProfileId, decimal TotalCharged, decimal TotalPaid, decimal Outstanding, decimal Credit,
    int OpenCharges);

public sealed record CashAccountDto(
    Guid Id, string Name, CashAccountType Type, decimal OpeningBalance, decimal Balance, bool IsActive, bool IsDefault);

public sealed record CreateCashAccountRequest(string? Name, CashAccountType Type, decimal OpeningBalance, bool IsDefault);

public sealed record UpdateCashAccountRequest(string? Name, bool? IsActive, bool? IsDefault);

public sealed record CashTransactionDto(
    Guid Id, Guid CashAccountId, string? CashAccountName, CashDirection Direction, decimal Amount, DateOnly OccurredOn,
    CashSourceType SourceType, Guid? SourceId, string? Description);

public sealed record CreateCashTransactionRequest(
    Guid CashAccountId, CashDirection Direction, decimal Amount, DateOnly? OccurredOn, string? Description);

public sealed record ExpenseCategoryDto(Guid Id, string Name, bool IsActive);

public sealed record CreateExpenseCategoryRequest(string? Name);

public sealed record ExpenseDto(
    Guid Id, Guid ExpenseCategoryId, string? CategoryName, Guid? CashAccountId, decimal Amount, DateOnly SpentOn,
    string Description, string? Reference);

public sealed record CreateExpenseRequest(
    Guid ExpenseCategoryId, Guid? CashAccountId, decimal Amount, DateOnly? SpentOn, string? Description, string? Reference);

public sealed record ComputeCompensationsRequest(DateOnly From, DateOnly To, Guid? TeacherProfileId);

public sealed record ComputeCompensationsResultDto(int Created, int AlreadyPriced, int WithoutRule);

public sealed record SessionCompensationDto(
    Guid Id, Guid ClassSessionId, DateOnly SessionDate, string? GroupName, Guid TeacherProfileId, decimal Amount,
    Guid? CompensationRuleId, Guid? TeacherPaymentId);

public sealed record TeacherSettlementDto(
    Guid TeacherProfileId, string? TeacherName, decimal Unsettled, int UnsettledSessions, decimal PaidInRange,
    IReadOnlyList<SessionCompensationDto> Items);

/// <summary>
/// Pays a teacher. Settles every unsettled compensation in [From, To]; the amount defaults
/// to their total. A different <see cref="Amount"/> is allowed (advances, adjustments).
/// </summary>
public sealed record CreateTeacherPaymentRequest(
    Guid TeacherProfileId, DateOnly? From, DateOnly? To, decimal? Amount, DateOnly? PaidOn, Guid? CashAccountId,
    string? Notes);

public sealed record TeacherPaymentDto(
    Guid Id, Guid TeacherProfileId, string? TeacherName, decimal Amount, DateOnly PaidOn, DateOnly? PeriodFrom,
    DateOnly? PeriodTo, Guid? CashAccountId, string? Notes, int SettledSessions);

public sealed record CategoryTotalDto(Guid CategoryId, string Name, decimal Total);

public sealed record FinanceSummaryDto(
    DateOnly From, DateOnly To, decimal Income, decimal Expenses, decimal TeacherPayments, decimal Net,
    decimal OutstandingFees, decimal ChargesIssued, IReadOnlyList<CategoryTotalDto> ExpensesByCategory,
    IReadOnlyList<CashAccountDto> CashAccounts);

/// <summary>Dashboard KPIs. Money fields are null when the caller lacks <c>Reports.View</c>.</summary>
public sealed record DashboardKpisDto(
    int ActiveStudents, int ActiveGroups, int ActiveTeachers, int SessionsToday, decimal? AttendanceRate30Days,
    decimal? OutstandingFees, decimal? IncomeThisMonth, decimal? ExpensesThisMonth);

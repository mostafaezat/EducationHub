using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Finance;

// Mission Phases 4 + 5 (original Phase 6). Navigation-free like scheduling: every relationship
// is an FK id configured in FinanceConfigurations, and handlers join explicitly.
// Money is decimal(18,2) in the tenant's currency (TenantSettings.CurrencySymbol).

/// <summary>
/// A price list entry. Scope precedence when resolving a group's monthly fee:
/// group-specific &gt; grade-specific &gt; tenant default (neither set).
/// </summary>
public class FeeRule : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public FeeFrequency Frequency { get; set; } = FeeFrequency.Monthly;

    public Guid? StudyGroupId { get; set; }

    public Guid? GradeLevelId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}

/// <summary>
/// Money a student owes. <see cref="Amount"/> is a snapshot taken when the charge is created,
/// so later price changes never rewrite history. Generated monthly charges are unique per
/// (student, group, period).
/// </summary>
public class Charge : TenantOwnedEntity
{
    public Guid StudentProfileId { get; set; }

    public Guid? FeeRuleId { get; set; }

    public Guid? StudyGroupId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public decimal AmountPaid { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>Billing period for generated charges, e.g. <c>2026-11</c>.</summary>
    public string? PeriodKey { get; set; }

    public ChargeStatus Status { get; set; } = ChargeStatus.Open;

    /// <summary>Optimistic-concurrency token: two payments racing for the same charge cannot both apply.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Money received from (or for) a student. Voiding keeps the row.</summary>
public class Payment : TenantOwnedEntity
{
    public Guid StudentProfileId { get; set; }

    public Guid? CashAccountId { get; set; }

    public decimal Amount { get; set; }

    public DateOnly PaidOn { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public bool IsVoided { get; set; }

    public DateTime? VoidedAt { get; set; }

    public string? VoidReason { get; set; }
}

/// <summary>How much of a payment settles which charge.</summary>
public class PaymentAllocation : TenantOwnedEntity
{
    public Guid PaymentId { get; set; }

    public Guid ChargeId { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>A cash box or bank account. Balance = opening + ins - outs.</summary>
public class CashAccount : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public CashAccountType Type { get; set; } = CashAccountType.Cash;

    public decimal OpeningBalance { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Used when a posting names no account.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// One movement of money in or out of a cash account. (SourceType, SourceId) is unique, so
/// a payment, expense or teacher payment can never be posted twice.
/// </summary>
public class CashTransaction : TenantOwnedEntity
{
    public Guid CashAccountId { get; set; }

    public CashDirection Direction { get; set; }

    public decimal Amount { get; set; }

    public DateOnly OccurredOn { get; set; }

    public CashSourceType SourceType { get; set; } = CashSourceType.Manual;

    public Guid? SourceId { get; set; }

    public string? Description { get; set; }
}

public class ExpenseCategory : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class Expense : TenantOwnedEntity
{
    public Guid ExpenseCategoryId { get; set; }

    public Guid? CashAccountId { get; set; }

    public decimal Amount { get; set; }

    public DateOnly SpentOn { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Reference { get; set; }
}

/// <summary>A settlement paid to a teacher, covering the session compensations it settles.</summary>
public class TeacherPayment : TenantOwnedEntity
{
    public Guid TeacherProfileId { get; set; }

    public Guid? CashAccountId { get; set; }

    public decimal Amount { get; set; }

    public DateOnly PaidOn { get; set; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    public string? Notes { get; set; }
}

/// <summary>
/// What a teacher earned for one completed session, priced by the compensation resolver at
/// computation time (snapshot). Unique per session; settled when linked to a TeacherPayment.
/// </summary>
public class SessionCompensation : TenantOwnedEntity
{
    public Guid ClassSessionId { get; set; }

    public Guid TeacherProfileId { get; set; }

    public Guid? CompensationRuleId { get; set; }

    public decimal Amount { get; set; }

    public Guid? TeacherPaymentId { get; set; }

    /// <summary>Optimistic-concurrency token: a session cannot be settled by two teacher payments.</summary>
    public byte[] RowVersion { get; set; } = [];
}

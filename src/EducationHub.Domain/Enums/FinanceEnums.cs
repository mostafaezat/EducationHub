namespace EducationHub.Domain.Enums;

public enum FeeFrequency
{
    /// <summary>Charged once, manually or on enrollment.</summary>
    OneTime = 0,

    /// <summary>Generated once per calendar month for each active enrollment.</summary>
    Monthly = 1
}

public enum ChargeStatus
{
    Open = 0,
    PartiallyPaid = 1,
    Paid = 2,
    Cancelled = 3
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
    Other = 3
}

public enum CashAccountType
{
    Cash = 0,
    Bank = 1
}

public enum CashDirection
{
    In = 0,
    Out = 1
}

/// <summary>What produced a cash movement. (SourceType, SourceId) is unique: one posting per source.</summary>
public enum CashSourceType
{
    Manual = 0,
    Payment = 1,
    PaymentVoid = 2,
    Expense = 3,
    TeacherPayment = 4
}

using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;

namespace EducationHub.Application.Features.Tenancy.DTOs;

public sealed record TenantDto(
    Guid Id,
    string Slug,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    string? Country,
    TenantType Type,
    TenantStatus Status,
    string Currency,
    string TimeZone,
    string DefaultLanguage,
    bool IsActive,
    DateTime? SubscriptionStartDate,
    DateTime? SubscriptionExpiryDate,
    bool IsSubscriptionExpired,
    DateTime CreatedAt,
    string? AdminEmail = null)
{
    public static TenantDto FromEntity(
        Domain.Entities.Tenancy.Tenant tenant,
        DateTime utcNow,
        string? adminEmail = null)
        => new(
            tenant.Id,
            tenant.Slug,
            tenant.Name,
            tenant.NameAr,
            tenant.Description,
            tenant.DescriptionAr,
            tenant.Email,
            tenant.Phone,
            tenant.Address,
            tenant.AddressAr,
            tenant.City,
            tenant.Country,
            tenant.Type,
            tenant.Status,
            tenant.Currency,
            tenant.TimeZone,
            tenant.DefaultLanguage,
            tenant.IsActive,
            tenant.SubscriptionStartDate,
            tenant.SubscriptionExpiryDate,
            tenant.IsSubscriptionExpired(utcNow),
            tenant.CreatedAt,
            adminEmail);
}

public sealed record CreateTenantRequest(
    string Slug,
    string Name,
    string? NameAr,
    TenantType Type,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    string? Country,
    string Currency,
    string TimeZone,
    string DefaultLanguage,
    string? Description,
    string? DescriptionAr,
    string AdminFullName,
    string AdminEmail,
    string AdminPassword);

public sealed record UpdateTenantRequest(
    string? Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    string? Country,
    string? Currency,
    string? TimeZone,
    string? DefaultLanguage,
    TenantStatus? Status,
    bool? IsActive,
    DateTime? SubscriptionStartDate,
    DateTime? SubscriptionExpiryDate);

public sealed record BranchDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? NameAr,
    string? Code,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    BranchStatus Status,
    bool IsMainBranch,
    DateTime CreatedAt)
{
    public static BranchDto FromEntity(Domain.Entities.Tenancy.Branch branch)
        => new(
            branch.Id,
            branch.TenantId,
            branch.Name,
            branch.NameAr,
            branch.Code,
            branch.Email,
            branch.Phone,
            branch.Address,
            branch.AddressAr,
            branch.City,
            branch.Status,
            branch.IsMainBranch,
            branch.CreatedAt);
}

public sealed record CreateBranchRequest(
    string Name,
    string? NameAr,
    string? Code,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    bool IsMainBranch);

public sealed record UpdateBranchRequest(
    string? Name,
    string? NameAr,
    string? Code,
    string? Email,
    string? Phone,
    string? Address,
    string? AddressAr,
    string? City,
    BranchStatus? Status,
    bool? IsMainBranch);

/// <summary>Echoes the tenant row so the settings screen has context.</summary>
public sealed record TenantSettingsDto(
    Guid TenantId,
    string TenantName,
    bool NotifyParentOnAbsence,
    bool NotifyParentOnSessionCancellation,
    int LateThresholdMinutes,
    bool RequireExcuseReason,
    bool AutoGenerateMonthlyFees,
    int FeeDueDayOfMonth,
    int GracePeriodDays,
    decimal SiblingDiscountPercentage,
    decimal DefaultPassingPercentage,
    bool UseCustomGradingScale,
    bool NotifyParentOnFeeDue,
    bool NotifyParentOnPaymentReceived,
    bool NotifyParentOnHomeworkAssigned,
    bool NotifyParentOnExamResultPublished,
    bool NotifyParentOnMakeupClassScheduled,
    string DateFormat,
    string CurrencySymbol)
{
    public static TenantSettingsDto FromEntity(
        Domain.Entities.Tenancy.TenantSettings settings,
        string tenantName)
        => new(
            settings.TenantId,
            tenantName,
            settings.NotifyParentOnAbsence,
            settings.NotifyParentOnSessionCancellation,
            settings.LateThresholdMinutes,
            settings.RequireExcuseReason,
            settings.AutoGenerateMonthlyFees,
            settings.FeeDueDayOfMonth,
            settings.GracePeriodDays,
            settings.SiblingDiscountPercentage,
            settings.DefaultPassingPercentage,
            settings.UseCustomGradingScale,
            settings.NotifyParentOnFeeDue,
            settings.NotifyParentOnPaymentReceived,
            settings.NotifyParentOnHomeworkAssigned,
            settings.NotifyParentOnExamResultPublished,
            settings.NotifyParentOnMakeupClassScheduled,
            settings.DateFormat,
            settings.CurrencySymbol);
}

public sealed record UpdateTenantSettingsRequest(
    bool? NotifyParentOnAbsence,
    bool? NotifyParentOnSessionCancellation,
    int? LateThresholdMinutes,
    bool? RequireExcuseReason,
    bool? AutoGenerateMonthlyFees,
    int? FeeDueDayOfMonth,
    int? GracePeriodDays,
    decimal? SiblingDiscountPercentage,
    decimal? DefaultPassingPercentage,
    bool? UseCustomGradingScale,
    bool? NotifyParentOnFeeDue,
    bool? NotifyParentOnPaymentReceived,
    bool? NotifyParentOnHomeworkAssigned,
    bool? NotifyParentOnExamResultPublished,
    bool? NotifyParentOnMakeupClassScheduled,
    string? DateFormat,
    string? CurrencySymbol);

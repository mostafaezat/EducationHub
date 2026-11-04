using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Tenancy;

/// <summary>
/// A tenant: one independent teacher, tutoring business or education center.
/// </summary>
/// <remarks>
/// <para>
/// This is the ROOT of the isolation model and is deliberately NOT <see cref="ITenantOwned"/>.
/// If it were, the tenant query filter would hide every tenant row and the platform
/// admin could not enumerate them. <see cref="EducationHubDbContext"/> therefore applies
/// only the soft-delete filter to this entity. This invariant is asserted by a unit test
/// so a future refactor cannot silently break it.
/// </para>
/// <para>
/// Business data hangs off this entity; the tenant's own settings live in
/// <see cref="TenantSettings"/>.
/// </para>
/// </remarks>
public class Tenant : SoftDeletableEntity
{
    /// <summary>URL-safe unique identifier, used for tenant-scoped routes and subdomains.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? NameAr { get; set; }

    public string? Description { get; set; }

    public string? DescriptionAr { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? AddressAr { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? LogoFileId { get; set; }

    /// <summary>Determines whether branches are used. See <see cref="TenantType"/>.</summary>
    public TenantType Type { get; set; } = TenantType.IndividualTeacher;

    public TenantStatus Status { get; set; } = TenantStatus.Trial;

    /// <summary>Default ISO 4217 currency code, e.g. <c>EGP</c>, <c>SAR</c>, <c>USD</c>.</summary>
    public string Currency { get; set; } = "EGP";

    /// <summary>IANA time zone id, e.g. <c>Africa/Cairo</c>. All server times are UTC.</summary>
    public string TimeZone { get; set; } = "Africa/Cairo";

    /// <summary>Default UI language for this tenant's users. Arabic is the product default.</summary>
    public string DefaultLanguage { get; set; } = "ar";

    public bool IsActive { get; set; } = true;

    public DateTime? SubscriptionStartDate { get; set; }

    public DateTime? SubscriptionExpiryDate { get; set; }

    public TenantSettings? Settings { get; set; }

    public ICollection<Branch> Branches { get; set; } = [];

    public bool IsSubscriptionExpired(DateTime utcNow)
        => SubscriptionExpiryDate.HasValue && SubscriptionExpiryDate.Value < utcNow;

    public bool CanAccess(DateTime utcNow)
        => !IsDeleted && IsActive && Status is TenantStatus.Active or TenantStatus.Trial
           && !IsSubscriptionExpired(utcNow);
}

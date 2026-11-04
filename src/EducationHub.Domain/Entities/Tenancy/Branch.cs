using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Tenancy;

/// <summary>
/// An optional physical location of a tenant. A single independent teacher operates with
/// no branches at all (spec section 17).
/// </summary>
public class Branch : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    public string? NameAr { get; set; }

    /// <summary>Optional branch code. Unique per tenant when set.</summary>
    public string? Code { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? AddressAr { get; set; }

    public string? City { get; set; }

    public BranchStatus Status { get; set; } = BranchStatus.Active;

    public bool IsMainBranch { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

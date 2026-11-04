using EducationHub.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Domain.Entities.Identity;

/// <summary>
/// A role. Tenant-scoped for the seven business roles; platform-level when
/// <see cref="TenantId"/> is <c>null</c>.
/// </summary>
/// <remarks>
/// Permissions are not stored in a separate join table. They are persisted as Identity
/// role claims with <c>ClaimType == "Permission"</c>, which is the pattern used by the
/// reference ERP solution: it keeps the schema lean and gives
/// <c>RoleManager.GetClaimsAsync</c> the values directly. The catalog itself lives in
/// <c>PermissionConstants</c> so code and database can never disagree about what exists.
/// </remarks>
public class ApplicationRole : IdentityRole<Guid>, IAuditableEntity, ITenantOwned
{
    /// <summary>Owning tenant. <c>null</c> for platform-level roles such as PlatformAdmin.</summary>
    public Guid TenantId { get; set; }

    public string? Description { get; set; }

    public string? DescriptionAr { get; set; }

    /// <summary>System roles cannot be renamed or deleted, only have permissions changed.</summary>
    public bool IsSystemRole { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}

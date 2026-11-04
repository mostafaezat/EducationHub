using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Audit;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Domain.Entities.Identity;

/// <summary>
/// An EducationHub user: tenant staff, a parent, or a student.
/// </summary>
/// <remarks>
/// <para>
/// Password hashing, lockout, email confirmation and token providers are all provided by
/// ASP.NET Core Identity - nothing here reimplements crypto (see
/// <c>EducationHubDbContext</c>, which maps this to the <c>Users</c> table).
/// </para>
/// <para>
/// Implements <see cref="ITenantOwned"/> through an interface rather than inheriting
/// <c>TenantOwnedEntity</c>, because <see cref="IdentityUser{TKey}"/> is already the base
/// class. The tenant query filter still applies - discovered via the interface - so a
/// Tenant A administrator can never list Tenant B's users.
/// </para>
/// <para>
/// <c>TenantId</c> is <c>null</c> for platform administrators, which is exactly why the
/// filter allows a platform-scope bypass for that role alone.
/// </para>
/// </remarks>
public class ApplicationUser : IdentityUser<Guid>, IAuditableEntity, ITenantOwned
{
    /// <summary>Owning tenant. <c>null</c> only for platform administrators.</summary>
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PhotoFileId { get; set; }

    /// <summary>Home branch for multi-branch centers. <c>null</c> for single-branch or branchless tenants.</summary>
    public Guid? BranchId { get; set; }

    /// <summary>ISO 639-1 code used for server-side localization and API messages.</summary>
    public string PreferredLanguage { get; set; } = "ar";

    /// <summary>
    /// Account lifecycle flag (spec section 13: activation / deactivation).
    /// Deactivation is preferred over deletion so audit and finance history survives.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public string? Notes { get; set; }

    // ---- Audit --------------------------------------------------------------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid? CreatedById { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }

    // ---- Security / session tracking ----------------------------------------
    public DateTime? LastLoginAt { get; set; }

    public string? LastLoginIp { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>Identity's own lockout fields are used; these are EducationHub extras.</summary>
    public DateTime? LockedOutUntil { get; set; }

    public bool IsLockedOut => LockedOutUntil.HasValue && LockedOutUntil.Value > DateTime.UtcNow;

    // ---- Navigation ---------------------------------------------------------
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public ICollection<LoginHistory> LoginHistory { get; set; } = [];
}

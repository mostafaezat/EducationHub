namespace EducationHub.Domain.Common;

/// <summary>Common audit fields required by spec section 52.</summary>
public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }

    Guid? CreatedById { get; set; }

    DateTime? UpdatedAt { get; set; }

    Guid? UpdatedById { get; set; }
}

/// <summary>
/// Marker for entities that belong to exactly one tenant.
/// <para>
/// The tenant query filter and the <c>IX_{Table}_TenantId</c> index are both discovered
/// through this interface, NOT through a base class. Detection by interface matters
/// because <c>ApplicationUser</c> and <c>ApplicationRole</c> inherit from ASP.NET Core
/// Identity types and therefore cannot share an EducationHub base class - a
/// base-class-driven filter would silently leave them unfiltered and leak one tenant's
/// user list to another.
/// </para>
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

/// <summary>
/// Marker for entities that should be soft deleted. Used only where history matters
/// (spec section 52: do not overuse soft delete).
/// </summary>
public interface ISoftDeletableEntity
{
    bool IsDeleted { get; set; }

    DateTime? DeletedAt { get; set; }

    Guid? DeletedById { get; set; }
}

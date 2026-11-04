namespace EducationHub.Domain.Common;

/// <summary>
/// Base for every tenant-owned, soft-deletable business entity.
/// Deriving from this type is what makes the tenant isolation invariant impossible to
/// forget: the global query filter and the tenant index are applied automatically to all
/// of its descendants.
/// </summary>
public abstract class TenantOwnedEntity : SoftDeletableEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
}

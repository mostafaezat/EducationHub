namespace EducationHub.Domain.Common;

/// <summary>
/// Base for entities that are soft deleted. Deliberately NOT tenant-owned, because the
/// <see cref="Tenancy.Tenant"/> root and platform-level records are not scoped to a tenant.
/// </summary>
public abstract class SoftDeletableEntity : AuditableEntity, ISoftDeletableEntity
{
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedById { get; set; }
}

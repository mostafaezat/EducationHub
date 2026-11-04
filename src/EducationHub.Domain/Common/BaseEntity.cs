namespace EducationHub.Domain.Common;

/// <summary>
/// Root of every EducationHub entity. Uses a client-generated GUID so an entity can be
/// created and referenced (e.g. queued for a notification) before it is persisted.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

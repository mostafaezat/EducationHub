using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Audit;

/// <summary>
/// Append-only record of a significant action (spec section 42).
/// </summary>
/// <remarks>
/// <para>
/// Audit rows are never updated or soft-deleted through the API - there is deliberately no
/// update or delete handler, so a normal user has no code path that can alter history.
/// </para>
/// <para>
/// <see cref="OldValue"/> and <see cref="NewValue"/> hold a JSON projection of the changed
/// properties only, never the whole entity and never secrets. Serialization and redaction
/// happen in the Application layer (<c>AuditLogger</c>), keeping this entity free of any
/// infrastructure concern.
/// </para>
/// </remarks>
public class AuditLog : TenantOwnedEntity
{
    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>Entity type name, e.g. <c>Student</c>.</summary>
    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    /// <summary>Correlation id, shared with the structured log entry for the same request.</summary>
    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Description { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

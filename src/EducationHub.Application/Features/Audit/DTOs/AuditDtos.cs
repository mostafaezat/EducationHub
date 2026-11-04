using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Audit.DTOs;

/// <summary>
/// An audit row as returned by the API.
/// </summary>
/// <param name="OldValue">
/// The change payload, kept as the raw stored JSON string rather than re-typed into a
/// dictionary. The stored shape varies by entity, and re-parsing it here would couple the
/// read path to every entity's current shape - so a change to any entity would break
/// reading audit history written before it.
/// </param>
public sealed record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? UserName,
    AuditAction Action,
    string EntityName,
    string? EntityId,
    string? CorrelationId,
    string? IpAddress,
    string? OldValue,
    string? NewValue,
    string? Description,
    DateTime OccurredAt)
{
    public static AuditLogDto FromEntity(AuditLog log)
        => new(
            log.Id,
            log.UserId,
            log.UserName,
            log.Action,
            log.EntityName,
            log.EntityId,
            log.CorrelationId,
            log.IpAddress,
            log.OldValue,
            log.NewValue,
            log.Description,
            log.OccurredAt);
}

/// <summary>
/// A grouped audit summary for the activity dashboard.
/// </summary>
/// <param name="UserId">
/// Null for platform-level actions, which carry no tenant - those are grouped under their
/// own row rather than being attributed to whoever happens to be signed in.
/// </param>
public sealed record AuditSummaryDto(
    AuditAction Action,
    Guid? UserId,
    string? UserName,
    int Count,
    DateTime LastOccurredAt);

/// <summary>One authentication attempt.</summary>
/// <param name="FailureReason">
/// Included deliberately. An administrator investigating a lockout needs to know whether
/// the attempts failed on a bad password or on a deactivated account - those need
/// different fixes - and the message alone would not distinguish them.
/// </param>
public sealed record LoginHistoryDto(
    Guid Id,
    Guid UserId,
    string? UserName,
    bool Succeeded,
    string? FailureReason,
    string? IpAddress,
    string? UserAgent,
    DateTime AttemptedAt)
{
    public static LoginHistoryDto FromEntity(Domain.Entities.Identity.LoginHistory entry)
        => new(
            entry.Id,
            entry.UserId,
            entry.UserName,
            entry.Succeeded,
            entry.FailureReason,
            entry.IpAddress,
            entry.UserAgent,
            entry.AttemptedAt);
}

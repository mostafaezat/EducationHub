using System.Text.Json;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EducationHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every entity added or modified in a unit of
/// work.
/// </summary>
/// <remarks>
/// <para>
/// Auditing happens inside the same <c>SaveChanges</c> as the business change, so the log
/// and the mutation commit or roll back together. A separate save would allow a successful
/// mutation with no record of it.
/// </para>
/// <para>
/// Only changed properties are serialized, and sensitive ones are dropped by a denylist. A
/// denylist is deliberate: a newly added sensitive property is protected by default and has
/// to be explicitly unblocked, whereas an allowlist silently starts leaking as soon as
/// somebody adds a field.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUser currentUser,
    IDiagnosticContext diagnostics)
{
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "Password",
        "SecurityStamp",
        "ConcurrencyStamp",
        "RefreshToken",
        "TokenHash"
    };

    public Guid? CurrentUserId => currentUser.UserId;

    public Task AuditChangesAsync(DbContext context, CancellationToken cancellationToken)
    {
        Audit(context);
        return Task.CompletedTask;
    }

    public void AuditChanges(DbContext context) => Audit(context);

    private void Audit(DbContext context)
    {
        // Auditing the audit table itself would recurse: writing a log row would generate
        // another log row for that row, indefinitely.
        if (context.ChangeTracker.Entries<AuditLog>().Any())
        {
            return;
        }

        var entries = context.ChangeTracker
            .Entries()
            .Where(static e => e.State is EntityState.Added or EntityState.Modified)
            .Where(static e => e.Entity is IAuditableEntity)
            .Where(static e => e.Entity is not AuditLog)
            .ToArray();

        if (entries.Length == 0)
        {
            return;
        }

        var requestTenantId = currentUser.TenantId is { } t && t != Guid.Empty ? t : (Guid?)null;

        foreach (var entry in entries)
        {
            var isInsert = entry.State == EntityState.Added;

            // Sign-in writes (login history, refresh tokens, last-login stamps) happen before
            // the request is authenticated, so there is no caller yet: the row's own user is
            // the actor.
            var actorId = currentUser.UserId ?? ReadGuid(entry, "UserId");

            var log = new AuditLog
            {
                // The request's tenant when there is one. Otherwise - an anonymous sign-in, or
                // the platform administrator - the tenant that owns the changed row, so a
                // centre's audit trail shows its own users' sign-ins and what the platform
                // changed in its data. Rows owned by no tenant stay platform-only (Guid.Empty).
                TenantId = requestTenantId ?? ReadGuid(entry, "TenantId") ?? Guid.Empty,
                UserId = actorId,
                UserName = currentUser.UserName,
                Action = isInsert ? AuditAction.Created : AuditAction.Updated,
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = GetEntityId(entry),
                CorrelationId = diagnostics.CorrelationId,
                IpAddress = currentUser.IpAddress,
                OldValue = isInsert ? null : SerializeChanges(entry, originalValues: true),
                NewValue = SerializeChanges(entry, originalValues: false),
                OccurredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedById = actorId
            };

            context.Set<AuditLog>().Add(log);
        }
    }

    /// <summary>Reads a Guid / Guid? property of the entry when the entity has one.</summary>
    private static Guid? ReadGuid(EntityEntry entry, string propertyName)
    {
        var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);

        return property?.CurrentValue is Guid value && value != Guid.Empty ? value : null;
    }

    private static string? GetEntityId(EntityEntry entry)
    {
        if (entry.Entity is BaseEntity entity)
        {
            return entity.Id.ToString();
        }

        // Identity entities (ApplicationUser, ApplicationRole) are not BaseEntity but do
        // carry a Guid Id, so their id is still recorded for them.
        var idProperty = entry.Properties
            .FirstOrDefault(static p => p.Metadata.Name == "Id")?
            .CurrentValue;

        return idProperty?.ToString();
    }

    private static string? SerializeChanges(EntityEntry entry, bool originalValues)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Properties)
        {
            if (RedactedProperties.Contains(property.Metadata.Name))
            {
                continue;
            }

            var current = property.CurrentValue;
            var original = property.OriginalValue;

            var changed = !Equals(current, original);

            if (originalValues)
            {
                if (changed)
                {
                    changes[property.Metadata.Name] = original;
                }
            }
            else if (entry.State == EntityState.Added || changed)
            {
                changes[property.Metadata.Name] = current;
            }
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
    }
}

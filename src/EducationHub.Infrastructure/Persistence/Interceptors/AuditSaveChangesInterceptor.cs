using System.Text.Json;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Audit;
using EducationHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EducationHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every entity added, modified or deleted in a unit
/// of work. Hard deletes and soft deletes (<c>IsDeleted</c> set to true) are both recorded as
/// <see cref="AuditAction.Deleted"/> with the row's key values.
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
            .Where(static e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
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

            // A hard delete, or a soft delete (IsDeleted flipped to true), is recorded as
            // "Deleted" - never as an update - with the row's key values, so the audit trail
            // says what disappeared even when the row itself is gone.
            var isDelete = entry.State == EntityState.Deleted || IsSoftDelete(entry);
            var action = isInsert ? AuditAction.Created : isDelete ? AuditAction.Deleted : AuditAction.Updated;

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
                Action = action,
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = GetEntityId(entry),
                CorrelationId = diagnostics.CorrelationId,
                IpAddress = currentUser.IpAddress,
                OldValue = isInsert ? null : SerializeChanges(entry, originalValues: true, includeKeys: isDelete),
                NewValue = entry.State == EntityState.Deleted ? null : SerializeChanges(entry, originalValues: false, includeKeys: isDelete),
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

    /// <summary>True when this save flips a soft-deletable row's <c>IsDeleted</c> from false to true.</summary>
    private static bool IsSoftDelete(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified || entry.Entity is not ISoftDeletableEntity)
        {
            return false;
        }

        var flag = entry.Property(nameof(ISoftDeletableEntity.IsDeleted));

        return flag.CurrentValue is true && flag.OriginalValue is false;
    }

    private static string? SerializeChanges(EntityEntry entry, bool originalValues, bool includeKeys = false)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

        // A hard-deleted row keeps no current state, so its whole last known state is recorded.
        var wholeRow = originalValues && entry.State == EntityState.Deleted;

        foreach (var property in entry.Properties)
        {
            if (RedactedProperties.Contains(property.Metadata.Name))
            {
                continue;
            }

            var current = property.CurrentValue;
            var original = property.OriginalValue;

            var changed = !Equals(current, original);

            // Deletes always carry the key values (Id, and composite keys of join rows), so the
            // removed row can be identified from the audit trail alone.
            if (wholeRow || (includeKeys && property.Metadata.IsKey()))
            {
                changes[property.Metadata.Name] = originalValues ? original : current;
                continue;
            }

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

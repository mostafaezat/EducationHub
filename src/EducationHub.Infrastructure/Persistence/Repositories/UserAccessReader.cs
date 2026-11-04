using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Batch reads over Identity's <c>UserRoles</c> and <c>RoleClaims</c> join tables.
/// </summary>
/// <remarks>
/// <para>
/// These tables are Identity internals with no tenant column of their own, so all knowledge
/// of their shape is confined to this one file. Handlers receive plain ids and dictionaries.
/// </para>
/// <para>
/// <b>Tenant safety.</b> None of these queries filter by tenant, because the join tables have
/// nothing to filter on - and because a role id is only ever supplied by a caller that
/// already resolved it through a tenant-filtered query. The user-facing lookups
/// (<see cref="GetUserIdsByRoleAsync"/>) deliberately return unfiltered ids for the same
/// reason documented on the interface: they are inputs to be intersected with a filtered
/// user query, never results in their own right.
/// </para>
/// </remarks>
public sealed class UserAccessReader(EducationHubDbContext context) : IUserAccessReader
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetRolesForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var ids = userIds as Guid[] ?? [.. userIds];

        var rows = await context.UserRoles
            .Where(ur => ids.Contains(ur.UserId))
            .Join(
                context.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, r.Name })
            .Select(x => new { x.UserId, x.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static x => x.UserId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<string>)[.. g
                    .Select(static x => x.Name)
                    .Where(static n => n is not null)
                    .Select(static n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)]);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPermissionsForUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var ids = userIds as Guid[] ?? [.. userIds];

        var rows = await context.UserRoles
            .Where(ur => ids.Contains(ur.UserId))
            .Join(
                context.RoleClaims,
                ur => ur.RoleId,
                rc => rc.RoleId,
                (ur, rc) => new { ur.UserId, rc.ClaimType, rc.ClaimValue })
            .Where(static x => x.ClaimType == EducationHubClaimTypes.PermissionClaimType)
            .Select(static x => new { x.UserId, x.ClaimValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static x => x.UserId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<string>)[.. g
                    .Select(static x => x.ClaimValue)
                    .Where(static v => !string.IsNullOrWhiteSpace(v))
                    .Select(static v => v!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)]);
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsByRoleAsync(
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return [];
        }

        // Identity normalizes role names to upper case, so the lookup must too - otherwise
        // "teacher" silently misses the stored "Teacher" and returns an empty member set.
        var normalized = roleName.Trim().ToUpperInvariant();

        return await context.UserRoles
            .Join(
                context.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, NormalizedName = r.NormalizedName })
            .Where(x => x.NormalizedName == normalized)
            .Select(static x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetPermissionsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var ids = roleIds as Guid[] ?? [.. roleIds];

        var rows = await context.RoleClaims
            .Where(rc => ids.Contains(rc.RoleId))
            .Where(static rc => rc.ClaimType == EducationHubClaimTypes.PermissionClaimType)
            .Select(static rc => new { rc.RoleId, rc.ClaimValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static x => x.RoleId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<string>)[.. g
                    .Select(static x => x.ClaimValue)
                    .Where(static v => !string.IsNullOrWhiteSpace(v))
                    .Select(static v => v!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)]);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetUserCountsByRoleIdsAsync(
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = roleIds as Guid[] ?? [.. roleIds];

        // Grouped in the database. Calling GetUsersInRoleAsync per role would materialize
        // every member row of every role on the page just to count them.
        var groups = await context.UserRoles
            .Where(ur => ids.Contains(ur.RoleId))
            .GroupBy(static ur => ur.RoleId)
            .Select(static g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return groups.ToDictionary(static g => g.RoleId, static g => g.Count);
    }
}

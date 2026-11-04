using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Roles.Commands;

/// <summary>
/// Shared role logic. Permission escalation is the theme here: a role is the only route to
/// capability, so every write must prove the caller already holds what it is granting.
/// </summary>
internal static class RoleCommandSupport
{
    /// <summary>
    /// Returns the tenant the role belongs to, or throws when there is none.
    /// </summary>
    /// <remarks>
    /// Roles are per-tenant. A platform administrator working without a tenant context is
    /// refused rather than quietly creating a tenant-less role, because that role would be
    /// invisible to (and unusable by) every tenant.
    /// </remarks>
    internal static Guid RequireTenant(ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "Roles.TenantContextRequired",
                "Role management requires an active tenant context.");
        }

        return tenantId;
    }

    /// <summary>
    /// Cleans a requested permission list against the catalog and reports any unknown code.
    /// </summary>
    /// <remarks>
    /// Unknown codes are rejected instead of ignored. Silently dropping them would let a
    /// caller believe a permission was granted while authorization denies it later - a bug
    /// that surfaces far from its cause.
    /// </remarks>
    internal static bool TryNormalizePermissions(
        IEnumerable<string>? requested,
        out IReadOnlyList<string> permissions,
        out string? unknown)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var cleaned = requested
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var catalog = PermissionConstants.GetAll();

        unknown = cleaned.FirstOrDefault(p => !catalog.Contains(p, StringComparer.OrdinalIgnoreCase));

        permissions = cleaned;

        return unknown is null;
    }

    /// <summary>
    /// Loads the caller's effective permissions from the DATABASE.
    /// </summary>
    /// <remarks>
    /// Deliberately not read from the JWT. A token's permission claims are a snapshot taken
    /// at login and live for its full lifetime, so a caller demoted a minute ago would keep
    /// its old authority if we trusted them - which defeats the check entirely.
    /// </remarks>
    internal static async Task<IReadOnlyCollection<string>> LoadCallerPermissionsAsync(
        IUserAccessReader accessReader,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessReader);
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.UserId is not { } callerId)
        {
            throw new BusinessRuleException("Auth.Unauthenticated", "Authentication is required.");
        }

        var permissions = await accessReader
            .GetPermissionsForUsersAsync([callerId], cancellationToken)
            .ConfigureAwait(false);

        return permissions.GetValueOrDefault(callerId) ?? [];
    }

    /// <summary>
    /// Refuses a grant the caller is not themselves authorized to make.
    /// </summary>
    internal static void EnsureNotEscalating(
        IReadOnlyCollection<string> target,
        IReadOnlyCollection<string> caller)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(caller);

        var extra = target.Except(caller, StringComparer.OrdinalIgnoreCase).ToArray();

        if (extra.Length > 0)
        {
            throw new BusinessRuleException(
                "Roles.CannotGrantUnheldPermission",
                $"You cannot grant permissions you do not hold yourself: {string.Join(", ", extra)}.");
        }
    }

    /// <summary>Loads a role the caller is allowed to see, scoped to the current tenant.</summary>
    internal static async Task<ApplicationRole> FindForTenantAsync(
        RoleManager<ApplicationRole> roleManager,
        ITenantContext tenantContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleManager);

        cancellationToken.ThrowIfCancellationRequested();

        // RoleManager has no id-based lookup, and the DbSet behind it is tenant-filtered, so
        // scanning the visible set is both correct and tenant-safe by construction.
        var role = await roleManager
            .Roles
            .FirstOrDefaultAsync(
                r => r.Id == id
                    && r.TenantId == (tenantContext.TenantId ?? Guid.Empty),
                cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            // "Not found" rather than "forbidden": a 403 would confirm the role exists.
            throw new BusinessRuleException("Roles.NotFound", "Role not found.");
        }

        return role;
    }

    /// <summary>Reads a role's permission claims, sorted for a stable API response.</summary>
    internal static async Task<IReadOnlyList<string>> GetPermissionsAsync(
        RoleManager<ApplicationRole> roleManager,
        ApplicationRole role,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(role);

        cancellationToken.ThrowIfCancellationRequested();

        var claims = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

        return [.. claims
            .Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType)
            .Select(static c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Makes the role's stored permission claims match <paramref name="permissions"/> exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A full replace rather than a diff. Permission sets are small, and a replace cannot
    /// leave a stale claim behind - the failure mode of an additive-only API, where revoking
    /// the last permission of a role silently fails.
    /// </para>
    /// <para>
    /// Claim writes go through <c>RoleManager.AddClaimAsync</c>/<c>RemoveClaimAsync</c> one
    /// claim at a time. Unlike <c>UserManager</c>, <c>RoleManager</c> has no bulk variants,
    /// and a role never holds more than a few dozen permissions, so batching would buy
    /// nothing while depending on an API that does not exist.
    /// </para>
    /// </remarks>
    internal static async Task<IdentityResult> ReplacePermissionsAsync(
        RoleManager<ApplicationRole> roleManager,
        ApplicationRole role,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roleManager);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(permissions);

        cancellationToken.ThrowIfCancellationRequested();

        var existing = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

        var permissionClaims = existing
            .Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType)
            .ToArray();

        var stale = permissionClaims
            .Where(c => !permissions.Contains(c.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (var claim in stale)
        {
            var removal = await roleManager.RemoveClaimAsync(role, claim).ConfigureAwait(false);

            if (!removal.Succeeded)
            {
                return removal;
            }
        }

        var present = permissionClaims
            .Select(static c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions.Where(p => !present.Contains(p)))
        {
            var addition = await roleManager
                .AddClaimAsync(role, new System.Security.Claims.Claim(
                    EducationHubClaimTypes.PermissionClaimType,
                    permission))
                .ConfigureAwait(false);

            if (!addition.Succeeded)
            {
                return addition;
            }
        }

        return IdentityResult.Success;
    }
}

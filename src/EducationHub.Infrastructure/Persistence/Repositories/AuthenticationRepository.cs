using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// The only place in the codebase that calls <c>IgnoreQueryFilters</c>.
/// </summary>
/// <remarks>
/// <para>
/// Authentication runs before any tenant is known: the caller presents an email and a
/// password and nothing else, so there is no tenant to filter on yet. The presented refresh
/// token works the same way - the token is what identifies the tenant.
/// </para>
/// <para>
/// Because the filter is bypassed, every method here is also responsible for its own
/// tenant checks. That is not an optional extra: <see cref="GetByEmailAsync"/> will happily
/// return a suspended tenant's user, and it is the handler's job - not the query's - to
/// refuse the login.
/// </para>
/// </remarks>
public sealed class AuthenticationRepository(EducationHubDbContext context) : IAuthenticationRepository
{
    public Task<AuthenticationSnapshot?> GetByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        return LoadSnapshotAsync(
            context.Users.IgnoreQueryFilters().Where(u => u.NormalizedEmail == normalizedEmail),
            cancellationToken);
    }

    public async Task<AuthenticationSnapshot?> GetByPhoneAsync(
        IReadOnlyList<string> phoneVariants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phoneVariants);

        if (phoneVariants.Count == 0)
        {
            return null;
        }

        var variants = phoneVariants.ToArray();

        // No tenant is known before sign-in, so both lookups ignore the query filters; the
        // match itself (a number typed by the account owner) is what scopes the result.
        var fromUsers = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.PhoneNumber != null && variants.Contains(u.PhoneNumber))
            .Select(static u => u.Id)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var fromParents = await context.ParentProfiles
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.UserId != null && p.PhoneNumber != null && variants.Contains(p.PhoneNumber))
            .Select(static p => p.UserId!.Value)
            .Take(2)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var matches = fromUsers.Concat(fromParents).Distinct().ToList();

        return matches.Count == 1
            ? await GetByIdAsync(matches[0], cancellationToken).ConfigureAwait(false)
            : null;
    }

    public Task<AuthenticationSnapshot?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => LoadSnapshotAsync(
            context.Users.IgnoreQueryFilters().Where(u => u.Id == userId),
            cancellationToken);

    /// <summary>
    /// Loads the user, roles and effective permissions.
    /// </summary>
    /// <remarks>
    /// Three queries rather than one. The user row, the role names and the permission codes
    /// come from different tables with no navigable path between them (Identity stores
    /// permissions as claims on the role, not as a collection on it), so a single projection
    /// would have to cartesian-product them and then de-duplicate on the client.
    /// <para>
    /// The role query ignores the global query filters, deliberately. This runs on login,
    /// before any identity has been established: there is no principal, so there is no tenant
    /// for the filter to match against and it would hide every role - which is precisely why
    /// the admin's own token came back with no roles and no permissions. Visibility here is
    /// established by the join through <c>UserRoles</c> instead: a row is read if and only if
    /// it belongs to the user being authenticated, which is the only relationship that
    /// matters at this point in the request. The same argument applies to a tenant user
    /// logging in, whose own roles are equally invisible to a scope that does not yet exist.
    /// </para>
    /// </remarks>
    private async Task<AuthenticationSnapshot?> LoadSnapshotAsync(
        IQueryable<ApplicationUser> userQuery,
        CancellationToken cancellationToken)
    {
        var user = await userQuery.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        // BL-009: the profile carries the tenant's display name. Platform administrators
        // have no tenant (Guid.Empty), so they skip the lookup and keep null.
        var tenantName = user.TenantId == Guid.Empty
            ? null
            : await context.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Id == user.TenantId)
                .Select(static t => t.Name)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        var roles = await context.Roles
            .IgnoreQueryFilters()
            .Where(r => context.UserRoles.Any(
                ur => ur.UserId == user.Id && ur.RoleId == r.Id))
            .Select(static r => r.Name)
            .Where(static n => n != null)
            .Select(static n => n!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roleNames = roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roleNames.Length == 0)
        {
            return new AuthenticationSnapshot(user, [], [], tenantName);
        }

        var permissions = await context.RoleClaims
            .Where(rc => rc.ClaimType == EducationHubClaimTypes.PermissionClaimType)
            .Join(
                context.UserRoles.Where(ur => ur.UserId == user.Id),
                rc => rc.RoleId,
                ur => ur.RoleId,
                (rc, _) => rc.ClaimValue)
            .Where(static v => v != null && v != string.Empty)
            .Select(static v => v!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var distinct = permissions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AuthenticationSnapshot(user, roleNames, distinct, tenantName);
    }

    public Task<Tenant?> GetTenantForAuthenticationAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        return context.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<IReadOnlyList<RefreshToken>> GetTokenFamilyAsync(
        Guid familyId,
        CancellationToken cancellationToken = default)
        => await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId)
            .OrderBy(static t => t.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> TryClaimRefreshTokenAsync(
        Guid tokenId,
        Guid replacedByTokenId,
        CancellationToken cancellationToken = default)
    {
        // A single conditional UPDATE: SQL Server takes the row lock for the WHERE and the SET
        // together, so two concurrent calls cannot both see RevokedAt IS NULL. The tracked
        // entity is not touched here; the caller then saves it with the same values through
        // RevokeRefreshTokenAsync, which is also what writes the audit row.
        var now = DateTime.UtcNow;

        var affected = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.Id == tokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, "Rotated")
                    .SetProperty(t => t.ReplacedByTokenId, replacedByTokenId),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    public async Task RevokeRefreshTokenAsync(
        RefreshToken token,
        string reason,
        Guid? replacedByTokenId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        // Revoking twice is not an error. The refresh flow can reach here twice for one
        // presented token (once when it is replayed), and throwing would turn a
        // detect-and-defend situation into a 500.
        if (token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedReason = reason;
        token.ReplacedByTokenId = replacedByTokenId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeTokenFamilyAsync(
        Guid familyId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var tokens = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tokens.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }

        // One SaveChanges for the whole family: revoking a stolen token's descendants must
        // be atomic, or a crash mid-loop leaves live tokens in the database.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAllForUserAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var tokens = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tokens.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }

        // Deliberately no SaveChanges here. The password-change handler owns the unit of
        // work so the user row and every revocation commit together; saving here would
        // split that into two transactions.
    }
}

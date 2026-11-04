using System.Globalization;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Identity;

/// <summary>
/// User store whose role-name lookups are scoped to the tenant of the user being modified.
/// </summary>
/// <remarks>
/// <para>
/// Identity's stock store resolves a role by normalised name with a bare
/// <c>SingleOrDefault</c>, which is correct only for globally unique names. This schema
/// enforces uniqueness per tenant instead (<c>IX_Roles_TenantId_NormalizedName</c>) so that
/// every tenant owns its own <c>TenantAdmin</c> - the same reason
/// <see cref="TenantScopedRoleValidator"/> exists. Under the platform scope the global
/// tenant filter short-circuits to "everything", so a lookup of "TenantAdmin" matches one
/// row per tenant and the call throws <see cref="InvalidOperationException"/>
/// ("Sequence contains more than one element"), surfacing as HTTP 409 on
/// <c>POST /api/users</c>.
/// </para>
/// <para>
/// The correct scope for a role lookup is the tenant of the user being modified, and that
/// is the only thing these overrides add. A platform-scope caller's users carry
/// <see cref="Guid.Empty"/> and therefore reach exactly the platform role; a tenant-scope
/// caller's users carry their own tenant and therefore reach exactly the rows the global
/// filter already returned - tenant behaviour is unchanged.
/// </para>
/// </remarks>
public sealed class ApplicationUserStore(
    EducationHubDbContext context,
    IdentityErrorDescriber describer)
    : UserStore<ApplicationUser, ApplicationRole, EducationHubDbContext, Guid>(context, describer)
{
    public override async Task<bool> IsInRoleAsync(
        ApplicationUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var role = await FindRoleInUsersTenantAsync(user, normalizedRoleName, cancellationToken)
            .ConfigureAwait(false);

        return role is not null
            && await Context.Set<IdentityUserRole<Guid>>()
                .AnyAsync(
                    ur => ur.RoleId == role.Id && ur.UserId == user.Id,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public override async Task AddToRoleAsync(
        ApplicationUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var role = await FindRoleInUsersTenantAsync(user, normalizedRoleName, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Format(
                CultureInfo.CurrentCulture,
                "Role '{0}' does not exist in tenant {1}.",
                normalizedRoleName,
                user.TenantId));

        var alreadyMember = await Context.Set<IdentityUserRole<Guid>>()
            .AnyAsync(
                ur => ur.RoleId == role.Id && ur.UserId == user.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (!alreadyMember)
        {
            // Mirrors the stock store: the membership is tracked here and persisted by the
            // UserManager's own SaveChanges for this operation, on this same context.
            Context.Set<IdentityUserRole<Guid>>()
                .Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        }
    }

    public override async Task RemoveFromRoleAsync(
        ApplicationUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var role = await FindRoleInUsersTenantAsync(user, normalizedRoleName, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return;
        }

        var membership = await Context.Set<IdentityUserRole<Guid>>()
            .FirstOrDefaultAsync(
                ur => ur.RoleId == role.Id && ur.UserId == user.Id,
                cancellationToken)
            .ConfigureAwait(false);

        if (membership is not null)
        {
            Context.Set<IdentityUserRole<Guid>>().Remove(membership);
        }
    }

    /// <summary>
    /// The named role of <paramref name="user"/>'s own tenant, never any other tenant's.
    /// </summary>
    /// <remarks>
    /// Accepts both a normalised name and a raw one: Identity normalises with the default
    /// upper-invariant key normalizer (this project installs no custom one), so comparing
    /// against the upper-invariant form of whatever arrives covers either calling style.
    /// </remarks>
    private Task<ApplicationRole?> FindRoleInUsersTenantAsync(
        ApplicationUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        var upper = roleName.ToUpperInvariant();

        return Context.Set<ApplicationRole>().FirstOrDefaultAsync(
            r => r.TenantId == user.TenantId
                && (r.NormalizedName == roleName || r.NormalizedName == upper),
            cancellationToken);
    }
}

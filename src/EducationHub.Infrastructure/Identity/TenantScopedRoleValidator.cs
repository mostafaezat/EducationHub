using EducationHub.Domain.Entities.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Infrastructure.Identity;

/// <summary>
/// Role names are unique per tenant, not per database: every tenant gets its own
/// <c>TenantAdmin</c>, <c>Teacher</c> and so on, and the schema enforces it with the
/// <c>(TenantId, NormalizedName)</c> unique index. Identity's default validator treats the
/// normalised name as globally unique, so the second tenant's provisioning would fail with
/// "already taken" even though its roles are its own. This validator scopes the uniqueness
/// check to the role's tenant, matching the schema and the login-side role lookup.
/// </summary>
public sealed class TenantScopedRoleValidator : IRoleValidator<ApplicationRole>
{
    public async Task<IdentityResult> ValidateAsync(
        RoleManager<ApplicationRole> manager,
        ApplicationRole role)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(role);

        var normalizedName = manager.NormalizeKey(role.Name ?? string.Empty);

        var duplicate = await manager.Roles
            .IgnoreQueryFilters()
            .AnyAsync(r =>
                r.Id != role.Id
                && r.NormalizedName == normalizedName
                && r.TenantId == role.TenantId);

        return duplicate
            ? IdentityResult.Failed(manager.ErrorDescriber.DuplicateRoleName(role.Name ?? string.Empty))
            : IdentityResult.Success;
    }
}
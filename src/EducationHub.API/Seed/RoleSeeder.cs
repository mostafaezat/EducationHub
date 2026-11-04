using EducationHub.Application.Features.Tenancy.Services;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleManager = Microsoft.AspNetCore.Identity.RoleManager<EducationHub.Domain.Entities.Identity.ApplicationRole>;

namespace EducationHub.API.Seed;

/// <summary>
/// Idempotent seed data: the platform role and permission repairs for the seven per-tenant roles.
/// </summary>
/// <remarks>
/// <para>
/// Runs on every startup but does nothing on the second one. It is written as "ensure this
/// exists" rather than "insert", so restarting the app repeatedly is safe and a
/// half-completed previous run is repaired instead of compounded.
/// </para>
/// <para>
/// Tenants created at runtime already receive their roles from
/// <c>CreateTenantCommandHandler</c>; this seeder stays as the repair path for tenants
/// that existed before that provisioning existed. Both use
/// <see cref="TenantRoleProvisioner"/> so a role's permission set is defined exactly once.
/// </para>
/// </remarks>
public sealed class RoleSeeder(
    EducationHubDbContext context,
    RoleManager roleManager,
    TenantRoleProvisioner roleProvisioner,
    ILogger<RoleSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Platform scope: TenantId is left as Guid.Empty, which is what the tenant query
        // filter reads as "not a tenant role". Platform roles are deliberately created
        // outside the migration so a new permission can be granted to them without a
        // migration.
        await EnsurePlatformRoleAsync(cancellationToken).ConfigureAwait(false);

        var tenants = await context.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var tenant in tenants)
        {
            await EnsureTenantRolesAsync(tenant, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Seed complete: 1 platform role, {TenantCount} tenant(s) processed", tenants.Count);
    }

    private async Task EnsurePlatformRoleAsync(CancellationToken cancellationToken)
    {
        var existing = await roleManager
            .Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Name == RoleNames.PlatformAdmin, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.Empty,
                Name = RoleNames.PlatformAdmin,
                Description = "Platform-wide administrator with access to every tenant.",
                IsSystemRole = true,
                IsActive = true
            };

            var created = await roleManager.CreateAsync(existing).ConfigureAwait(false);

            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create the platform role: {Errors}",
                    string.Join("; ", created.Errors.Select(static e => e.Description)));

                return;
            }

            logger.LogInformation("Created platform role {RoleName}", RoleNames.PlatformAdmin);
        }

        // Every permission, including the platform ones a tenant role must never hold.
        // Same exact-reconcile routine tenant provisioning uses for the seven tenant roles.
        var reconciled = await roleProvisioner
            .ReconcilePermissionsAsync(existing, PermissionConstants.GetAll(), cancellationToken)
            .ConfigureAwait(false);

        if (!reconciled.Succeeded)
        {
            logger.LogError(
                "Could not reconcile permissions of the platform role: {Errors}",
                string.Join("; ", reconciled.Errors));
        }
    }

    private async Task EnsureTenantRolesAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        foreach (var roleName in RoleNames.TenantRoles)
        {
            // Keep the same log-and-continue behaviour as always: seeding repairs what it
            // can and reports the rest rather than aborting startup on a bad role.
            var result = await roleProvisioner
                .EnsureRoleAsync(roleName, tenant, cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                logger.LogError(
                    "Could not ensure role {RoleName} for tenant {TenantId}: {Errors}",
                    roleName,
                    tenant.Id,
                    string.Join("; ", result.Errors));
            }
        }
    }
}

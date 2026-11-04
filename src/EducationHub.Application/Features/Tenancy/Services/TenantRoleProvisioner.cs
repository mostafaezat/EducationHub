using System.Security.Claims;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RoleManager = Microsoft.AspNetCore.Identity.RoleManager<EducationHub.Domain.Entities.Identity.ApplicationRole>;

namespace EducationHub.Application.Features.Tenancy.Services;

/// <summary>
/// Outcome of ensuring one system role. Succeeded is false when a role could not be created
/// or had at least one permission claim it was asked for that failed to stick.
/// </summary>
public sealed record RoleProvisionResult(bool Succeeded, IReadOnlyList<string> Errors);

/// <summary>
/// Creates and keeps in step the seven built-in roles of a tenant and their permission sets.
/// </summary>
/// <remarks>
/// <para>
/// The single source of truth for what each system role may do. Both the seed path (<c>RoleSeeder</c>,
/// which repairs roles for pre-existing tenants on every startup) and the runtime path (tenant
/// provisioning, where <c>CreateTenantCommandHandler</c> creates the roles in the same transaction
/// as the tenant) call this, so the two can never drift apart.
/// </para>
/// <para>
/// Role writes here happen under platform scope - the caller is either the seeder or a platform
/// administrator - so the tenant filter does not hide or reject the tenant-scoped roles being
/// created. Each caller decides what an error means: the seeder logs and continues, provisioning
/// aborts the whole operation so a tenant is never left half-rolled-out.
/// </para>
/// </remarks>
public sealed class TenantRoleProvisioner(
    RoleManager roleManager,
    ILogger<TenantRoleProvisioner> logger)
{
    /// <summary>
    /// What each built-in role may do.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="PermissionConstants"/>, so adding a permission to the catalog
    /// makes it available to the role that should hold it without inventing a raw string.
    /// </remarks>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildDefaultRolePermissions()
    {
        static IReadOnlyList<string> Group(string area, params string[] actions)
            => [.. actions.Select(a => $"{area}.{a}")];

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RoleNames.TenantOwner] = [.. PermissionConstants.GetAll()],

            [RoleNames.TenantAdmin] =
            [
                .. Group("Users", "View", "Create", "Update", "Delete", "AssignRoles", "ResetPassword", "ToggleActive"),
                .. Group("Roles", "View", "Create", "Update", "Delete", "AssignPermissions"),
                .. Group("Permissions", "View"),
                .. Group("Tenants", "View"),
                .. Group("Branches", "View", "Create", "Update", "Delete"),
                .. Group("Settings", "View", "Update"),
                .. Group("AcademicYears", "View", "Create", "Update", "Delete"),
                .. Group("Grades", "View", "Create", "Update", "Delete"),
                .. Group("Subjects", "View", "Create", "Update", "Delete"),
                .. Group("Teachers", "View", "Create", "Update", "Delete"),
                .. Group("Students", "View", "Create", "Update", "Delete"),
                .. Group("Parents", "View", "Create", "Update", "Delete"),
                .. Group("Files", "View", "Upload", "Delete"),
                .. Group("Groups", "View", "Create", "Update", "Delete"),
                .. Group("Sessions", "View", "Manage"),
                .. Group("Attendance", "View", "Mark"),
                .. Group("Fees", "View", "Manage"),
                .. Group("Payments", "View", "Create", "Void"),
                .. Group("Finance", "View", "Manage"),
                .. Group("Reports", "View"),
                .. Group("Homework", "View", "Manage"),
                .. Group("Exams", "View", "Manage"),
                .. Group("AuditLogs", "View"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Teacher] =
            [
                .. Group("Users", "View"),
                .. Group("Branches", "View"),
                .. Group("Settings", "View"),
                .. Group("AcademicYears", "View"),
                .. Group("Grades", "View"),
                .. Group("Subjects", "View"),
                .. Group("Teachers", "View"),
                .. Group("Students", "View"),
                .. Group("Files", "View", "Upload"),
                .. Group("Groups", "View"),
                .. Group("Sessions", "View", "Manage"),
                .. Group("Attendance", "View", "Mark"),
                .. Group("Homework", "View", "Manage"),
                .. Group("Exams", "View", "Manage"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Assistant] =
            [
                .. Group("Users", "View"),
                .. Group("Branches", "View"),
                .. Group("Settings", "View"),
                .. Group("AcademicYears", "View"),
                .. Group("Grades", "View"),
                .. Group("Subjects", "View"),
                .. Group("Teachers", "View"),
                .. Group("Students", "View"),
                .. Group("Files", "View", "Upload"),
                .. Group("Groups", "View"),
                .. Group("Sessions", "View"),
                .. Group("Attendance", "View", "Mark"),
                .. Group("Homework", "View"),
                .. Group("Exams", "View"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Accountant] =
            [
                .. Group("Users", "View"),
                .. Group("Branches", "View"),
                .. Group("Settings", "View"),
                .. Group("AcademicYears", "View"),
                .. Group("Grades", "View"),
                .. Group("Teachers", "View"),
                .. Group("Students", "View"),
                .. Group("Parents", "View"),
                .. Group("Files", "View", "Upload"),
                .. Group("Groups", "View"),
                .. Group("Fees", "View", "Manage"),
                .. Group("Payments", "View", "Create", "Void"),
                .. Group("Finance", "View", "Manage"),
                .. Group("Reports", "View"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Parent] =
            [
                .. Group("Subjects", "View"),
                PermissionConstants.ParentsViewOwnChildren,
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Student] =
            [
                .. Group("Subjects", "View"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ]
        };
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions =
        BuildDefaultRolePermissions();

    /// <summary>
    /// Finds this tenant's copy of a system role, or null when the tenant does not have it yet.
    /// </summary>
    /// <remarks>
    /// Role names repeat across tenants, so a lookup must always name its tenant: Identity's
    /// FindByNameAsync has no tenant and would return (or throw on) whichever match it sees
    /// first. Scoped by (TenantId, NormalizedName), the unique index the schema enforces.
    /// </remarks>
    public async Task<ApplicationRole?> FindRoleAsync(
        string roleName,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleName);

        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = roleManager.NormalizeKey(roleName);

        return await roleManager.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.NormalizedName == normalizedName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures one system role exists for the tenant and carries exactly its default permissions.
    /// </summary>
    /// <remarks>
    /// Idempotent: the second run finds the role and reconcile is a no-op when nothing changed.
    /// </remarks>
    public async Task<RoleProvisionResult> EnsureRoleAsync(
        string roleName,
        Tenant tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        ArgumentNullException.ThrowIfNull(tenant);

        cancellationToken.ThrowIfCancellationRequested();

        // Roles are tenant-scoped: ApplicationRole carries TenantId and the unique index is
        // (TenantId, NormalizedName). The Identity store's FindByNameAsync has no idea, so a
        // lookup by name alone would hand tenant B tenant A's TenantAdmin role (or trip the
        // unique index for real). Query the store queryable with the tenant id instead.
        var role = await FindRoleAsync(roleName, tenant.Id, cancellationToken).ConfigureAwait(false);

        var errors = new List<string>();

        if (role is null)
        {
            role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                Name = roleName,
                Description = $"System role '{roleName}' for {tenant.Name}.",
                IsSystemRole = true,
                IsActive = true
            };

            var created = await roleManager.CreateAsync(role).ConfigureAwait(false);

            if (!created.Succeeded)
            {
                errors.AddRange(created.Errors.Select(static e => e.Description));

                return new RoleProvisionResult(false, errors);
            }

            logger.LogInformation(
                "Created role {RoleName} for tenant {TenantId}",
                roleName,
                tenant.Id);
        }

        var desired = DefaultRolePermissions.TryGetValue(roleName, out var permissions)
            ? permissions
            : [];

        var reconcile = await ReconcilePermissionsAsync(role, desired, cancellationToken).ConfigureAwait(false);

        return reconcile.Succeeded
            ? new RoleProvisionResult(true, [])
            : new RoleProvisionResult(false, reconcile.Errors);
    }

    /// <summary>
    /// Makes the role's permission claims match <paramref name="desired"/> exactly.
    /// </summary>
    /// <remarks>
    /// A replace, not an append. Appending would make it impossible to revoke a permission,
    /// and would make the seeder's effect depend on how many times the app had been started.
    /// </remarks>
    public async Task<RoleProvisionResult> ReconcilePermissionsAsync(
        ApplicationRole role,
        IReadOnlyList<string> desired,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(desired);

        cancellationToken.ThrowIfCancellationRequested();

        var existing = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

        var permissionClaims = existing
            .Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType)
            .ToArray();

        var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();

        foreach (var claim in permissionClaims.Where(c => !desiredSet.Contains(c.Value)))
        {
            var removal = await roleManager.RemoveClaimAsync(role, claim).ConfigureAwait(false);

            if (!removal.Succeeded)
            {
                errors.AddRange(removal.Errors.Select(static e => e.Description));
            }
        }

        var present = permissionClaims
            .Select(static c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in desiredSet.Where(p => !present.Contains(p)))
        {
            var addition = await roleManager
                .AddClaimAsync(role, new Claim(EducationHubClaimTypes.PermissionClaimType, permission))
                .ConfigureAwait(false);

            if (!addition.Succeeded)
            {
                errors.AddRange(addition.Errors.Select(static e => e.Description));
            }
        }

        return new RoleProvisionResult(errors.Count == 0, errors);
    }
}
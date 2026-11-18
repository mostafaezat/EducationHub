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
    /// Role-claim type marking which version of the built-in permission sets a system role was
    /// last reconciled to. Not a permission: every permission reader filters on
    /// <see cref="EducationHubClaimTypes.PermissionClaimType"/>, so this claim never reaches a token.
    /// </summary>
    public const string DefaultsVersionClaimType = "SystemPermissionsVersion";

    /// <summary>
    /// The first stamped version of the built-in permission sets. Roles stamped with it hold
    /// (or held, before a tenant customised them) the defaults as they were then; everything
    /// granted later is listed in <see cref="AddedInVersion"/>.
    /// </summary>
    public const string BaselineVersion = "2026-11-18";

    /// <summary>
    /// Version of <see cref="BuildDefaultRolePermissions"/>: always the last entry of
    /// <see cref="AddedInVersion"/>. To grant a built-in role a new permission, add it to the
    /// role in <see cref="BuildDefaultRolePermissions"/> (for new tenants), append a new
    /// version entry listing it (for existing tenants) and set this constant to that version.
    /// </summary>
    public const string DefaultsVersion = "2026-11-18.2";

    /// <summary>
    /// What each version added to which built-in role, oldest first. Existing roles are upgraded
    /// additively: only the entries newer than a role's stamp are applied, and only by adding.
    /// Nothing is ever removed, and a permission granted by an older version that a tenant has
    /// since removed is never granted again.
    /// </summary>
    internal static readonly IReadOnlyList<(string Version, IReadOnlyDictionary<string, IReadOnlyList<string>> Added)> AddedInVersion =
    [
        ("2026-11-18.2", new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RoleNames.TenantOwner] = [PermissionConstants.RegistrationRequestsView, PermissionConstants.RegistrationRequestsManage],
            [RoleNames.TenantAdmin] = [PermissionConstants.RegistrationRequestsView, PermissionConstants.RegistrationRequestsManage],
            [RoleNames.Assistant] = [PermissionConstants.RegistrationRequestsView, PermissionConstants.RegistrationRequestsManage]
        })
    ];

    /// <summary>The full default permission set of a built-in role (empty for an unknown name).</summary>
    public static IReadOnlyList<string> DefaultPermissionsFor(string roleName)
        => DefaultRolePermissions.TryGetValue(roleName, out var permissions) ? permissions : [];

    /// <summary>
    /// The permissions to add to a role stamped <paramref name="stampedVersion"/>, or null when
    /// the stamp is not a version this build knows (it was written by a newer build), in which
    /// case the role must be left alone.
    /// </summary>
    public static IReadOnlyList<string>? PermissionsAddedSince(string roleName, string stampedVersion)
    {
        var start = -1;

        if (string.Equals(stampedVersion, BaselineVersion, StringComparison.Ordinal))
        {
            start = 0;
        }

        for (var i = 0; start < 0 && i < AddedInVersion.Count; i++)
        {
            if (string.Equals(AddedInVersion[i].Version, stampedVersion, StringComparison.Ordinal))
            {
                start = i + 1;
            }
        }

        if (start < 0)
        {
            return null;
        }

        return
        [
            .. AddedInVersion
                .Skip(start)
                .SelectMany(entry => entry.Added.TryGetValue(roleName, out var added) ? added : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

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

        IReadOnlyList<string> familyRead =
        [
            PermissionConstants.GroupsView,
            PermissionConstants.SessionsView,
            PermissionConstants.AttendanceView,
            PermissionConstants.HomeworkView,
            PermissionConstants.ExamsView
        ];

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Everything inside the tenant; never the platform-only areas (tenants, plans,
            // platform settings), which belong to the platform administrator alone.
            [RoleNames.TenantOwner] = [.. PermissionConstants.GetTenantPermissions()],

            [RoleNames.TenantAdmin] =
            [
                .. Group("Users", "View", "Create", "Update", "Delete", "AssignRoles", "ResetPassword", "ToggleActive"),
                .. Group("Roles", "View", "Create", "Update", "Delete", "AssignPermissions"),
                .. Group("Permissions", "View"),
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
                .. Group("RegistrationRequests", "View", "Manage"),
                .. Group("AuditLogs", "View"),
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            // Teachers see their own groups only (row-level DataScope); the user directory and
            // the centre's settings are management data, so they are not part of the role.
            [RoleNames.Teacher] =
            [
                .. Group("Branches", "View"),
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
                .. Group("Branches", "View"),
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
                .. Group("RegistrationRequests", "View", "Manage"),
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

            // Parents and students read their own family's teaching data. The rows they get are
            // narrowed by DataScope to their own children / themself, and the role holds no
            // write permission at all.
            [RoleNames.Parent] =
            [
                .. Group("Subjects", "View"),
                .. familyRead,
                PermissionConstants.ParentsViewOwnChildren,
                PermissionConstants.DashboardView,
                PermissionConstants.Login,
                PermissionConstants.ChangePassword
            ],

            [RoleNames.Student] =
            [
                .. Group("Subjects", "View"),
                .. familyRead,
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

        return await ApplyDefaultsAsync(role, roleName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The startup repair path: creates a missing system role (with the full defaults), and
    /// upgrades an existing one additively when its stamp is older than <see cref="DefaultsVersion"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tenant may adjust its own system roles (remove a permission, add an extra one). The
    /// upgrade therefore never removes anything and only grants what the versions newer than the
    /// role's stamp added (<see cref="AddedInVersion"/>), so a permission the tenant deliberately
    /// removed is not granted back. A role already at the current version is not touched.
    /// </para>
    /// <para>
    /// A role with no stamp at all predates versioning, when every startup reset roles to the
    /// defaults, so it cannot hold a deliberate removal: it gets whatever defaults it is missing.
    /// A stamp this build does not recognise came from a newer build and is left alone.
    /// </para>
    /// </remarks>
    public async Task<RoleProvisionResult> EnsureRoleKeepingCustomizationsAsync(
        string roleName,
        Tenant tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleName);
        ArgumentNullException.ThrowIfNull(tenant);

        var role = await FindRoleAsync(roleName, tenant.Id, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            return await EnsureRoleAsync(roleName, tenant, cancellationToken).ConfigureAwait(false);
        }

        var claims = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

        var stamp = claims.FirstOrDefault(static c => c.Type == DefaultsVersionClaimType)?.Value;

        if (string.Equals(stamp, DefaultsVersion, StringComparison.Ordinal))
        {
            return new RoleProvisionResult(true, []);
        }

        IReadOnlyList<string> toAdd;

        if (stamp is null)
        {
            toAdd = DefaultPermissionsFor(roleName);
        }
        else
        {
            var added = PermissionsAddedSince(roleName, stamp);

            if (added is null)
            {
                logger.LogWarning(
                    "Role {RoleName} of tenant {TenantId} is stamped with unknown defaults version {Version}; left unchanged",
                    roleName,
                    tenant.Id,
                    stamp);

                return new RoleProvisionResult(true, []);
            }

            toAdd = added;
        }

        var granted = await AddMissingPermissionsAsync(role, claims, toAdd, cancellationToken).ConfigureAwait(false);

        if (!granted.Succeeded)
        {
            return granted;
        }

        if (toAdd.Count > 0)
        {
            logger.LogInformation(
                "Upgraded role {RoleName} of tenant {TenantId} from defaults version {From} to {To}",
                roleName,
                tenant.Id,
                stamp ?? "(none)",
                DefaultsVersion);
        }

        var stamped = await StampVersionAsync(role).ConfigureAwait(false);

        return stamped.Succeeded
            ? new RoleProvisionResult(true, [])
            : new RoleProvisionResult(false, [.. stamped.Errors.Select(static e => e.Description)]);
    }

    /// <summary>Grants each of <paramref name="permissions"/> the role does not hold yet; never removes.</summary>
    private async Task<RoleProvisionResult> AddMissingPermissionsAsync(
        ApplicationRole role,
        IList<Claim> existingClaims,
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var present = existingClaims
            .Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType)
            .Select(static c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();

        foreach (var permission in permissions.Where(p => !present.Contains(p)))
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

    /// <summary>Reconciles the role to its default permission set and stamps the current version.</summary>
    private async Task<RoleProvisionResult> ApplyDefaultsAsync(
        ApplicationRole role,
        string roleName,
        CancellationToken cancellationToken)
    {
        var desired = DefaultPermissionsFor(roleName);

        var reconcile = await ReconcilePermissionsAsync(role, desired, cancellationToken).ConfigureAwait(false);

        if (!reconcile.Succeeded)
        {
            return new RoleProvisionResult(false, reconcile.Errors);
        }

        var stamped = await StampVersionAsync(role).ConfigureAwait(false);

        return stamped.Succeeded
            ? new RoleProvisionResult(true, [])
            : new RoleProvisionResult(false, [.. stamped.Errors.Select(static e => e.Description)]);
    }

    private async Task<IdentityResult> StampVersionAsync(ApplicationRole role)
    {
        var claims = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

        foreach (var old in claims.Where(static c => c.Type == DefaultsVersionClaimType && c.Value != DefaultsVersion))
        {
            var removed = await roleManager.RemoveClaimAsync(role, old).ConfigureAwait(false);

            if (!removed.Succeeded)
            {
                return removed;
            }
        }

        if (claims.Any(static c => c.Type == DefaultsVersionClaimType && c.Value == DefaultsVersion))
        {
            return IdentityResult.Success;
        }

        return await roleManager.AddClaimAsync(role, new Claim(DefaultsVersionClaimType, DefaultsVersion)).ConfigureAwait(false);
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
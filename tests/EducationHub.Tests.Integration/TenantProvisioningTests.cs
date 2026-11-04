using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Application.Features.Tenancy.Services;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Persistence.Repositories;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoleManager = Microsoft.AspNetCore.Identity.RoleManager<EducationHub.Domain.Entities.Identity.ApplicationRole>;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Tenant provisioning: the tenant, its settings, its seven system roles and its first
/// administrator must all appear together, or not at all.
/// </summary>
/// <remarks>
/// Driven end-to-end through <see cref="CreateTenantCommandHandler"/> with real repositories,
/// real Identity stores and a real SQL Server, because the transactional guarantee is the
/// point of the test. A rolled-back admin failure leaving a tenant behind would be a
/// half-provisioned tenant - exactly what the explicit transaction exists to prevent.
/// </remarks>
public sealed class TenantProvisioningTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTime FixedNow = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Provisioning_creates_tenant_settings_roles_and_admin_together()
    {
        var slug = $"academy-{Guid.NewGuid():N}"[..20];
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";

        var result = await ProvisionAsync(
            slug,
            adminEmail,
            password: "Adm1nPassw0rd");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(adminEmail, result.Data?.AdminEmail, ignoreCase: true);

        await using var read = fixture.CreateContext(new PlatformTenantContext());

        var tenantId = result.Data!.Id;

        var tenant = await read.Tenants
            .IgnoreQueryFilters()
            .FirstAsync(t => t.Id == tenantId);
        Assert.Equal(slug, tenant.Slug);

        var settings = await read.TenantSettings
            .IgnoreQueryFilters()
            .FirstAsync(s => s.TenantId == tenantId);
        Assert.Equal(tenant.Currency, settings.CurrencySymbol);

        var roleManager = fixture.CreateRoleManager(read);

        var roles = await roleManager.Roles
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync();

        Assert.Equal(RoleNames.TenantRoles.Count, roles.Count);
        Assert.Equal(
            RoleNames.TenantRoles.OrderBy(static r => r, StringComparer.OrdinalIgnoreCase),
            roles.Select(static r => r.Name).OrderBy(static r => r, StringComparer.OrdinalIgnoreCase));

        // The permission claims that make the role actually usable.
        var tenantAdmin = roles.Single(r => r.Name == RoleNames.TenantAdmin);

        var claims = await roleManager.GetClaimsAsync(tenantAdmin);

        Assert.Contains(claims, c => c.Type == "Permission" && c.Value == PermissionConstants.UsersCreate);
        Assert.DoesNotContain(claims, c => c.Type == "Permission" && c.Value == PermissionConstants.PlatformManageTenants);

        var userManager = fixture.CreateUserManager(read);

        var admin = await userManager
            .Users
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Email == adminEmail);

        Assert.Equal(tenantId, admin.TenantId);
        Assert.True(admin.EmailConfirmed);
        Assert.True(admin.IsActive);

        var assigned = await userManager.GetRolesAsync(admin);

        Assert.Contains(RoleNames.TenantAdmin, assigned);
    }

    [Fact]
    public async Task Provisioning_rejects_an_email_already_in_use_across_tenants()
    {
        var firstSlug = $"first-{Guid.NewGuid():N}"[..20];
        var secondSlug = $"second-{Guid.NewGuid():N}"[..20];
        var sharedEmail = $"shared-{Guid.NewGuid():N}@example.test";

        var first = await ProvisionAsync(firstSlug, sharedEmail, password: "Adm1nPassw0rd");

        Assert.True(first.Succeeded);

        var second = await ProvisionAsync(secondSlug, sharedEmail, password: "Adm1nPassw0rd");

        Assert.True(second.IsFailure, "The second provisioning should be refused.");
        Assert.Contains(second.Errors, e => e.Code == "Tenants.AdminEmailTaken");

        await using var read = fixture.CreateContext(new PlatformTenantContext());

        Assert.DoesNotContain(
            await read.Tenants.IgnoreQueryFilters().Select(t => t.Slug).ToListAsync(),
            slug => slug == secondSlug);
    }

    /// <summary>
    /// A rejected administrator account must roll back the tenant and its roles: a tenant
    /// that exists but cannot be administered is worse than no tenant at all.
    /// </summary>
    [Fact]
    public async Task Provisioning_failure_rolls_back_tenant_and_roles()
    {
        var slug = $"rollback-{Guid.NewGuid():N}"[..20];
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";

        // Six characters fails Identity's own password policy (length >= 8), which the
        // handler surfaces as a failed CreateAsync after the tenant and roles are staged.
        // Because roles are tenant-scoped and this slug is fresh, CreateAsync is the first
        // write to fail - every role staged before it must then be undone.
        await using (var probe = fixture.CreateContext(new PlatformTenantContext()))
        {
            var rolesBefore = await probe.Roles.IgnoreQueryFilters().CountAsync();

            var result = await ProvisionAsync(slug, adminEmail, password: "short1");

            Assert.True(result.IsFailure, "The weak password must be refused.");

            var rolesAfter = await probe.Roles.IgnoreQueryFilters().CountAsync();
            var tenantPresent = await probe.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug);
            var adminPresent = await probe.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == adminEmail);

            Assert.Equal(rolesBefore, rolesAfter);
            Assert.False(tenantPresent, "A rolled-back tenant must not be visible.");
            Assert.False(adminPresent, "A rolled-back administrator must not be visible.");
        }
    }

    private async Task<Result<TenantDto>> ProvisionAsync(
        string slug,
        string adminEmail,
        string password)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(adminEmail);
        ArgumentNullException.ThrowIfNull(password);

        await using var context = fixture.CreateContext(new PlatformTenantContext());

        var roleManager = fixture.CreateRoleManager(context);
        var userManager = fixture.CreateUserManager(context);

        var handler = new CreateTenantCommandHandler(
            new EfRepository<Tenant>(context),
            new EfRepository<TenantSettings>(context),
            new EfRepository<IdentityUserRole<Guid>>(context),
            new TenantRoleProvisioner(roleManager, NullLogger<TenantRoleProvisioner>.Instance),
            userManager,
            new PlatformTenantContext(),
            new FixedDateTimeService(),
            new UnitOfWork(context));

        var result = await handler.Handle(
            new CreateTenantCommand(new CreateTenantRequest(
                Slug: slug,
                Name: "Provisioned Academy",
                NameAr: null,
                Type: TenantType.EducationCenter,
                Email: null,
                Phone: null,
                Address: null,
                AddressAr: null,
                City: "Cairo",
                Country: null,
                Currency: "EGP",
                TimeZone: "Africa/Cairo",
                DefaultLanguage: "ar",
                Description: null,
                DescriptionAr: null,
                AdminFullName: "Provisioned Admin",
                AdminEmail: adminEmail,
                AdminPassword: password)),
            CancellationToken.None);

        return result;
    }

    /// <summary>A frozen clock so Provisioning_rolls_back... can predict nothing.</summary>
    private sealed class FixedDateTimeService : IDateTimeService
    {
        public DateTime UtcNow => FixedNow;

        public DateOnly UtcToday => DateOnly.FromDateTime(FixedNow);

        public DateTime GetTenantNow() => FixedNow;

        public DateOnly GetTenantToday() => DateOnly.FromDateTime(FixedNow);

        public DateTime ToTenantTime(DateTime utcDateTime) => utcDateTime;
    }
}
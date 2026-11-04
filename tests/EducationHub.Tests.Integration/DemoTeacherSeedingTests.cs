using System.Net;
using EducationHub.API.Seed;
using EducationHub.Application.Features.Tenancy.Services;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Services;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The demo-teacher seed's contract (mission Phase 1b): add-only, idempotent, scoped to
/// its tenant, role-assigned through the tenant's own Teacher row, and a logged no-op
/// without password, tenant or role.
/// </summary>
/// <remarks>
/// Every test owns a tenant with a unique slug AND unique e-mail addresses: the tenant
/// slug is platform-unique and Identity e-mail is globally unique, the collection shares
/// one database, and fixed values would make the tests collide with each other rather
/// than with the seeder. The production values are pinned by
/// <see cref="DemoTeacherSeeder.DemoTeacherEmails"/> and the parameterless overload.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DemoTeacherSeedingTests(ApiTestFixture fixture)
{
    private const string SeededPassword = "Demo#Teacher123";
    private const string PreExistingPassword = "Existing#Passw0rd1";

    [Fact]
    public async Task Seed_creates_the_demo_teachers_with_the_tenants_teacher_role_and_they_can_sign_in()
    {
        var emails = UniqueEmails();
        var (slug, tenantId) = await CreateTenantAsync("demo-teach", provisionRoles: true);

        await RunSeederAsync(slug, emails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var users = await db.Users
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.Email)
                .ToListAsync();

            Assert.Equal(2, users.Count);

            var first = Assert.Single(users, u => u.Email == emails[0]);
            var second = Assert.Single(users, u => u.Email == emails[1]);

            // Display names are numbered by position in the e-mail list.
            Assert.Equal("Demo Teacher 1", first.FullName);
            Assert.Equal("Demo Teacher 2", second.FullName);
            Assert.True(first.IsActive);
            Assert.True(first.EmailConfirmed);
            Assert.True(second.IsActive);
            Assert.True(second.EmailConfirmed);

            // Membership points at THIS tenant's Teacher row, not any other tenant's.
            var teacherRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Teacher);
            var memberIds = await db.UserRoles
                .Where(ur => ur.RoleId == teacherRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync();

            Assert.Equal(2, memberIds.Count);
            Assert.Contains(first.Id, memberIds);
            Assert.Contains(second.Id, memberIds);
        });

        using (var scope = fixture.CreateScope())
        {
            // Platform override: a bare scope keeps the tenant query filter, which would
            // hide the demo tenant's users from FindByEmailAsync.
            using var _ = PlatformScopeOverride.Begin();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var email in emails)
            {
                var user = await userManager.FindByEmailAsync(email);
                Assert.NotNull(user);
                Assert.True(
                    await userManager.CheckPasswordAsync(user, SeededPassword),
                    $"The seeded password must verify for {email}.");
            }
        }

        // End-to-end: the seeded credential signs in and carries the Teacher role.
        var session = await ApiScenarios.LoginAsync(fixture.Client, emails[0], SeededPassword);

        using var me = await fixture.Client.GetAsync("/api/auth/me", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var roles = (await me.ReadJsonAsync())
            .GetProperty("data")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(static r => r.GetString())
            .ToArray();

        Assert.Contains(RoleNames.Teacher, roles);

        // The documented production addresses stay what the docs say they are.
        Assert.Equal(
            ["teacher.one@educationhub.local", "teacher.two@educationhub.local"],
            DemoTeacherSeeder.DemoTeacherEmails);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_never_modifies_a_pre_existing_account()
    {
        var emails = UniqueEmails();
        var (slug, tenantId) = await CreateTenantAsync("demo-idem", provisionRoles: true);

        // An account that already owns the first seeded e-mail: custom display name, its
        // own password, and no roles. The seeder must leave every one of those alone.
        var preExisting = await CreateUserAsync(
            tenantId, emails[0], "Pre Existing Teacher", PreExistingPassword, assignTeacherRole: false);

        await RunSeederAsync(slug, emails);
        await RunSeederAsync(slug, emails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            Assert.Equal(2, await db.Users.CountAsync(u => u.TenantId == tenantId));

            var untouched = await db.Users.SingleAsync(u => u.Id == preExisting.Id);
            Assert.Equal("Pre Existing Teacher", untouched.FullName);

            // Still role-less: create-if-absent must not adopt existing accounts.
            Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == preExisting.Id));

            // The second address was created (once) and holds the Teacher role.
            var created = await db.Users.SingleAsync(u => u.TenantId == tenantId && u.Email == emails[1]);
            var teacherRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Teacher);
            Assert.True(await db.UserRoles.AnyAsync(
                ur => ur.UserId == created.Id && ur.RoleId == teacherRole.Id));
        });

        using var scope = fixture.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // The original password still verifies, and the seeder's password does not:
        // proof of no reset and no rotation across both runs.
        Assert.True(await userManager.CheckPasswordAsync(preExisting, PreExistingPassword));
        Assert.False(await userManager.CheckPasswordAsync(preExisting, SeededPassword));
    }

    [Fact]
    public async Task Seed_never_touches_another_tenant()
    {
        var emails = UniqueEmails();
        var (demoSlug, demoId) = await CreateTenantAsync("demo-scope", provisionRoles: true);
        var (_, controlId) = await CreateTenantAsync("demo-ctrl", provisionRoles: true);

        var controlUser = await CreateUserAsync(
            controlId, UniqueEmail(), "Control Staff", PreExistingPassword, assignTeacherRole: true);

        await RunSeederAsync(demoSlug, emails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // The control tenant keeps exactly what it had: its own single user and role
            // membership, nothing added by the seed run.
            Assert.Equal(1, await db.Users.CountAsync(u => u.TenantId == controlId));
            var unchanged = await db.Users.SingleAsync(u => u.Id == controlUser.Id);
            Assert.Equal("Control Staff", unchanged.FullName);

            var controlRole = await db.Roles.SingleAsync(
                r => r.TenantId == controlId && r.Name == RoleNames.Teacher);
            var controlMembers = await db.UserRoles
                .Where(ur => ur.RoleId == controlRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync();

            Assert.Equal([controlUser.Id], controlMembers);

            // The demo tenant was seeded.
            Assert.Equal(2, await db.Users.CountAsync(u => u.TenantId == demoId));
        });
    }

    [Fact]
    public async Task Seed_skips_an_e_mail_that_already_belongs_to_another_tenant()
    {
        var foreignEmail = UniqueEmail();
        var (_, controlId) = await CreateTenantAsync("demo-own", provisionRoles: true);
        var (demoSlug, demoId) = await CreateTenantAsync("demo-thief", provisionRoles: true);

        var foreignUser = await CreateUserAsync(
            controlId, foreignEmail, "Foreign Owner", PreExistingPassword, assignTeacherRole: true);

        await RunSeederAsync(demoSlug, [foreignEmail]);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // Nothing created in the target tenant, and the owning account is untouched.
            Assert.Equal(0, await db.Users.CountAsync(u => u.TenantId == demoId));

            var owner = await db.Users.SingleAsync(u => u.Id == foreignUser.Id);
            Assert.Equal("Foreign Owner", owner.FullName);
            Assert.Equal(controlId, owner.TenantId);

            var ownerRole = await db.Roles.SingleAsync(
                r => r.TenantId == controlId && r.Name == RoleNames.Teacher);
            Assert.True(await db.UserRoles.AnyAsync(
                ur => ur.UserId == owner.Id && ur.RoleId == ownerRole.Id));
        });

        using var scope = fixture.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await userManager.CheckPasswordAsync(foreignUser, PreExistingPassword));
    }

    [Fact]
    public async Task Seed_without_a_matching_tenant_changes_nothing()
    {
        var emails = UniqueEmails();
        var missingSlug = $"missing-{Guid.NewGuid():N}";

        var before = await CountUsersAsync();

        await RunSeederAsync(missingSlug, emails);

        var after = await CountUsersAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Seed_without_a_password_is_a_logged_no_op()
    {
        var emails = UniqueEmails();
        var (slug, _) = await CreateTenantAsync("demo-nopwd", provisionRoles: true);

        var before = await CountUsersAsync();

        await RunSeederAsync(slug, emails, password: null);

        var after = await CountUsersAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Seed_without_the_teachers_role_row_is_a_logged_no_op()
    {
        var emails = UniqueEmails();

        // Tenant exists but its per-tenant roles were never provisioned: the seeder must
        // skip (role repair belongs to RoleSeeder) instead of throwing out of AddToRoleAsync.
        var (slug, _) = await CreateTenantAsync("demo-norole", provisionRoles: false);

        var before = await CountUsersAsync();

        await RunSeederAsync(slug, emails);

        var after = await CountUsersAsync();

        Assert.Equal(before, after);
    }

    private static string[] UniqueEmails()
    =>
    [
        $"teacher.one-{Guid.NewGuid():N}@educationhub.local",
        $"teacher.two-{Guid.NewGuid():N}@educationhub.local"
    ];

    private static string UniqueEmail()
        => $"staff-{Guid.NewGuid():N}@educationhub.local";

    private async Task RunSeederAsync(
        string slug,
        IReadOnlyList<string> emails,
        string? password = SeededPassword)
    {
        var previous = Environment.GetEnvironmentVariable(
            DemoTeacherSeeder.PasswordEnvironmentVariable);

        // password: null unsets the variable, which is how the missing-password contract
        // is exercised. Same set/restore pattern SeedingTests uses for the admin variables.
        Environment.SetEnvironmentVariable(
            DemoTeacherSeeder.PasswordEnvironmentVariable, password);

        try
        {
            // Await inside the usings: disposing the scope while the seeder is still
            // running would close its DbContext connection mid-query.
            using var scope = fixture.CreateScope();
            using var _ = PlatformScopeOverride.Begin();

            await scope.ServiceProvider
                .GetRequiredService<DemoTeacherSeeder>()
                .SeedAsync(slug, emails);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                DemoTeacherSeeder.PasswordEnvironmentVariable, previous);
        }
    }

    private async Task<int> CountUsersAsync()
    {
        var count = 0;

        await fixture.WithPlatformScopeAsync(async db =>
        {
            count = await db.Users.IgnoreQueryFilters().CountAsync();
        });

        return count;
    }

    private async Task<(string Slug, Guid TenantId)> CreateTenantAsync(
        string prefix,
        bool provisionRoles)
    {
        var slug = $"{prefix}-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();

        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();

        var db = scope.ServiceProvider.GetRequiredService<EducationHubDbContext>();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Slug = slug,
            Name = $"Seeded Academy {prefix}",
            Type = TenantType.EducationCenter,
            Status = TenantStatus.Active,
            Currency = "EGP",
            TimeZone = "Africa/Cairo",
            DefaultLanguage = "ar",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            SubscriptionExpiryDate = DateTime.UtcNow.AddYears(1)
        });

        await db.SaveChangesAsync();

        if (provisionRoles)
        {
            // Same component the production path uses (tenant provisioning and RoleSeeder),
            // so the tenant's Teacher row is exactly what a runtime-created tenant has.
            var provisioner = scope.ServiceProvider.GetRequiredService<TenantRoleProvisioner>();
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);

            foreach (var roleName in RoleNames.TenantRoles)
            {
                var result = await provisioner.EnsureRoleAsync(roleName, tenant, CancellationToken.None);
                Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            }
        }

        return (slug, tenantId);
    }

    private async Task<ApplicationUser> CreateUserAsync(
        Guid tenantId,
        string email,
        string fullName,
        string password,
        bool assignTeacherRole)
    {
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = fullName,
            TenantId = tenantId,
            PreferredLanguage = SupportedLanguages.Default,
            IsActive = true,
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        if (assignTeacherRole)
        {
            var assigned = await userManager.AddToRoleAsync(user, RoleNames.Teacher);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        }

        return user;
    }
}

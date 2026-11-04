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
/// The demo student/parent seed's contract (mission Phase 1c): add-only, idempotent,
/// scoped to its tenant, each role group gated by its own password variable and assigned
/// through that tenant's own role rows, and a logged no-op without password, tenant or role.
/// </summary>
/// <remarks>
/// Every test owns a tenant with a unique slug AND unique e-mail addresses: the tenant
/// slug is platform-unique and Identity e-mail is globally unique, the collection shares
/// one database, and fixed values would make the tests collide with each other rather
/// than with the seeder. The production values are pinned by
/// <see cref="DemoStudentParentSeeder.DemoStudentEmails"/> /
/// <see cref="DemoStudentParentSeeder.DemoParentEmails"/> and the parameterless overload.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class DemoStudentParentSeedingTests(ApiTestFixture fixture)
{
    private const string SeededPassword = "Demo#Family123";
    private const string PreExistingPassword = "Existing#Passw0rd1";

    [Fact]
    public async Task Seed_creates_students_and_parents_with_their_tenant_roles_and_both_can_sign_in()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (slug, tenantId) = await CreateTenantAsync("demo-fam");

        await RunSeederAsync(slug, studentEmails, parentEmails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var users = await db.Users
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.Email)
                .ToListAsync();

            Assert.Equal(4, users.Count);

            var student1 = Assert.Single(users, u => u.Email == studentEmails[0]);
            var student2 = Assert.Single(users, u => u.Email == studentEmails[1]);
            var parent1 = Assert.Single(users, u => u.Email == parentEmails[0]);
            var parent2 = Assert.Single(users, u => u.Email == parentEmails[1]);

            // Display names are numbered per group.
            Assert.Equal("Demo Student 1", student1.FullName);
            Assert.Equal("Demo Student 2", student2.FullName);
            Assert.Equal("Demo Parent 1", parent1.FullName);
            Assert.Equal("Demo Parent 2", parent2.FullName);

            foreach (var user in users)
            {
                Assert.True(user.IsActive);
                Assert.True(user.EmailConfirmed);
            }

            // Each membership points at THIS tenant's own role row, not any other's.
            var studentRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Student);
            var parentRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Parent);

            var studentMembers = await db.UserRoles
                .Where(ur => ur.RoleId == studentRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync();
            var parentMembers = await db.UserRoles
                .Where(ur => ur.RoleId == parentRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync();

            // Exactly two members each, and they are the right two (no cross-group rows).
            Assert.Equal(2, studentMembers.Count);
            Assert.Contains(student1.Id, studentMembers);
            Assert.Contains(student2.Id, studentMembers);
            Assert.Equal(2, parentMembers.Count);
            Assert.Contains(parent1.Id, parentMembers);
            Assert.Contains(parent2.Id, parentMembers);
        });

        using (var scope = fixture.CreateScope())
        using (PlatformScopeOverride.Begin())
        {
            // Platform override: a bare scope keeps the tenant query filter, which would
            // hide the demo tenant's users from FindByEmailAsync.
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var email in studentEmails.Concat(parentEmails))
            {
                var user = await userManager.FindByEmailAsync(email);
                Assert.NotNull(user);
                Assert.True(
                    await userManager.CheckPasswordAsync(user, SeededPassword),
                    $"The seeded password must verify for {email}.");
            }
        }

        // End-to-end: each group's credential signs in and carries that group's role.
        await AssertCanSignInAsync(studentEmails[0], RoleNames.Student);
        await AssertCanSignInAsync(parentEmails[0], RoleNames.Parent);

        // The documented production addresses stay what the docs say they are.
        Assert.Equal(
            ["student.one@educationhub.local", "student.two@educationhub.local"],
            DemoStudentParentSeeder.DemoStudentEmails);
        Assert.Equal(
            ["parent.one@educationhub.local", "parent.two@educationhub.local"],
            DemoStudentParentSeeder.DemoParentEmails);
    }

    [Fact]
    public async Task Seed_is_idempotent_and_never_modifies_pre_existing_accounts()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (slug, tenantId) = await CreateTenantAsync("demo-fidem");

        // Accounts that already own the first seeded address of each group: custom display
        // names, their own passwords, and no roles. The seeder must leave all of it alone.
        var preStudent = await CreateUserAsync(
            tenantId, studentEmails[0], "Pre Existing Student", PreExistingPassword, roleToAssign: null);
        var preParent = await CreateUserAsync(
            tenantId, parentEmails[0], "Pre Existing Parent", PreExistingPassword, roleToAssign: null);

        await RunSeederAsync(slug, studentEmails, parentEmails);
        await RunSeederAsync(slug, studentEmails, parentEmails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            Assert.Equal(4, await db.Users.CountAsync(u => u.TenantId == tenantId));

            var untouchedStudent = await db.Users.SingleAsync(u => u.Id == preStudent.Id);
            var untouchedParent = await db.Users.SingleAsync(u => u.Id == preParent.Id);
            Assert.Equal("Pre Existing Student", untouchedStudent.FullName);
            Assert.Equal("Pre Existing Parent", untouchedParent.FullName);

            // Still role-less: create-if-absent must not adopt existing accounts.
            Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == preStudent.Id));
            Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == preParent.Id));

            // The second address of each group was created (once) and holds its role.
            var createdStudent = await db.Users
                .SingleAsync(u => u.TenantId == tenantId && u.Email == studentEmails[1]);
            var createdParent = await db.Users
                .SingleAsync(u => u.TenantId == tenantId && u.Email == parentEmails[1]);

            var studentRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Student);
            var parentRole = await db.Roles.SingleAsync(
                r => r.TenantId == tenantId && r.Name == RoleNames.Parent);

            Assert.True(await db.UserRoles.AnyAsync(
                ur => ur.UserId == createdStudent.Id && ur.RoleId == studentRole.Id));
            Assert.True(await db.UserRoles.AnyAsync(
                ur => ur.UserId == createdParent.Id && ur.RoleId == parentRole.Id));
        });

        using var scope = fixture.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // The original passwords still verify and the seeder's password does not:
        // proof of no reset and no rotation across both runs.
        Assert.True(await userManager.CheckPasswordAsync(preStudent, PreExistingPassword));
        Assert.False(await userManager.CheckPasswordAsync(preStudent, SeededPassword));
        Assert.True(await userManager.CheckPasswordAsync(preParent, PreExistingPassword));
        Assert.False(await userManager.CheckPasswordAsync(preParent, SeededPassword));
    }

    [Fact]
    public async Task Seed_never_touches_another_tenant()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (demoSlug, demoId) = await CreateTenantAsync("demo-fscope");
        var (_, controlId) = await CreateTenantAsync("demo-fctrl");

        var controlUser = await CreateUserAsync(
            controlId, UniqueEmail(), "Control Student", PreExistingPassword, RoleNames.Student);

        await RunSeederAsync(demoSlug, studentEmails, parentEmails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // The control tenant keeps exactly what it had: its own single user and role
            // membership, nothing added by the seed run.
            Assert.Equal(1, await db.Users.CountAsync(u => u.TenantId == controlId));
            var unchanged = await db.Users.SingleAsync(u => u.Id == controlUser.Id);
            Assert.Equal("Control Student", unchanged.FullName);

            var controlRole = await db.Roles.SingleAsync(
                r => r.TenantId == controlId && r.Name == RoleNames.Student);
            var controlMembers = await db.UserRoles
                .Where(ur => ur.RoleId == controlRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync();

            Assert.Equal([controlUser.Id], controlMembers);

            // The demo tenant was seeded.
            Assert.Equal(4, await db.Users.CountAsync(u => u.TenantId == demoId));
        });
    }

    [Fact]
    public async Task Seed_skips_an_e_mail_owned_by_another_tenant_and_keeps_seeding_the_group()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (_, controlId) = await CreateTenantAsync("demo-fown");
        var (demoSlug, demoId) = await CreateTenantAsync("demo-fthief");

        var foreignUser = await CreateUserAsync(
            controlId, studentEmails[0], "Foreign Student Owner", PreExistingPassword, RoleNames.Student);

        await RunSeederAsync(demoSlug, studentEmails, parentEmails);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // 3 of 4 created: the foreign-owned address was skipped, the rest of the group
            // and the other group proceeded normally.
            Assert.Equal(3, await db.Users.CountAsync(u => u.TenantId == demoId));
            Assert.False(await db.Users.AnyAsync(
                u => u.TenantId == demoId && u.Email == studentEmails[0]));

            // The owning account is untouched.
            var owner = await db.Users.SingleAsync(u => u.Id == foreignUser.Id);
            Assert.Equal("Foreign Student Owner", owner.FullName);
            Assert.Equal(controlId, owner.TenantId);

            var ownerRole = await db.Roles.SingleAsync(
                r => r.TenantId == controlId && r.Name == RoleNames.Student);
            Assert.True(await db.UserRoles.AnyAsync(
                ur => ur.UserId == owner.Id && ur.RoleId == ownerRole.Id));
        });

        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await userManager.CheckPasswordAsync(foreignUser, PreExistingPassword));
    }

    [Fact]
    public async Task Seed_without_a_matching_tenant_changes_nothing()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var missingSlug = $"missing-{Guid.NewGuid():N}";

        var before = await CountUsersAsync();

        await RunSeederAsync(missingSlug, studentEmails, parentEmails);

        var after = await CountUsersAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Seed_without_either_password_is_a_logged_no_op()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (slug, _) = await CreateTenantAsync("demo-fnopwd");

        var before = await CountUsersAsync();

        await RunSeederAsync(slug, studentEmails, parentEmails, studentPassword: null, parentPassword: null);

        var after = await CountUsersAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Seed_with_only_one_password_creates_only_that_group()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");
        var (slug, tenantId) = await CreateTenantAsync("demo-f1pw");

        var before = await CountUsersAsync();

        await RunSeederAsync(slug, studentEmails, parentEmails, parentPassword: null);

        var after = await CountUsersAsync();

        // The two groups are gated independently: students seeded, parents skipped.
        Assert.Equal(2, after - before);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            Assert.True(await db.Users.AnyAsync(
                u => u.TenantId == tenantId && u.Email == studentEmails[0]));
            Assert.False(await db.Users.AnyAsync(
                u => u.TenantId == tenantId && u.Email == parentEmails[0]));
        });
    }

    [Fact]
    public async Task Seed_without_the_parent_role_row_skips_only_that_group()
    {
        var studentEmails = UniqueEmails("student");
        var parentEmails = UniqueEmails("parent");

        // All seven tenant roles except Parent: students proceed, parents must skip
        // (role repair is RoleSeeder's job) instead of throwing out of AddToRoleAsync.
        var (slug, tenantId) = await CreateTenantAsync(
            "demo-fnorole",
            RoleNames.TenantRoles.Where(r => r != RoleNames.Parent).ToArray());

        var before = await CountUsersAsync();

        await RunSeederAsync(slug, studentEmails, parentEmails);

        var after = await CountUsersAsync();

        Assert.Equal(2, after - before);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            Assert.True(await db.Users.AnyAsync(
                u => u.TenantId == tenantId && u.Email == studentEmails[1]));
            Assert.False(await db.Users.AnyAsync(
                u => u.TenantId == tenantId && u.Email == parentEmails[0]));
        });
    }

    private static string[] UniqueEmails(string prefix)
    =>
    [
        $"{prefix}.one-{Guid.NewGuid():N}@educationhub.local",
        $"{prefix}.two-{Guid.NewGuid():N}@educationhub.local"
    ];

    private static string UniqueEmail()
        => $"famstaff-{Guid.NewGuid():N}@educationhub.local";

    private async Task AssertCanSignInAsync(string email, string expectedRole)
    {
        var session = await ApiScenarios.LoginAsync(fixture.Client, email, SeededPassword);

        using var me = await fixture.Client.GetAsync("/api/auth/me", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var roles = (await me.ReadJsonAsync())
            .GetProperty("data")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(static r => r.GetString())
            .ToArray();

        Assert.Contains(expectedRole, roles);
    }

    private async Task RunSeederAsync(
        string slug,
        IReadOnlyList<string> studentEmails,
        IReadOnlyList<string> parentEmails,
        string? studentPassword = SeededPassword,
        string? parentPassword = SeededPassword)
    {
        var previousStudent = Environment.GetEnvironmentVariable(
            DemoStudentParentSeeder.StudentPasswordEnvironmentVariable);
        var previousParent = Environment.GetEnvironmentVariable(
            DemoStudentParentSeeder.ParentPasswordEnvironmentVariable);

        // null unsets the variable, which is how the missing-password contracts are
        // exercised. Same set/restore pattern SeedingTests uses for the admin variables;
        // neither variable's value is ever logged or asserted on.
        Environment.SetEnvironmentVariable(
            DemoStudentParentSeeder.StudentPasswordEnvironmentVariable, studentPassword);
        Environment.SetEnvironmentVariable(
            DemoStudentParentSeeder.ParentPasswordEnvironmentVariable, parentPassword);

        try
        {
            // Await inside the usings: disposing the scope while the seeder is still
            // running would close its DbContext connection mid-query.
            using var scope = fixture.CreateScope();
            using var _ = PlatformScopeOverride.Begin();

            await scope.ServiceProvider
                .GetRequiredService<DemoStudentParentSeeder>()
                .SeedAsync(slug, studentEmails, parentEmails);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                DemoStudentParentSeeder.StudentPasswordEnvironmentVariable, previousStudent);
            Environment.SetEnvironmentVariable(
                DemoStudentParentSeeder.ParentPasswordEnvironmentVariable, previousParent);
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
        IReadOnlyCollection<string>? provisionRoles = null)
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

        // null = all seven roles (what a runtime-provisioned tenant has); a specific list
        // supports the missing-role-row contract; empty = no roles at all.
        var provisioner = scope.ServiceProvider.GetRequiredService<TenantRoleProvisioner>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);

        foreach (var roleName in provisionRoles ?? RoleNames.TenantRoles)
        {
            var result = await provisioner.EnsureRoleAsync(roleName, tenant, CancellationToken.None);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        }

        return (slug, tenantId);
    }

    private async Task<ApplicationUser> CreateUserAsync(
        Guid tenantId,
        string email,
        string fullName,
        string password,
        string? roleToAssign)
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

        if (roleToAssign is not null)
        {
            var assigned = await userManager.AddToRoleAsync(user, roleToAssign);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        }

        return user;
    }
}

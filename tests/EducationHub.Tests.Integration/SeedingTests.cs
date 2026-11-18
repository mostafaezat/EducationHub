using System.Net;
using System.Security.Claims;
using EducationHub.API.Seed;
using EducationHub.Application.Features.Tenancy.Services;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Services;
using EducationHub.Shared.Common.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The seed pipeline's contract: idempotent, password-preserving, role-assigning, and
/// reachable only from Development with auto-migrate enabled.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SeedingTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Seeding_again_with_a_different_password_leaves_the_hash_untouched()
    {
        var hashBefore = await ReadAdminPasswordHashAsync();
        var previous = Environment.GetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD");

        // Hand the seeder a different password for the SAME email. Create-if-absent means
        // it must be ignored entirely: no reset, no rotation, no rehash.
        Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD", "Rotated#Passw0rd99");

        try
        {
            using var scope = fixture.CreateScope();
            using var _ = PlatformScopeOverride.Begin();
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

            Assert.True(await seeder.SeedAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD", previous);
        }

        Assert.Equal(hashBefore, await ReadAdminPasswordHashAsync());

        // The documented password still signs in.
        using var response = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = ApiTestFixture.AdminEmail, password = ApiTestFixture.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reseeding_keeps_a_tenants_own_changes_to_its_system_roles()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "seed-keep");

        var roles = await ModuleTestHelpers.GetDataAsync(Client, "/api/roles?search=Student&pageSize=50", token);
        var student = roles.GetProperty("items").EnumerateArray().First(r => r.GetProperty("name").GetString() == RoleNames.Student);
        var permissions = student.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.DoesNotContain(PermissionConstants.TeachersView, permissions);

        // The tenant gives its students one extra read permission.
        permissions.Add(PermissionConstants.TeachersView);
        await ModuleTestHelpers.PutDataAsync(Client, $"/api/roles/{student.Id()}/permissions", new { permissions }, token);

        using (var scope = fixture.CreateScope())
        using (PlatformScopeOverride.Begin())
        {
            await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync();
        }

        var after = await ModuleTestHelpers.GetDataAsync(Client, $"/api/roles/{student.Id()}", token);
        Assert.Contains(after.GetProperty("permissions").EnumerateArray(), p => p.GetString() == PermissionConstants.TeachersView);
    }

    [Fact]
    public async Task A_defaults_version_bump_only_adds_new_permissions_and_keeps_customisations()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "seed-additive");

        var roles = await ModuleTestHelpers.GetDataAsync(Client, "/api/roles?search=Assistant&pageSize=50", token);
        var assistant = roles.GetProperty("items").EnumerateArray().First(r => r.GetProperty("name").GetString() == RoleNames.Assistant);
        var permissions = assistant.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(PermissionConstants.AttendanceMark, permissions);
        Assert.DoesNotContain(PermissionConstants.PaymentsView, permissions);

        // The role as a tenant would have it under the previous defaults version: it removed one
        // default permission, added a non-default one, and the registration permissions did not
        // exist yet.
        permissions.Remove(PermissionConstants.AttendanceMark);
        permissions.Add(PermissionConstants.PaymentsView);
        permissions.Remove(PermissionConstants.RegistrationRequestsView);
        permissions.Remove(PermissionConstants.RegistrationRequestsManage);
        await ModuleTestHelpers.PutDataAsync(Client, $"/api/roles/{assistant.Id()}/permissions", new { permissions }, token);
        await SetDefaultsStampAsync(assistant.Id(), TenantRoleProvisioner.BaselineVersion);

        await ReseedRolesAsync();
        var first = await ReadAssistantAsync(assistant.Id(), token);

        Assert.DoesNotContain(PermissionConstants.AttendanceMark, first.Permissions);
        Assert.Contains(PermissionConstants.PaymentsView, first.Permissions);
        Assert.Contains(PermissionConstants.RegistrationRequestsView, first.Permissions);
        Assert.Contains(PermissionConstants.RegistrationRequestsManage, first.Permissions);
        Assert.Equal(TenantRoleProvisioner.DefaultsVersion, first.Stamp);

        // Idempotent: a second run changes nothing (the deliberate removal stays removed).
        await ReseedRolesAsync();
        var second = await ReadAssistantAsync(assistant.Id(), token);

        Assert.Equal(first.Permissions.Order(StringComparer.Ordinal), second.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(TenantRoleProvisioner.DefaultsVersion, second.Stamp);
    }

    [Fact]
    public void Every_version_entry_only_adds_permissions_the_role_has_by_default()
    {
        foreach (var roleName in RoleNames.TenantRoles)
        {
            var defaults = TenantRoleProvisioner.DefaultPermissionsFor(roleName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // New tenants get the full defaults; the upgrade path must never grant more than that.
            var addedSinceBaseline = TenantRoleProvisioner.PermissionsAddedSince(roleName, TenantRoleProvisioner.BaselineVersion);
            Assert.NotNull(addedSinceBaseline);
            Assert.All(addedSinceBaseline, p => Assert.Contains(p, defaults));

            // DefaultsVersion must be the newest entry: nothing is newer than it.
            Assert.Empty(TenantRoleProvisioner.PermissionsAddedSince(roleName, TenantRoleProvisioner.DefaultsVersion)!);
        }

        Assert.Null(TenantRoleProvisioner.PermissionsAddedSince(RoleNames.Assistant, "2999-01-01"));
    }

    private async Task ReseedRolesAsync()
    {
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync();
    }

    private async Task SetDefaultsStampAsync(Guid roleId, string version)
    {
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = await roleManager.Roles.IgnoreQueryFilters().FirstAsync(r => r.Id == roleId);

        foreach (var stamp in (await roleManager.GetClaimsAsync(role)).Where(c => c.Type == TenantRoleProvisioner.DefaultsVersionClaimType))
        {
            Assert.True((await roleManager.RemoveClaimAsync(role, stamp)).Succeeded);
        }

        Assert.True((await roleManager.AddClaimAsync(role, new Claim(TenantRoleProvisioner.DefaultsVersionClaimType, version))).Succeeded);
    }

    private async Task<(List<string> Permissions, string? Stamp)> ReadAssistantAsync(Guid roleId, string token)
    {
        var role = await ModuleTestHelpers.GetDataAsync(Client, $"/api/roles/{roleId}", token);
        var permissions = role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();

        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var entity = await roleManager.Roles.IgnoreQueryFilters().FirstAsync(r => r.Id == roleId);
        var stamps = (await roleManager.GetClaimsAsync(entity)).Where(c => c.Type == TenantRoleProvisioner.DefaultsVersionClaimType).ToList();

        // Exactly one stamp: the upgrade replaces the old one rather than piling them up.
        Assert.Single(stamps);

        return (permissions, stamps[0].Value);
    }

    [Fact]
    public async Task Missing_admin_credentials_skip_creation_and_keep_the_existing_account()
    {
        var previousEmail = Environment.GetEnvironmentVariable("EDUCATIONHUB_ADMIN_EMAIL");
        var previousPassword = Environment.GetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD");

        Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_EMAIL", null);
        Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD", null);

        bool seeded;

        try
        {
            using var scope = fixture.CreateScope();
            seeded = await scope.ServiceProvider
                .GetRequiredService<AdminUserSeeder>()
                .SeedAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_EMAIL", previousEmail);
            Environment.SetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD", previousPassword);
        }

        // Warn + skip: no failure, no invention of a default password.
        Assert.False(seeded);

        // The already-seeded administrator is untouched and still signs in.
        using var response = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = ApiTestFixture.AdminEmail, password = ApiTestFixture.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seeded_platform_admin_holds_the_platform_role()
    {
        var session = await ApiScenarios
            .LoginAsync(Client, ApiTestFixture.AdminEmail, ApiTestFixture.AdminPassword);

        using var me = await Client.GetAsync("/api/auth/me", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var roles = (await me.ReadJsonAsync())
            .GetProperty("data")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(static r => r.GetString())
            .ToArray();

        Assert.Contains(RoleNames.PlatformAdmin, roles);
    }

    [Fact]
    public async Task Auto_migrate_disabled_leaves_an_empty_database_untouched()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        var previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var previousAutoMigrate = Environment.GetEnvironmentVariable("Database__AutoMigrateOnStartup");

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", "false");

        try
        {
            using var factory = new ApiWebApplicationFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // The first request forces the host to build, so the initializer would have run
            // here if the flag permitted it.
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            Assert.Equal(0, await CountUsersTablesAsync(connectionString));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnection);
            Environment.SetEnvironmentVariable("Database__AutoMigrateOnStartup", previousAutoMigrate);
            await DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Non_development_environment_never_migrates_or_seeds()
    {
        // AutoMigrateOnStartup stays true (it is true in configuration): only the
        // environment guard is under test here.
        var connectionString = await CreateEmptyDatabaseAsync();
        var previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);

        try
        {
            using var factory = new ProductionApiWebApplicationFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            Assert.Equal(0, await CountUsersTablesAsync(connectionString));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnection);
            await DropDatabaseAsync(connectionString);
        }
    }

    private async Task<string?> ReadAdminPasswordHashAsync()
    {
        string? hash = null;

        await fixture.WithPlatformScopeAsync(async db =>
        {
            hash = await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Email == ApiTestFixture.AdminEmail)
                .Select(u => u.PasswordHash)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        return hash;
    }

    private static async Task<string> CreateEmptyDatabaseAsync()
    {
        var master = new SqlConnectionStringBuilder(SqlServerFixture.ServerConnectionString)
        {
            InitialCatalog = "master"
        };

        var name = $"EducationHub_SeedGate_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}]";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return new SqlConnectionStringBuilder(master.ConnectionString)
        {
            InitialCatalog = name
        }.ConnectionString;
    }

    private static async Task<int> CountUsersTablesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users'";

        return (int)(await command.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private static async Task DropDatabaseAsync(string connectionString)
    {
        SqlConnection.ClearAllPools();

        var target = new SqlConnectionStringBuilder(connectionString);
        var name = target.InitialCatalog;
        target.InitialCatalog = "master";

        try
        {
            await using var connection = new SqlConnection(target.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{name}];";

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // A leftover gate-test database is not worth failing a run over.
        }
    }
}

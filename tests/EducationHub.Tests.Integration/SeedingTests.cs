using System.Net;
using EducationHub.API.Seed;
using EducationHub.Infrastructure.Services;
using EducationHub.Shared.Common.Constants;
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

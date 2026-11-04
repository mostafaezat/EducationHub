using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EducationHub.Tests.Integration;

/// <summary>
/// A real SQL Server database, one per test run.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server rather than SQLite or the in-memory provider, because both of those lie about
/// the things this system is most likely to break on: SQLite has no <c>uniqueidentifier</c>
/// and no filtered indexes, and the in-memory provider skips query filters entirely - which
/// is precisely the mechanism tenant isolation depends on. A run against either would report
/// green while the isolation guarantee went untested.
/// </para>
/// <para>
/// The database name carries a new GUID, so parallel runs and a leftover database from a
/// failed run cannot collide.
/// </para>
/// </remarks>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"EducationHub_Test_{Guid.NewGuid():N}";

    private string _masterConnectionString = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// The connection string of the server this fixture creates its database on.
    /// </summary>
    /// <remarks>
    /// Read from <c>EducationHub__TestConnection</c> so CI containers and remote instances
    /// can be pointed somewhere else. Falls back to a local default instance.
    /// <c>localhost</c> rather than <c>MSSQLSERVER</c>: the latter is an <i>instance</i>
    /// name, not a resolvable host, so clients fall back to named pipes and fail with
    /// "network path not found" even when the server is up and listening on 1433.
    /// </remarks>
    public static string ServerConnectionString
        => Environment.GetEnvironmentVariable("EducationHub__TestConnection")
            ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";

    public async Task InitializeAsync()
    {
        var builder = new SqlConnectionStringBuilder(ServerConnectionString);

        if (!string.IsNullOrWhiteSpace(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
        {
            builder.InitialCatalog = "master";
        }

        _masterConnectionString = builder.ConnectionString;

        await using (var connection = new SqlConnection(_masterConnectionString))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = $"CREATE DATABASE [{_databaseName}]";

            await command.ExecuteNonQueryAsync();
        }

        var testBuilder = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName
        };

        ConnectionString = testBuilder.ConnectionString;

        await using var db = CreateContext(new PlatformTenantContext());
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Builds a context over a given tenant scope.
    /// </summary>
    /// <remarks>
    /// Pass <see cref="PlatformTenantContext"/> to arrange or inspect data across tenants.
    /// Pass <see cref="SingleTenantContext"/> to assert isolation - a test using the platform
    /// context to prove isolation would prove nothing, since that context bypasses the very
    /// filter under test.
    /// </remarks>
    public EducationHubDbContext CreateContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<EducationHubDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        var interceptor = new AuditSaveChangesInterceptor(
            new TestCurrentUser(tenantContext.TenantId, tenantContext.IsPlatformScope),
            new TestDiagnosticContext());

        return new EducationHubDbContext(options, tenantContext, interceptor);
    }

    /// <summary>
    /// Builds an Identity store over a context, for seeding users and roles.
    /// </summary>
    /// <remarks>
    /// Identity services are constructed by hand rather than resolved from DI, because each
    /// test needs its own context: a <c>UserManager</c> bound to a different context would
    /// read stale tracked state after another context changed the row.
    /// </remarks>
    public UserManager<ApplicationUser> CreateUserManager(EducationHubDbContext context)
    {
        var provider = BuildIdentityServices(context);

        return provider.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public RoleManager<ApplicationRole> CreateRoleManager(EducationHubDbContext context)
    {
        var provider = BuildIdentityServices(context);

        return provider.GetRequiredService<RoleManager<ApplicationRole>>();
    }

    private ServiceProvider BuildIdentityServices(EducationHubDbContext context)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(context);

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<EducationHubDbContext>()
            .AddDefaultTokenProviders();

        // Mirrors the application's DI: role names are unique per tenant, so the global
        // Identity role validator must not veto a second tenant's TenantAdmin.
        services.Replace(
            ServiceDescriptor.Scoped<IRoleValidator<ApplicationRole>, TenantScopedRoleValidator>());

        // AddDefaultTokenProviders registers token providers (using data protection keys)
        // that UserManager constructs eagerly, so a UserManager resolved bare in a test
        // needs a real IDataProtectionProvider or it throws at resolve time.
        services.AddDataProtection();

        return services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        try
        {
            await using var connection = new SqlConnection(_masterConnectionString);

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            // ROLLBACK IMMEDIATE because a leaked open connection would otherwise make the
            // drop fail and leave the database behind.
            command.CommandText =
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}];";

            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // A leftover test database is not worth failing a run over.
        }
    }
}

/// <summary>Sees every tenant's rows. For arranging data and cross-tenant assertions only.</summary>
public sealed class PlatformTenantContext : ITenantContext
{
    public Guid? TenantId => null;

    public Guid? BranchId => null;

    public bool IsPlatformScope => true;
}

/// <summary>Sees exactly one tenant's rows, exactly as a real request would.</summary>
public sealed class SingleTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId => tenantId;

    public Guid? BranchId => null;

    public bool IsPlatformScope => false;
}

internal sealed class TestCurrentUser(Guid? tenantId, bool isPlatformAdmin) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid? UserId => null;

    public string? UserName => "tests";

    public string? FullName => "Test User";

    public Guid? TenantId => tenantId;

    public Guid? BranchId => null;

    public string? IpAddress => "127.0.0.1";

    public string? PreferredLanguage => "en";

    public bool IsPlatformAdmin => isPlatformAdmin;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Permissions => [];

    public bool HasPermission(string permission) => false;
}

internal sealed class TestDiagnosticContext : IDiagnosticContext
{
    public string CorrelationId => "test";

    public string? IpAddress => "127.0.0.1";
}
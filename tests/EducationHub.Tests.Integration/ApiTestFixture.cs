using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// One real API host over one real SQL Server database, shared by every endpoint test in the
/// <c>api</c> collection.
/// </summary>
/// <remarks>
/// <para>
/// A single host is deliberate: building one runs the full program (migrations, seeding,
/// JWT setup), so a per-test host would multiply a multi-second startup by the number of
/// tests. xUnit runs tests inside one collection serially, so the shared host and its
/// rate-limit windows stay deterministic.
/// </para>
/// <para>
/// Configuration is pushed through <see cref="Environment"/> rather than
/// <c>WebApplicationFactory.ConfigureAppConfiguration</c> because <c>Program.cs</c> resolves
/// and validates the signing key in its top-level statements - before the factory's
/// configuration delegates are applied. Environment variables are visible to
/// <c>WebApplication.CreateBuilder</c> immediately, which is the only point early enough.
/// </para>
/// </remarks>
public sealed class ApiTestFixture : IAsyncLifetime
{
    public const string AdminEmail = "platform.admin@educationhub.test";
    public const string AdminPassword = "Platf0rm#Adm1n1";

    /// <summary>Long enough for HMAC-SHA256, and unique to tests so it never matches a real key.</summary>
    public const string SigningKey = "EDUCATIONHUB_TEST_ONLY_SIGNING_KEY_5d1f8b3a6c2e4907a3f61b8c2d5e7a90";

    private readonly string _databaseName = $"EducationHub_Api_{Guid.NewGuid():N}";
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);

    private string _masterConnectionString = string.Empty;
    private ApiWebApplicationFactory _factory = null!;

    public string ConnectionString { get; private set; } = string.Empty;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>Every WhatsApp message the shared host "sent".</summary>
    public CapturingWhatsAppSender WhatsApp => _factory.WhatsApp;

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync().ConfigureAwait(false);

        // A high ceiling on the credential limiter: the collection drives many login/refresh
        // calls from one address, and a realistic low limit would turn unrelated tests into
        // 429s. The limiter itself is proven by a dedicated host with a low limit (see
        // AuthEndpointTests.Rate_limiter_returns_429_with_retry_after).
        SetEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
        SetEnvironment("ConnectionStrings__DefaultConnection", ConnectionString);
        SetEnvironment("Database__AutoMigrateOnStartup", "true");
        SetEnvironment("Jwt__SigningKey", SigningKey);
        SetEnvironment("EDUCATIONHUB_ADMIN_EMAIL", AdminEmail);
        SetEnvironment("EDUCATIONHUB_ADMIN_PASSWORD", AdminPassword);
        SetEnvironment("Auth__RateLimit__PermitLimit", "10000");
        SetEnvironment("Auth__RateLimit__PerIp__PermitLimit", "10000");
        SetEnvironment("Auth__RateLimit__PhoneReset__PermitLimit", "10000");
        SetEnvironment("Auth__RateLimit__PublicRegistration__PermitLimit", "10000");
        SetEnvironment("Auth__Email__FrontendBaseUrl", "https://portal.test");

        _factory = new ApiWebApplicationFactory();

        Client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Forces the host to build and the initializer to run before the first test body, so a
        // startup failure surfaces here rather than as a confusing 500 later.
        using var response = await Client.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The test API host did not become ready ({(int)response.StatusCode}).");
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();

        foreach (var (key, value) in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        SqlConnection.ClearAllPools();

        try
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{_databaseName}];";

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // A leftover test database is not worth failing a run over.
        }
    }

    /// <summary>
    /// Runs a read/write against the database with full platform scope (every tenant visible).
    /// </summary>
    /// <remarks>
    /// For arranging and asserting only. Proving isolation through this scope would prove
    /// nothing, because it is the bypass the isolation filter exists to deny.
    /// </remarks>
    public async Task WithPlatformScopeAsync(Func<EducationHubDbContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var scope = _factory.Services.CreateScope();
        using var _ = PlatformScopeOverride.Begin();

        var db = scope.ServiceProvider.GetRequiredService<EducationHubDbContext>();

        await action(db).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a DI scope over the live host, for tests that must invoke an application
    /// service (the seed pipeline, for example) directly rather than through HTTP.
    /// </summary>
    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    /// <summary>
    /// Mints a real Identity token for the given flow, using the host's own token providers.
    /// </summary>
    /// <remarks>
    /// Generated by the running host so it is bound to the same data-protection keys the
    /// endpoint will validate against; a hand-built provider would derive different keys and
    /// every token would look invalid.
    /// </remarks>
    public Task<string> GenerateEmailConfirmationTokenAsync(string email)
        => GenerateTokenAsync(email, static (manager, user) => manager.GenerateEmailConfirmationTokenAsync(user));

    public Task<string> GeneratePasswordResetTokenAsync(string email)
        => GenerateTokenAsync(email, static (manager, user) => manager.GeneratePasswordResetTokenAsync(user));

    /// <summary>
    /// Builds a second host with a low credential limit, for the one test that must observe a
    /// 429. The caller disposes the returned factory.
    /// </summary>
    /// <remarks>
    /// The client is created inside the environment override on purpose: the limit is read
    /// when the host is built, which happens in <c>CreateClient</c>, so restoring the variable
    /// any earlier would let the host pick up the collection-wide ceiling instead.
    /// </remarks>
    internal (ApiWebApplicationFactory Factory, HttpClient Client) CreateRateLimitedHost(int permitLimit)
        => CreateRateLimitedHost(("Auth__RateLimit__PermitLimit", permitLimit));

    /// <summary>A second host with the given limits (environment variable name, value).</summary>
    internal (ApiWebApplicationFactory Factory, HttpClient Client) CreateRateLimitedHost(params (string Variable, int Value)[] limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var previous = limits.ToDictionary(static l => l.Variable, static l => Environment.GetEnvironmentVariable(l.Variable));

        foreach (var (variable, value) in limits)
        {
            Environment.SetEnvironmentVariable(variable, value.ToString(CultureInfo.InvariantCulture));
        }

        try
        {
            var factory = new ApiWebApplicationFactory();

            var client = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false
                });

            return (factory, client);
        }
        finally
        {
            foreach (var (variable, value) in previous)
            {
                Environment.SetEnvironmentVariable(variable, value);
            }
        }
    }

    private async Task<string> GenerateTokenAsync(
        string email,
        Func<UserManager<ApplicationUser>, ApplicationUser, Task<string>> generate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(generate);

        using var scope = _factory.Services.CreateScope();
        using var _ = PlatformScopeOverride.Begin();

        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await manager.FindByEmailAsync(email).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No user exists for {email}.");

        return await generate(manager, user).ConfigureAwait(false);
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private async Task CreateDatabaseAsync()
    {
        var builder = new SqlConnectionStringBuilder(SqlServerFixture.ServerConnectionString);

        if (!string.IsNullOrWhiteSpace(builder.InitialCatalog)
            && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
        {
            builder.InitialCatalog = "master";
        }

        _masterConnectionString = builder.ConnectionString;

        await using (var connection = new SqlConnection(_masterConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{_databaseName}]";

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        ConnectionString = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;
    }
}

/// <summary>Serializes every API test into one collection so they share one host.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiTestFixture>
{
    public const string Name = "api";
}

/// <summary>Small HTTP helpers shared by the endpoint test classes.</summary>
internal static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object body, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body)
        };

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string path, string? token = null)
        => SendAsync(client, HttpMethod.Get, path, token);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string path, object body, string? token)
        => SendJsonAsync(client, HttpMethod.Put, path, body, token);

    public static Task<HttpResponseMessage> DeleteAsync(this HttpClient client, string path, string? token)
        => SendAsync(client, HttpMethod.Delete, path, token);

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    /// <summary>Reads the ProblemDetails <c>title</c>, which the API uses as a stable code.</summary>
    public static async Task<string?> ReadProblemTitleAsync(this HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

            return body.TryGetProperty("title", out var title) ? title.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token)
        => SendCoreAsync(client, method, path, content: null, token);

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object body, string? token)
        => SendCoreAsync(client, method, path, JsonContent.Create(body), token);

    private static Task<HttpResponseMessage> SendCoreAsync(HttpClient client, HttpMethod method, string path, HttpContent? content, string? token)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client.SendAsync(request);
    }
}
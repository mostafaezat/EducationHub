using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Tests.Integration;

/// <summary>The token pair and profile returned by a successful login.</summary>
internal sealed record AuthSession(string AccessToken, string RefreshToken, JsonElement Data);

/// <summary>A freshly provisioned tenant and the credentials of its first administrator.</summary>
internal sealed record TenantAdmin(Guid TenantId, string Email, string Password);

/// <summary>
/// End-to-end arrangements built from real HTTP calls, so tests exercise the same surface a
/// client does rather than reaching into the database to set up state.
/// </summary>
internal static class ApiScenarios
{
    public static async Task<AuthSession> LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostJsonAsync("/api/auth/login", new { email, password }).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Login for {email} failed with {(int)response.StatusCode}.");
        }

        var body = await response.ReadJsonAsync().ConfigureAwait(false);
        var data = body.GetProperty("data");

        return new AuthSession(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!,
            data);
    }

    public static async Task<string> LoginTokenAsync(HttpClient client, string email, string password)
        => (await LoginAsync(client, email, password).ConfigureAwait(false)).AccessToken;

    public static async Task<TenantAdmin> ProvisionTenantAsync(
        HttpClient client,
        string platformToken,
        string slugPrefix)
    {
        ArgumentNullException.ThrowIfNull(client);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"{slugPrefix}-{suffix}";
        var adminEmail = $"admin-{suffix}@example.test";
        const string adminPassword = "Adm1nPassw0rd";

        using var response = await client.PostJsonAsync(
            "/api/tenants",
            new
            {
                slug,
                name = "Isolation Academy",
                type = "EducationCenter",
                currency = "EGP",
                timeZone = "Africa/Cairo",
                defaultLanguage = "ar",
                adminFullName = "Tenant Admin",
                adminEmail,
                adminPassword
            },
            platformToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Tenant provisioning failed with {(int)response.StatusCode}.");
        }

        var body = await response.ReadJsonAsync().ConfigureAwait(false);
        var tenantId = body.GetProperty("data").GetProperty("id").GetGuid();

        return new TenantAdmin(tenantId, adminEmail, adminPassword);
    }

    public static async Task<Guid> CreateUserAsync(
        HttpClient client,
        string callerToken,
        string fullName,
        string email,
        string password,
        IReadOnlyList<string>? roles = null,
        bool mustChangePassword = false,
        bool sendEmailConfirmation = false)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.PostJsonAsync(
            "/api/users",
            new
            {
                fullName,
                email,
                password,
                phoneNumber = (string?)null,
                branchId = (Guid?)null,
                preferredLanguage = "ar",
                mustChangePassword,
                roles = roles ?? [],
                sendEmailConfirmation
            },
            callerToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"User creation failed with {(int)response.StatusCode}.");
        }

        var body = await response.ReadJsonAsync().ConfigureAwait(false);

        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    public static string NewEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Seeds the platform administrator and returns its access token.</summary>
    public static Task<string> PlatformAdminTokenAsync(HttpClient client)
        => LoginTokenAsync(client, ApiTestFixture.AdminEmail, ApiTestFixture.AdminPassword);

    /// <summary>Number of live (unrevoked) refresh tokens for an address, read with platform scope.</summary>
    public static async Task<int> CountLiveRefreshTokensAsync(ApiTestFixture fixture, string email)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var count = 0;

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email)
                .ConfigureAwait(false);

            if (user is null)
            {
                return;
            }

            count = await db.RefreshTokens
                .IgnoreQueryFilters()
                .CountAsync(t => t.UserId == user.Id && t.RevokedAt == null)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);

        return count;
    }
}
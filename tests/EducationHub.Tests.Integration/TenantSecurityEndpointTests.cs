using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The multi-tenant boundary as seen through the API: one tenant cannot read or act on
/// another's data, a contradicting tenant header is refused, platform endpoints are
/// unreachable from a tenant administrator, and a suspended tenant cannot authenticate.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenantSecurityEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Tenant_admin_cannot_read_another_tenants_user()
    {
        var (_, tokenA) = await ProvisionAndLoginAsync("iso-a");
        var (_, tokenB) = await ProvisionAndLoginAsync("iso-b");

        var userBEmail = ApiScenarios.NewEmail("b-user");

        var userBId = await ApiScenarios.CreateUserAsync(
            Client,
            tokenB,
            "B User",
            userBEmail,
            "UserB#Pass1",
            roles: ["TenantAdmin"]);

        // Direct read: the query filter hides the row, so it is a 404 - not a 403 that would
        // confirm the id exists.
        using var directRead = await Client.GetAsync($"/api/users/{userBId}", tokenA);
        Assert.Equal(HttpStatusCode.NotFound, directRead.StatusCode);

        // List read: tenant A's page must not contain tenant B's user.
        using var list = await Client.GetAsync("/api/users", tokenA);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var items = (await list.ReadJsonAsync()).GetProperty("data").GetProperty("items");

        Assert.DoesNotContain(
            items.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == userBId);
    }

    [Fact]
    public async Task Contradicting_tenant_header_is_rejected()
    {
        var (tenantA, tokenA) = await ProvisionAndLoginAsync("hdr-a");
        var (tenantB, _) = await ProvisionAndLoginAsync("hdr-b");

        // A header that agrees with the signed claim is harmless.
        using var agreeing = await SendWithTenantHeaderAsync("/api/users", tokenA, tenantA.TenantId);
        Assert.Equal(HttpStatusCode.OK, agreeing.StatusCode);

        // A header that contradicts it is refused rather than silently switching tenant.
        using var contradicting = await SendWithTenantHeaderAsync("/api/users", tokenA, tenantB.TenantId);
        Assert.Equal(HttpStatusCode.Forbidden, contradicting.StatusCode);
    }

    [Fact]
    public async Task Unknown_login_email_gets_the_same_generic_failure()
    {
        using var response = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = ApiScenarios.NewEmail("ghost"), password = "Ghost#Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Auth.InvalidCredentials", await response.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Suspended_tenant_blocks_login()
    {
        var (tenant, _) = await ProvisionAndLoginAsync("suspend");

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var row = await db.Tenants
                .IgnoreQueryFilters()
                .FirstAsync(t => t.Id == tenant.TenantId);

            row.IsActive = false;

            await db.SaveChangesAsync();
        });

        using var login = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = tenant.Email, password = tenant.Password });

        Assert.Equal(HttpStatusCode.Conflict, login.StatusCode);
        Assert.Equal("Tenant.Inactive", await login.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Tenant_admin_cannot_reach_platform_endpoints()
    {
        var (_, token) = await ProvisionAndLoginAsync("platform-block");
        var (otherTenant, _) = await ProvisionAndLoginAsync("platform-other");

        using var listTenants = await Client.GetAsync("/api/tenants", token);
        Assert.Equal(HttpStatusCode.Forbidden, listTenants.StatusCode);

        using var readOther = await Client.GetAsync($"/api/tenants/{otherTenant.TenantId}", token);
        Assert.Equal(HttpStatusCode.Forbidden, readOther.StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_present_on_success_and_error_responses()
    {
        using var ok = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        Assert.Equal("nosniff", HeaderValue(ok, "X-Content-Type-Options"));
        Assert.Equal("DENY", HeaderValue(ok, "X-Frame-Options"));
        Assert.Equal("no-referrer", HeaderValue(ok, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", HeaderValue(ok, "Content-Security-Policy"));

        // The error path carries them too, because the middleware runs before the exception handler.
        using var error = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = ApiScenarios.NewEmail("ghost"), password = "Ghost#Pass1" });

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal("nosniff", HeaderValue(error, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task No_migrations_are_pending()
    {
        var pending = Array.Empty<string>();

        await fixture.WithPlatformScopeAsync(async db =>
        {
            pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        });

        Assert.Empty(pending);
    }

    private static string? HeaderValue(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private async Task<HttpResponseMessage> SendWithTenantHeaderAsync(string path, string token, Guid tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString("D"));

        return await Client.SendAsync(request);
    }

    private async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        return (tenant, token);
    }
}
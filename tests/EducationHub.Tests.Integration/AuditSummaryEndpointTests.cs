using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// GET /api/audit/summary. The handler used to construct the DTO inside the GroupBy
/// projection, which EF Core cannot translate - every call came back 409. These tests pin
/// the endpoint to 200 with a well-formed grouped payload.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditSummaryEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Summary_returns_grouped_activity_as_ok()
    {
        var (_, token) = await ProvisionAndLoginAsync("audit-sum");

        // Signing in writes an audit row, so the payload is normally non-empty - but the
        // contract under test is 200 + a well-formed array, whatever its size.
        using var response = await Client.GetAsync("/api/audit/summary", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);

        foreach (var row in data.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, row.ValueKind);
            Assert.True(row.TryGetProperty("action", out _));
            Assert.True(row.TryGetProperty("count", out _));
            Assert.True(row.GetProperty("count").GetInt32() > 0);
            Assert.True(row.TryGetProperty("lastOccurredAt", out _));
        }
    }

    [Fact]
    public async Task Summary_accepts_date_filters_and_top()
    {
        var (_, token) = await ProvisionAndLoginAsync("audit-range");

        using var response = await Client.GetAsync(
            "/api/audit/summary?from=2020-01-01&to=2099-12-31&top=5",
            token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.True(data.GetArrayLength() <= 5);
    }

    [Fact]
    public async Task Summary_requires_an_authenticated_caller()
    {
        using var response = await Client.GetAsync("/api/audit/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        return (tenant, token);
    }
}

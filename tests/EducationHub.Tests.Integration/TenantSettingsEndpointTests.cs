using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// GET/PUT /api/settings. TenantSettings is keyed on TenantId, and both handlers used to
/// look the row up through a lookup keyed on "Id" - so every call came back 404 for a row
/// that exists. These tests pin the endpoints to 200 with the seeded values.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenantSettingsEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Get_settings_returns_the_seed_row_for_the_tenant()
    {
        var (_, token) = await ProvisionAndLoginAsync("settings-get");

        using var response = await Client.GetAsync("/api/settings", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");

        Assert.Equal(JsonValueKind.Object, data.ValueKind);
        Assert.Equal(15, data.GetProperty("lateThresholdMinutes").GetInt32());
        Assert.Equal(5, data.GetProperty("feeDueDayOfMonth").GetInt32());
        Assert.Equal("EGP", data.GetProperty("currencySymbol").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("tenantName").GetString()));
    }

    [Fact]
    public async Task Put_settings_updates_and_reads_back()
    {
        var (_, token) = await ProvisionAndLoginAsync("settings-put");

        using var update = await Client.PutJsonAsync(
            "/api/settings",
            new { lateThresholdMinutes = 42, currencySymbol = "SAR", gracePeriodDays = 7 },
            token);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var reread = await Client.GetAsync("/api/settings", token);
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);

        var data = (await reread.ReadJsonAsync()).GetProperty("data");

        Assert.Equal(42, data.GetProperty("lateThresholdMinutes").GetInt32());
        Assert.Equal("SAR", data.GetProperty("currencySymbol").GetString());
        Assert.Equal(7, data.GetProperty("gracePeriodDays").GetInt32());

        // Fields the update did not touch keep their seeded defaults.
        Assert.Equal(5, data.GetProperty("feeDueDayOfMonth").GetInt32());
    }

    [Fact]
    public async Task Settings_are_isolated_per_tenant()
    {
        var (_, tokenA) = await ProvisionAndLoginAsync("settings-iso-a");
        var (_, tokenB) = await ProvisionAndLoginAsync("settings-iso-b");

        using var update = await Client.PutJsonAsync(
            "/api/settings",
            new { lateThresholdMinutes = 30 },
            tokenA);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var readB = await Client.GetAsync("/api/settings", tokenB);
        Assert.Equal(HttpStatusCode.OK, readB.StatusCode);

        var dataB = (await readB.ReadJsonAsync()).GetProperty("data");

        // Tenant A's change must not leak into tenant B's row.
        Assert.Equal(15, dataB.GetProperty("lateThresholdMinutes").GetInt32());
    }

    private async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        return (tenant, token);
    }
}

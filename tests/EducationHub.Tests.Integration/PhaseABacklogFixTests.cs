using System.Net;
using System.Text.Json;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Regression coverage for the Mission Phase A backlog fixes:
/// BL-009 (profile carries the tenant name) and BL-016 (a soft-deleted grade releases its
/// per-tenant Order slot).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PhaseABacklogFixTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Tenant_user_profile_carries_the_tenant_name()
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, "bl009");

        var session = await ApiScenarios.LoginAsync(Client, tenant.Email, tenant.Password);

        // Login response.
        Assert.Equal(
            "Isolation Academy",
            session.Data.GetProperty("user").GetProperty("tenantName").GetString());

        // /api/auth/me reads the same snapshot.
        using var me = await Client.GetAsync("/api/auth/me", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var data = (await me.ReadJsonAsync()).GetProperty("data");
        Assert.Equal("Isolation Academy", data.GetProperty("tenantName").GetString());
    }

    [Fact]
    public async Task Platform_admin_profile_has_no_tenant_name()
    {
        var token = await ApiScenarios.PlatformAdminTokenAsync(Client);

        using var me = await Client.GetAsync("/api/auth/me", token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var data = (await me.ReadJsonAsync()).GetProperty("data");
        // The API omits null members (JsonIgnoreCondition.WhenWritingNull), so "no tenant
        // name" is either an absent member or an explicit null.
        var hasName = data.TryGetProperty("tenantName", out var tenantName)
            && tenantName.ValueKind != JsonValueKind.Null;

        Assert.False(hasName, $"Expected no tenantName for the platform admin, got {tenantName}.");
    }

    [Fact]
    public async Task Platform_role_claim_without_platform_tenant_cannot_reach_tenant_endpoints()
    {
        // A correctly signed token for a real tenant user that also carries the PlatformAdmin
        // role claim - the shape a role-name collision or a provisioning bug would produce.
        // /api/tenants used [Authorize(Roles = PlatformAdmin)], which checks the role alone;
        // it now uses the PlatformAdmin policy, which also requires TenantId == Guid.Empty.
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, "bl007");

        ApplicationUser user = null!;
        await fixture.WithPlatformScopeAsync(async db =>
        {
            var normalized = tenant.Email.ToUpperInvariant();
            user = await db.Users.AsNoTracking().FirstAsync(u => u.NormalizedEmail == normalized);
        });

        Assert.NotEqual(Guid.Empty, user.TenantId);

        string forged;
        using (var scope = fixture.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            forged = tokens.CreateAccessToken(user, [RoleNames.PlatformAdmin], []).Token;
        }

        using var list = await Client.GetAsync("/api/tenants", forged);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var one = await Client.GetAsync($"/api/tenants/{tenant.TenantId}", forged);
        Assert.Equal(HttpStatusCode.Forbidden, one.StatusCode);

        // The genuine platform administrator is unaffected.
        using var genuine = await Client.GetAsync("/api/tenants", platform);
        Assert.Equal(HttpStatusCode.OK, genuine.StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_grade_releases_its_order_for_reuse()
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, "bl016");
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        const int order = 901;

        using var first = await Client.PostJsonAsync(
            "/api/grades",
            new { code = "BL16A", name = "Order slot A", order },
            token);
        Assert.True(
            first.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"First create returned {(int)first.StatusCode}.");

        var firstId = (await first.ReadJsonAsync()).GetProperty("data").GetProperty("id").GetGuid();

        using var delete = await Client.DeleteAsync($"/api/grades/{firstId}", token);
        Assert.True(
            delete.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"Delete returned {(int)delete.StatusCode}.");

        // Before BL-016 the unfiltered unique index still held (TenantId, 901) for the
        // soft-deleted row, and this insert failed at the database.
        using var second = await Client.PostJsonAsync(
            "/api/grades",
            new { code = "BL16B", name = "Order slot B", order },
            token);
        Assert.True(
            second.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Re-using the Order of a soft-deleted grade returned {(int)second.StatusCode}.");
    }
}

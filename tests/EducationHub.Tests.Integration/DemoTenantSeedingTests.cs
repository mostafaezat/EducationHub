using System.Net;
using EducationHub.API.Seed;
using EducationHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>A fresh development database gets its demo tenant and a working tenant administrator.</summary>
[Collection(ApiCollection.Name)]
public sealed class DemoTenantSeedingTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Creates_the_tenant_and_a_signing_in_admin_once()
    {
        var slug = "demo-tenant-" + Guid.NewGuid().ToString("N")[..8];
        var email = ApiScenarios.NewEmail("demo-tenant-admin");

        await RunAsync(slug, email, "Adm1nPassw0rd");

        var token = await ApiScenarios.LoginTokenAsync(Client, email, "Adm1nPassw0rd");
        using (var students = await Client.GetAsync("/api/students", token))
        {
            Assert.Equal(HttpStatusCode.OK, students.StatusCode);
        }

        // A second run with a different password neither duplicates nor resets anything.
        await RunAsync(slug, email, "Different1Pass");
        await ApiScenarios.LoginTokenAsync(Client, email, "Adm1nPassw0rd");

        var count = 0;
        await fixture.WithPlatformScopeAsync(async db =>
            count = await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Slug == slug));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Without_a_password_nothing_is_created()
    {
        var slug = "demo-tenant-nopw-" + Guid.NewGuid().ToString("N")[..8];

        await RunAsync(slug, ApiScenarios.NewEmail("demo-nopw"), null);

        var exists = true;
        await fixture.WithPlatformScopeAsync(async db =>
            exists = await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug));
        Assert.False(exists);
    }

    private async Task RunAsync(string slug, string email, string? password)
    {
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        await scope.ServiceProvider.GetRequiredService<DemoTenantSeeder>().SeedAsync(slug, email, password);
    }
}

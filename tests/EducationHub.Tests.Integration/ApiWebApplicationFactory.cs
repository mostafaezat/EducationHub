using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Builds the real API host in-process for endpoint tests.
/// </summary>
/// <remarks>
/// Every machine-dependent setting (connection string, signing key, seeded administrator
/// credentials, rate-limit ceiling) is supplied through environment variables by
/// <see cref="ApiTestFixture"/> before the host is first built, because the API reads those
/// values at startup - before <see cref="ConfigureWebHost"/> overrides would take effect.
/// This factory therefore only pins the environment name.
/// </remarks>
internal sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Development so the database initializer (migrate + seed) runs and Swagger is enabled,
        // matching the E2E configuration the fixes were verified against.
        builder.UseEnvironment("Development");
    }
}
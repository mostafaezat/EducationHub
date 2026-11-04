using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Builds the host exactly as a production deploy would run it: the environment name is
/// Production, so <c>Program.cs</c> must skip the migrate-and-seed initializer even though
/// <c>Database:AutoMigrateOnStartup</c> is true in configuration.
/// </summary>
internal sealed class ProductionApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Production");
    }
}

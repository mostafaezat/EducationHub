using EducationHub.API.Seed;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EducationHub.API;

/// <summary>
/// Brings the database up to date at startup, then applies the seed data.
/// </summary>
/// <remarks>
/// <para>
/// Called only in Development, and only when <c>Database:AutoMigrateOnStartup</c> is true.
/// Both guards matter: an unguarded startup migration lets two instances race each other
/// into a failed deployment, and lets an accidental production deploy alter the schema.
/// Production migrations are an explicit, reviewed step.
/// </para>
/// <para>
/// <c>EnsureCreated</c> is never used. It creates a schema from the current model with no
/// history, which means the first real migration then tries to recreate tables that already
/// exist.
/// </para>
/// </remarks>
public static class DatabaseInitializer
{
    public static async Task ApplyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();

        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var context = provider.GetRequiredService<EducationHubDbContext>();

        logger.LogInformation("Applying database migrations");

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var seeder = provider.GetRequiredService<DatabaseSeeder>();

        // Seeding runs before any request exists, so there is no principal for
        // HttpTenantContext to read scope from - and without an explicit platform scope the
        // very save-time check that protects a real request would refuse the platform
        // administrator row (no tenant) and hide the roles the seeder is about to update.
        // Scoped with `using` so the elevation ends with the seeding call.
        using (PlatformScopeOverride.Begin())
        {
            await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Database initialization complete");
    }
}
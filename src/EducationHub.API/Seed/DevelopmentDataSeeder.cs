using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Placeholder for development-only demo records; seeds nothing automatically.
/// </summary>
/// <remarks>
/// The documented demo data (the <c>admin-ui-test</c> tenant and
/// <c>tenant.admin@educationhub.local</c>) is created through the API at runtime, which
/// exercises the same validation, provisioning, audit and role-assignment paths a real
/// record takes. Manufacturing it here would bypass all four, so seeding deliberately
/// never does. See docs/SEED_DATA_AND_LOCAL_LOGIN.md for how the demo records are made.
/// </remarks>
public sealed class DevelopmentDataSeeder(ILogger<DevelopmentDataSeeder> logger)
{
    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "No development-only records are seeded automatically; demo data is created through the API.");

        return Task.CompletedTask;
    }
}

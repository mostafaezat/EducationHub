using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Runs every seed class in dependency order: roles first (the administrator is assigned
/// the platform role immediately after creation), then the administrator, then the
/// academic catalog for the demo tenant, then that tenant's demo teacher accounts, then
/// its demo student and parent accounts, then the development-data placeholder.
/// </summary>
/// <remarks>
/// Called only from <c>DatabaseInitializer</c>, which itself runs only in Development and
/// only when <c>Database:AutoMigrateOnStartup</c> is true. Nothing else resolves this type,
/// so the seed pipeline cannot be reached from an environment where it is not wanted.
/// </remarks>
public sealed class DatabaseSeeder(
    RoleSeeder roles,
    AdminUserSeeder admin,
    DemoTenantSeeder demoTenant,
    AcademicSeeder academic,
    DemoTeacherSeeder teachers,
    DemoStudentParentSeeder students,
    DemoStudentProfileSeeder studentProfiles,
    DemoOperationsSeeder operations,
    DevelopmentDataSeeder development,
    ILogger<DatabaseSeeder> logger)
{
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        await roles.SeedAsync(cancellationToken).ConfigureAwait(false);

        var adminSeeded = await admin.SeedAsync(cancellationToken).ConfigureAwait(false);

        // Creates the demo tenant on a fresh dev database (opt-in), so the demo seeders below have
        // a tenant to fill on the very first run.
        await demoTenant.SeedAsync(cancellationToken).ConfigureAwait(false);

        await academic.SeedAsync(cancellationToken).ConfigureAwait(false);

        await teachers.SeedAsync(cancellationToken).ConfigureAwait(false);

        await students.SeedAsync(cancellationToken).ConfigureAwait(false);

        await studentProfiles.SeedAsync(cancellationToken).ConfigureAwait(false);

        await operations.SeedAsync(cancellationToken).ConfigureAwait(false);

        await development.SeedAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seed pipeline complete: roles reconciled, platform administrator seeded: {AdminSeeded}",
            adminSeeded);

        return adminSeeded;
    }
}

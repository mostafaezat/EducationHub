using EducationHub.Application.Common.Helpers;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Creates the first platform administrator when none exists.
/// </summary>
/// <remarks>
/// <para>
/// Credentials come from configuration rather than being hard-coded, and their absence is
/// a logged no-op rather than a startup failure. A seeder that invents a default admin
/// password would ship a known credential to production; a seeder that refuses to start
/// when the operator has not supplied one would make the API unbootable in an
/// environment where the account is meant to be created out of band. Warning plus skip
/// reports both facts without turning either into an outage.
/// </para>
/// <para>
/// Idempotent by email lookup, so an operator can set the variables, start the app once
/// and then unset them. An existing account is never modified: the password is never
/// reset, never rotated, and never logged. See docs/SEED_DATA_AND_LOCAL_LOGIN.md.
/// </para>
/// </remarks>
public sealed class AdminUserSeeder(
    UserManager<ApplicationUser> userManager,
    ILogger<AdminUserSeeder> logger)
{
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        var email = Normalize.Email(
            Environment.GetEnvironmentVariable("EDUCATIONHUB_ADMIN_EMAIL"));

        var password = Environment.GetEnvironmentVariable("EDUCATIONHUB_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "EDUCATIONHUB_ADMIN_EMAIL / EDUCATIONHUB_ADMIN_PASSWORD are not both set; " +
                "no platform administrator was created. Set them and restart to seed the first admin.");

            return false;
        }

        if (await userManager.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return true;
        }

        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = "Platform Administrator",
            TenantId = Guid.Empty,
            PreferredLanguage = SupportedLanguages.Default,
            IsActive = true,
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(admin, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            logger.LogError(
                "Could not create the platform administrator: {Errors}",
                string.Join("; ", created.Errors.Select(static e => e.Description)));

            return false;
        }

        var assigned = await userManager.AddToRoleAsync(admin, RoleNames.PlatformAdmin).ConfigureAwait(false);

        if (!assigned.Succeeded)
        {
            logger.LogError(
                "Platform administrator created but the role assignment failed: {Errors}",
                string.Join("; ", assigned.Errors.Select(static e => e.Description)));

            return false;
        }

        return true;
    }
}

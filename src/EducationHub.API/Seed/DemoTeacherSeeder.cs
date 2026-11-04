using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Creates the demo teacher accounts of the demo tenant (mission Phase 1b).
/// </summary>
/// <remarks>
/// <para>
/// These are plain <see cref="ApplicationUser"/> rows carrying the demo tenant's own
/// <c>Teacher</c> role: teacher profiles and subject assignments belong to the teachers
/// module (Mission Phase 2), not to this seeder.
/// </para>
/// <para>
/// The password comes from configuration rather than being hard-coded, and its absence is
/// a logged no-op rather than a startup failure — the same contract as
/// <see cref="AdminUserSeeder"/>, for the same reason: a seeder that invents a default
/// password ships a known credential. Idempotent by e-mail lookup (e-mail is globally
/// unique), so an existing account is never modified: no profile edits, no role changes,
/// no password reset. See docs/SEED_DATA_AND_LOCAL_LOGIN.md §4.3.
/// </para>
/// </remarks>
public sealed class DemoTeacherSeeder(
    EducationHubDbContext context,
    UserManager<ApplicationUser> userManager,
    ILogger<DemoTeacherSeeder> logger)
{
    /// <summary>The environment variable supplying the initial password of every demo teacher.</summary>
    public const string PasswordEnvironmentVariable = "EDUCATIONHUB_DEMO_TEACHER_PASSWORD";

    /// <summary>The demo teacher e-mail addresses, in the order their display names are numbered.</summary>
    public static readonly IReadOnlyList<string> DemoTeacherEmails =
    [
        "teacher.one@educationhub.local",
        "teacher.two@educationhub.local"
    ];

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(AcademicSeeder.DemoTenantSlug, DemoTeacherEmails, cancellationToken);

    /// <summary>Seeds demo teachers into the tenant with the given slug.</summary>
    /// <remarks>
    /// The slug and e-mail parameters exist so tests can target throwaway tenants with
    /// unique addresses; production code always calls the parameterless overload, which
    /// pins <see cref="AcademicSeeder.DemoTenantSlug"/> and <see cref="DemoTeacherEmails"/>.
    /// </remarks>
    public async Task SeedAsync(
        string tenantSlug,
        IReadOnlyList<string> emails,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(emails);

        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "{Variable} is not set; no demo teacher accounts were created. " +
                "Set it and restart to seed them (docs/SEED_DATA_AND_LOCAL_LOGIN.md §4.3).",
                PasswordEnvironmentVariable);

            return;
        }

        var normalizedEmails = emails
            .Where(static e => !string.IsNullOrWhiteSpace(e))
            .Select(static e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedEmails.Length == 0)
        {
            return;
        }

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.Slug == tenantSlug && !t.IsDeleted,
                cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogWarning(
                "No active tenant with slug {Slug}; demo teacher seed skipped. " +
                "Create the demo tenant through the API first (docs/SEED_DATA_AND_LOCAL_LOGIN.md).",
                tenantSlug);

            return;
        }

        // The tenant's own Teacher row (RoleSeeder / tenant provisioning creates it). Checked
        // up front because ApplicationUserStore.AddToRoleAsync throws when the row is absent,
        // and seeding must log and continue, never abort the pipeline.
        var teacherRoleExists = await context.Set<ApplicationRole>()
            .IgnoreQueryFilters()
            .AnyAsync(
                r => r.TenantId == tenant.Id && r.Name == RoleNames.Teacher,
                cancellationToken)
            .ConfigureAwait(false);

        if (!teacherRoleExists)
        {
            logger.LogWarning(
                "Tenant {Slug} has no {Role} role row; demo teacher seed skipped. " +
                "RoleSeeder repairs tenant roles on the next run.",
                tenantSlug,
                RoleNames.Teacher);

            return;
        }

        for (var index = 0; index < normalizedEmails.Length; index++)
        {
            var email = normalizedEmails[index];

            var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

            if (existing is not null)
            {
                // Create-if-absent only. Never modify, never reset a password, and never
                // adopt an account that belongs to a different tenant.
                if (existing.TenantId == tenant.Id)
                {
                    logger.LogDebug(
                        "Demo teacher {Email} already exists in tenant {Slug}; left untouched.",
                        email,
                        tenantSlug);
                }
                else
                {
                    logger.LogWarning(
                        "E-mail {Email} already belongs to another tenant; " +
                        "no demo teacher was created and the existing account was left untouched.",
                        email);
                }

                continue;
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FullName = $"Demo Teacher {index + 1}",
                TenantId = tenant.Id,
                PreferredLanguage = SupportedLanguages.Default,
                IsActive = true,
                EmailConfirmed = true
            };

            var created = await userManager.CreateAsync(user, password).ConfigureAwait(false);

            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create demo teacher {Email}: {Errors}",
                    email,
                    string.Join("; ", created.Errors.Select(static e => e.Description)));

                continue;
            }

            var assigned = await userManager
                .AddToRoleAsync(user, RoleNames.Teacher)
                .ConfigureAwait(false);

            if (!assigned.Succeeded)
            {
                logger.LogError(
                    "Demo teacher {Email} created but the {Role} role was not assigned: {Errors}",
                    email,
                    RoleNames.Teacher,
                    string.Join("; ", assigned.Errors.Select(static e => e.Description)));

                continue;
            }

            logger.LogInformation(
                "Created demo teacher {Email} ({FullName}) in tenant {Slug}",
                email,
                user.FullName,
                tenantSlug);
        }
    }
}

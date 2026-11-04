using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Creates the demo student and demo parent accounts of the demo tenant (mission Phase 1c).
/// </summary>
/// <remarks>
/// <para>
/// Both groups are plain <see cref="ApplicationUser"/> rows carrying the demo tenant's own
/// <c>Student</c> / <c>Parent</c> role: student and parent profile entities belong to their
/// module phases, not to this seeder.
/// </para>
/// <para>
/// Each group has its own password environment variable and is gated independently, so an
/// operator can seed either group alone; an unset variable is a logged skip for that group,
/// never a startup failure and never an invented password — the same contract as
/// <see cref="AdminUserSeeder"/> and <see cref="DemoTeacherSeeder"/>. Idempotent by e-mail
/// lookup (e-mail is globally unique), so an existing account is never modified: no profile
/// edits, no role changes, no password reset, and an e-mail owned by another tenant is
/// always left with its owner. See docs/SEED_DATA_AND_LOCAL_LOGIN.md §4.4–§4.5.
/// </para>
/// </remarks>
public sealed class DemoStudentParentSeeder(
    EducationHubDbContext context,
    UserManager<ApplicationUser> userManager,
    ILogger<DemoStudentParentSeeder> logger)
{
    /// <summary>The environment variable supplying the initial password of every demo student.</summary>
    public const string StudentPasswordEnvironmentVariable = "EDUCATIONHUB_DEMO_STUDENT_PASSWORD";

    /// <summary>The environment variable supplying the initial password of every demo parent.</summary>
    public const string ParentPasswordEnvironmentVariable = "EDUCATIONHUB_DEMO_PARENT_PASSWORD";

    /// <summary>The demo student e-mail addresses, in the order their display names are numbered.</summary>
    public static readonly IReadOnlyList<string> DemoStudentEmails =
    [
        "student.one@educationhub.local",
        "student.two@educationhub.local"
    ];

    /// <summary>The demo parent e-mail addresses, in the order their display names are numbered.</summary>
    public static readonly IReadOnlyList<string> DemoParentEmails =
    [
        "parent.one@educationhub.local",
        "parent.two@educationhub.local"
    ];

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(
            AcademicSeeder.DemoTenantSlug,
            DemoStudentEmails,
            DemoParentEmails,
            cancellationToken);

    /// <summary>Seeds demo students and parents into the tenant with the given slug.</summary>
    /// <remarks>
    /// The slug and e-mail parameters exist so tests can target throwaway tenants with
    /// unique addresses; production code always calls the parameterless overload, which
    /// pins the demo tenant and the production e-mail constants.
    /// </remarks>
    public async Task SeedAsync(
        string tenantSlug,
        IReadOnlyList<string> studentEmails,
        IReadOnlyList<string> parentEmails,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(studentEmails);
        ArgumentNullException.ThrowIfNull(parentEmails);

        var groups = new[]
        {
            new DemoGroup(
                RoleNames.Student,
                Environment.GetEnvironmentVariable(StudentPasswordEnvironmentVariable),
                StudentPasswordEnvironmentVariable,
                studentEmails,
                "Demo Student"),
            new DemoGroup(
                RoleNames.Parent,
                Environment.GetEnvironmentVariable(ParentPasswordEnvironmentVariable),
                ParentPasswordEnvironmentVariable,
                parentEmails,
                "Demo Parent")
        };

        var enabled = groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Password))
            .ToArray();

        if (enabled.Length == 0)
        {
            logger.LogWarning(
                "Neither {StudentVariable} nor {ParentVariable} is set; " +
                "no demo student or parent accounts were created. " +
                "Set them and restart to seed them (docs/SEED_DATA_AND_LOCAL_LOGIN.md §4.5).",
                StudentPasswordEnvironmentVariable,
                ParentPasswordEnvironmentVariable);

            return;
        }

        foreach (var group in groups.Except(enabled))
        {
            logger.LogWarning(
                "{Variable} is not set; demo {Role} accounts were skipped " +
                "(docs/SEED_DATA_AND_LOCAL_LOGIN.md §4.5).",
                group.PasswordVariable,
                group.RoleName);
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
                "No active tenant with slug {Slug}; demo student/parent seed skipped. " +
                "Create the demo tenant through the API first (docs/SEED_DATA_AND_LOCAL_LOGIN.md).",
                tenantSlug);

            return;
        }

        foreach (var group in enabled)
        {
            await SeedGroupAsync(group, tenant, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SeedGroupAsync(
        DemoGroup group,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        // The caller only passes groups whose password variable was set; the guard keeps
        // that invariant explicit for the nullable analysis.
        var password = group.Password;

        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var emails = group.Emails
            .Where(static e => !string.IsNullOrWhiteSpace(e))
            .Select(static e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (emails.Length == 0)
        {
            return;
        }

        // The tenant's own role row (tenant provisioning / RoleSeeder creates it). Checked
        // up front because ApplicationUserStore.AddToRoleAsync throws when the row is
        // absent, and seeding must log and continue, never abort the pipeline.
        var roleExists = await context.Set<ApplicationRole>()
            .IgnoreQueryFilters()
            .AnyAsync(
                r => r.TenantId == tenant.Id && r.Name == group.RoleName,
                cancellationToken)
            .ConfigureAwait(false);

        if (!roleExists)
        {
            logger.LogWarning(
                "Tenant {Slug} has no {Role} role row; its demo accounts were skipped. " +
                "RoleSeeder repairs tenant roles on the next run.",
                tenant.Slug,
                group.RoleName);

            return;
        }

        for (var index = 0; index < emails.Length; index++)
        {
            var email = emails[index];

            var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

            if (existing is not null)
            {
                // Create-if-absent only. Never modify, never reset a password, and never
                // adopt an account that belongs to a different tenant.
                if (existing.TenantId == tenant.Id)
                {
                    logger.LogDebug(
                        "Demo account {Email} already exists in tenant {Slug}; left untouched.",
                        email,
                        tenant.Slug);
                }
                else
                {
                    logger.LogWarning(
                        "E-mail {Email} already belongs to another tenant; " +
                        "no demo account was created and the existing account was left untouched.",
                        email);
                }

                continue;
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FullName = $"{group.FullNamePrefix} {index + 1}",
                TenantId = tenant.Id,
                PreferredLanguage = SupportedLanguages.Default,
                IsActive = true,
                EmailConfirmed = true
            };

            var created = await userManager
                .CreateAsync(user, password)
                .ConfigureAwait(false);

            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create demo account {Email}: {Errors}",
                    email,
                    string.Join("; ", created.Errors.Select(static e => e.Description)));

                continue;
            }

            var assigned = await userManager
                .AddToRoleAsync(user, group.RoleName)
                .ConfigureAwait(false);

            if (!assigned.Succeeded)
            {
                logger.LogError(
                    "Demo account {Email} created but the {Role} role was not assigned: {Errors}",
                    email,
                    group.RoleName,
                    string.Join("; ", assigned.Errors.Select(static e => e.Description)));

                continue;
            }

            logger.LogInformation(
                "Created demo {Role} account {Email} ({FullName}) in tenant {Slug}",
                group.RoleName,
                email,
                user.FullName,
                tenant.Slug);
        }
    }

    /// <summary>One role group: its role, its password source, its addresses, its name numbering.</summary>
    private sealed record DemoGroup(
        string RoleName,
        string? Password,
        string PasswordVariable,
        IReadOnlyList<string> Emails,
        string FullNamePrefix);
}

using EducationHub.Application.Common.Helpers;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Creates the demo staff accounts of the demo tenant - owner, assistant and accountant - so
/// every built-in role can be tried, and gives the demo parents a mobile number so sign-in by
/// phone can be tried too.
/// </summary>
/// <remarks>
/// Same contract as <see cref="DemoTeacherSeeder"/>: the password comes from an environment
/// variable (absent = logged no-op), accounts are created only when their e-mail does not
/// exist, and an existing account is never modified or re-roled. The one write to existing
/// rows is filling a demo parent profile's empty mobile number, and only when no other account
/// or parent already uses that number.
/// </remarks>
public sealed class DemoStaffSeeder(
    EducationHubDbContext context,
    UserManager<ApplicationUser> userManager,
    ILogger<DemoStaffSeeder> logger)
{
    /// <summary>The environment variable supplying the initial password of every demo staff account.</summary>
    public const string PasswordEnvironmentVariable = "EDUCATIONHUB_DEMO_STAFF_PASSWORD";

    /// <summary>The demo staff accounts: e-mail, display name, tenant role.</summary>
    public static readonly IReadOnlyList<(string Email, string FullName, string Role)> DemoStaff =
    [
        ("owner.demo@educationhub.local", "Demo Owner", RoleNames.TenantOwner),
        ("assistant.demo@educationhub.local", "Demo Assistant", RoleNames.Assistant),
        ("accountant.demo@educationhub.local", "Demo Accountant", RoleNames.Accountant)
    ];

    /// <summary>Mobile numbers given to the demo parents' profiles (when they have none).</summary>
    public static readonly IReadOnlyList<(string Email, string Phone)> DemoParentPhones =
    [
        ("parent.one@educationhub.local", "01000000201"),
        ("parent.two@educationhub.local", "01000000202")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == AcademicSeeder.DemoTenantSlug && !t.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogWarning("No active tenant with slug {Slug}; demo staff seed skipped.", AcademicSeeder.DemoTenantSlug);
            return;
        }

        await SeedStaffAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await SeedParentPhonesAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedStaffAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "{Variable} is not set; no demo staff accounts were created (docs/SEED_DATA_AND_LOCAL_LOGIN.md).",
                PasswordEnvironmentVariable);

            return;
        }

        foreach (var (email, fullName, role) in DemoStaff)
        {
            var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

            if (existing is not null)
            {
                logger.LogDebug("Demo staff account {Email} already exists; left untouched.", email);
                continue;
            }

            var roleExists = await context.Set<ApplicationRole>()
                .IgnoreQueryFilters()
                .AnyAsync(r => r.TenantId == tenantId && r.Name == role, cancellationToken)
                .ConfigureAwait(false);

            if (!roleExists)
            {
                logger.LogWarning("Demo tenant has no {Role} role row; {Email} skipped.", role, email);
                continue;
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FullName = fullName,
                TenantId = tenantId,
                PreferredLanguage = SupportedLanguages.Default,
                IsActive = true,
                EmailConfirmed = true
            };

            var created = await userManager.CreateAsync(user, password).ConfigureAwait(false);

            if (!created.Succeeded)
            {
                logger.LogError(
                    "Could not create demo staff account {Email}: {Errors}",
                    email,
                    string.Join("; ", created.Errors.Select(static e => e.Description)));

                continue;
            }

            var assigned = await userManager.AddToRoleAsync(user, role).ConfigureAwait(false);

            if (!assigned.Succeeded)
            {
                logger.LogError(
                    "Demo staff account {Email} created but the {Role} role was not assigned: {Errors}",
                    email,
                    role,
                    string.Join("; ", assigned.Errors.Select(static e => e.Description)));

                continue;
            }

            logger.LogInformation("Created demo staff account {Email} ({Role})", email, role);
        }
    }

    private async Task SeedParentPhonesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var (email, phone) in DemoParentPhones)
        {
            var account = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

            if (account is null || account.TenantId != tenantId)
            {
                continue;
            }

            var profile = await context.ParentProfiles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.UserId == account.Id && !p.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (profile is null || !string.IsNullOrWhiteSpace(profile.PhoneNumber))
            {
                continue;
            }

            var canonical = PhoneNumberFormat.ToCanonical(phone)!;
            var variants = PhoneNumberFormat.LookupVariants(canonical).ToArray();

            var taken = await context.Users.IgnoreQueryFilters()
                    .AnyAsync(u => u.PhoneNumber != null && variants.Contains(u.PhoneNumber), cancellationToken)
                    .ConfigureAwait(false)
                || await context.ParentProfiles.IgnoreQueryFilters()
                    .AnyAsync(p => p.PhoneNumber != null && variants.Contains(p.PhoneNumber), cancellationToken)
                    .ConfigureAwait(false);

            if (taken)
            {
                logger.LogWarning("Mobile {Phone} is already in use; demo parent {Email} keeps no number.", phone, email);
                continue;
            }

            profile.PhoneNumber = canonical;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Demo parent {Email} can now also sign in with {Phone}", email, phone);
        }
    }
}

using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Mission Phase 2.5 demo data: gives the demo student and parent accounts (seeded by
/// <see cref="DemoStudentParentSeeder"/>) their profiles, and links parent N to student N.
/// </summary>
/// <remarks>
/// <para>
/// Add-only and idempotent, like every seeder here: an account that already has a profile
/// (including a soft-deleted one) is left alone, an existing link is never changed, and
/// nothing is ever deleted or overwritten. Accounts that do not exist (their password
/// variable was unset) are simply skipped.
/// </para>
/// <para>
/// Runs after <see cref="DemoStudentParentSeeder"/> in <see cref="DatabaseSeeder"/>.
/// </para>
/// </remarks>
public sealed class DemoStudentProfileSeeder(
    EducationHubDbContext context,
    ILogger<DemoStudentProfileSeeder> logger)
{
    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(
            AcademicSeeder.DemoTenantSlug,
            DemoStudentParentSeeder.DemoStudentEmails,
            DemoStudentParentSeeder.DemoParentEmails,
            cancellationToken);

    public async Task SeedAsync(
        string tenantSlug,
        IReadOnlyList<string> studentEmails,
        IReadOnlyList<string> parentEmails,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);
        ArgumentNullException.ThrowIfNull(studentEmails);
        ArgumentNullException.ThrowIfNull(parentEmails);

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug && !t.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogInformation("No active tenant with slug {Slug}; demo profile seed skipped.", tenantSlug);
            return;
        }

        var studentIds = new List<Guid?>();
        var parentIds = new List<Guid?>();
        var created = 0;

        foreach (var email in studentEmails)
        {
            var (id, isNew) = await EnsureStudentAsync(tenant.Id, email, cancellationToken).ConfigureAwait(false);
            studentIds.Add(id);
            created += isNew ? 1 : 0;
        }

        foreach (var email in parentEmails)
        {
            var (id, isNew) = await EnsureParentAsync(tenant.Id, email, cancellationToken).ConfigureAwait(false);
            parentIds.Add(id);
            created += isNew ? 1 : 0;
        }

        var links = 0;

        for (var i = 0; i < Math.Min(studentIds.Count, parentIds.Count); i++)
        {
            if (studentIds[i] is not { } studentId || parentIds[i] is not { } parentId)
            {
                continue;
            }

            var linked = await context.StudentGuardians
                .IgnoreQueryFilters()
                .AnyAsync(
                    g => g.StudentProfileId == studentId && g.ParentProfileId == parentId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (linked)
            {
                continue;
            }

            var hasPrimary = await context.StudentGuardians
                .IgnoreQueryFilters()
                .AnyAsync(g => g.StudentProfileId == studentId && g.IsPrimary && !g.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            context.StudentGuardians.Add(new StudentGuardian
            {
                TenantId = tenant.Id,
                StudentProfileId = studentId,
                ParentProfileId = parentId,
                Relationship = GuardianRelationship.Guardian,
                IsPrimary = !hasPrimary
            });

            links++;
        }

        if (created > 0 || links > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Demo profile seed for {Slug}: {Profiles} profile(s) and {Links} guardian link(s) created.",
            tenantSlug,
            created,
            links);
    }

    /// <summary>
    /// The live student profile for the account, creating one when the account has none.
    /// Null when the account does not exist or its only profile was soft-deleted.
    /// </summary>
    private async Task<(Guid? Id, bool IsNew)> EnsureStudentAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken)
    {
        var user = await FindAccountAsync(tenantId, email, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return (null, false);
        }

        var existing = await context.StudentProfiles
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.UserId == user.Value.Id)
            .Select(static s => new { s.Id, s.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return (existing.IsDeleted ? null : existing.Id, false);
        }

        var profile = new StudentProfile
        {
            TenantId = tenantId,
            UserId = user.Value.Id,
            FullName = user.Value.FullName,
            Status = StudentStatus.Active
        };

        context.StudentProfiles.Add(profile);

        return (profile.Id, true);
    }

    private async Task<(Guid? Id, bool IsNew)> EnsureParentAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken)
    {
        var user = await FindAccountAsync(tenantId, email, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return (null, false);
        }

        var existing = await context.ParentProfiles
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.UserId == user.Value.Id)
            .Select(static p => new { p.Id, p.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return (existing.IsDeleted ? null : existing.Id, false);
        }

        var profile = new ParentProfile
        {
            TenantId = tenantId,
            UserId = user.Value.Id,
            FullName = user.Value.FullName,
            Email = user.Value.Email
        };

        context.ParentProfiles.Add(profile);

        return (profile.Id, true);
    }

    private async Task<(Guid Id, string FullName, string? Email)?> FindAccountAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToUpperInvariant();

        var account = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.NormalizedEmail == normalized)
            .Select(static u => new { u.Id, u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return account is null ? null : (account.Id, account.FullName, account.Email);
    }
}

using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Adds the academic catalog (current year, grade levels, subjects) to the demo tenant.
/// </summary>
/// <remarks>
/// <para>
/// Add-only and idempotent, keyed the way the database keys: year by (TenantId, Name),
/// grades and subjects by (TenantId, Code). An existing row - including a soft-deleted one,
/// because none of the three unique indexes is filtered on IsDeleted - is skipped entirely:
/// never updated, never resurrected. Restarting repeatedly is a no-op after the first run.
/// </para>
/// <para>
/// Scoped to the tenant with slug <c>admin-ui-test</c>. A missing tenant is a logged no-op,
/// the same warn-and-skip philosophy <see cref="AdminUserSeeder"/> applies to missing
/// credentials: a fresh database has no demo tenant (it is created through the API, see
/// docs/SEED_DATA_AND_LOCAL_LOGIN.md), and seeding must not invent one.
/// </para>
/// <para>
/// Promoting 2026/2027 demotes whichever year is current using exactly the rules the
/// product's own SetCurrent command applies (<c>AcademicCommandSupport.ClearCurrentAsync</c>):
/// <c>IsCurrent</c> goes false and a <c>Current</c> status becomes <c>Upcoming</c>. Name,
/// dates, archive flag and every other column of the demoted year are left untouched, so
/// history stays queryable. The demotion is committed before the insert because the
/// filtered unique index <c>IX_AcademicYears_TenantId_IsCurrent</c> allows only one current
/// year per tenant and a single SaveChanges gives no ordering guarantee between the two.
/// </para>
/// </remarks>
public sealed class AcademicSeeder(
    EducationHubDbContext context,
    ILogger<AcademicSeeder> logger)
{
    /// <summary>The demo tenant created through the API; the only tenant the default run touches.</summary>
    public const string DemoTenantSlug = "admin-ui-test";

    private const string CurrentYearName = "2026/2027";

    private static readonly DateOnly CurrentYearStart = new(2026, 9, 1);

    private static readonly DateOnly CurrentYearEnd = new(2027, 6, 30);

    /// <summary>
    /// Canonical grade list, KG first. <c>G1</c> is present because the seeder must also
    /// work on a tenant that has no grades at all; on the real demo tenant the existing G1
    /// row wins the (TenantId, Code) lookup and is skipped untouched.
    /// </summary>
    private static readonly (string Code, string Name, string NameAr)[] GradeCatalog =
    [
        ("KG1", "KG1", "روضة 1"),
        ("KG2", "KG2", "روضة 2"),
        ("G1", "Grade 1", "الصف الأول"),
        ("G2", "Grade 2", "الصف الثاني"),
        ("G3", "Grade 3", "الصف الثالث"),
        ("G4", "Grade 4", "الصف الرابع"),
        ("G5", "Grade 5", "الصف الخامس"),
        ("G6", "Grade 6", "الصف السادس")
    ];

    private static readonly (string Code, string Name, string NameAr)[] SubjectCatalog =
    [
        ("ARB", "Arabic", "العربية"),
        ("ENG", "English", "الإنجليزية"),
        ("MATH", "Mathematics", "الرياضيات"),
        ("SCI", "Science", "العلوم"),
        ("SOC", "Social Studies", "الدراسات الاجتماعية")
    ];

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(DemoTenantSlug, cancellationToken);

    /// <summary>Seeds the catalog into the tenant with the given slug.</summary>
    /// <remarks>
    /// The slug parameter exists so tests can target a throwaway tenant; production code
    /// always calls the parameterless overload, which pins <see cref="DemoTenantSlug"/>.
    /// </remarks>
    public async Task SeedAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantSlug);

        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.Slug == tenantSlug && !t.IsDeleted,
                cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogWarning(
                "No active tenant with slug {Slug}; academic catalog seed skipped. " +
                "Create the demo tenant through the API first (docs/SEED_DATA_AND_LOCAL_LOGIN.md).",
                tenantSlug);

            return;
        }

        await SeedAcademicYearAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await SeedGradeLevelsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await SeedSubjectsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedAcademicYearAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: the (TenantId, Name) index is not filtered on IsDeleted, so a
        // soft-deleted year with this name still blocks the insert.
        var existing = await context.AcademicYears
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                y => y.TenantId == tenantId && y.Name == CurrentYearName,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Add-only: an existing year is never edited, not even to make it current again.
            logger.LogDebug(
                "Academic year {Name} already exists for tenant {TenantId}; left untouched.",
                CurrentYearName,
                tenantId);

            return;
        }

        // Date overlap is enforced by the API but not by the database, and this seeder
        // writes below the API. Refuse rather than create two years claiming the same dates.
        var overlapping = await context.AcademicYears
            .IgnoreQueryFilters()
            .Where(y => y.TenantId == tenantId
                && !y.IsDeleted
                && y.StartDate <= CurrentYearEnd
                && y.EndDate >= CurrentYearStart)
            .Select(y => y.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (overlapping.Count > 0)
        {
            logger.LogWarning(
                "Academic year {Name} ({Start:yyyy-MM-dd} - {End:yyyy-MM-dd}) overlaps " +
                "{Existing}; the year was NOT seeded. Resolve the overlap through the API.",
                CurrentYearName,
                CurrentYearStart,
                CurrentYearEnd,
                string.Join(", ", overlapping));

            return;
        }

        var demoted = await DemoteCurrentYearAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var year = new AcademicYear
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = CurrentYearName,
            NameAr = CurrentYearName,
            StartDate = CurrentYearStart,
            EndDate = CurrentYearEnd,
            Status = AcademicYearStatus.Current,
            IsCurrent = true,
            IsArchived = false
        };

        context.AcademicYears.Add(year);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded academic year {Name} ({Start:yyyy-MM-dd} - {End:yyyy-MM-dd}) as current " +
            "for tenant {TenantId}{Demotion}",
            year.Name,
            year.StartDate,
            year.EndDate,
            tenantId,
            demoted is null ? string.Empty : $"; demoted {demoted} to Upcoming");
    }

    /// <summary>
    /// Clears the current-year flag using the product's own SetCurrent semantics and
    /// reports which year was demoted, or null when the tenant had none.
    /// </summary>
    private async Task<string?> DemoteCurrentYearAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var current = await context.AcademicYears
            .Where(y => y.TenantId == tenantId && y.IsCurrent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (current.Count == 0)
        {
            return null;
        }

        var names = new List<string>(current.Count);

        foreach (var year in current)
        {
            logger.LogInformation(
                "Demoting academic year {Name} ({Start:yyyy-MM-dd} - {End:yyyy-MM-dd}): " +
                "IsCurrent false, Status Current -> Upcoming. All other columns untouched.",
                year.Name,
                year.StartDate,
                year.EndDate);

            year.IsCurrent = false;

            if (year.Status == AcademicYearStatus.Current)
            {
                year.Status = AcademicYearStatus.Upcoming;
            }

            names.Add(year.Name);
        }

        // Separate save so the demotion lands before the insert: the filtered unique index
        // on (TenantId) WHERE IsCurrent = 1 would reject an insert that races the update.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return string.Join(", ", names);
    }

    private async Task SeedGradeLevelsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // Soft-deleted rows are included: their Code and Order still occupy the (unfiltered)
        // unique indexes, so they must block re-insertion and be counted for order allocation.
        var existing = await context.GradeLevels
            .IgnoreQueryFilters()
            .Where(g => g.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var codes = existing.Select(static g => g.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // New grades sort after everything the tenant already has. The demo tenant's G1 was
        // created through the API at Order 1 and is never moved (add-only), so KG1..G6 are
        // appended; a tenant with no grades starts at 1 and gets the canonical order.
        var nextOrder = existing.Count == 0 ? 1 : existing.Max(static g => g.Order) + 1;

        var added = new List<string>(GradeCatalog.Length);

        foreach (var (code, name, nameAr) in GradeCatalog)
        {
            if (!codes.Add(code))
            {
                logger.LogDebug(
                    "Grade {Code} already exists for tenant {TenantId}; left untouched.",
                    code,
                    tenantId);

                continue;
            }

            context.GradeLevels.Add(new GradeLevel
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                NameAr = nameAr,
                Order = nextOrder++,
                IsActive = true
            });

            added.Add(code);
        }

        if (added.Count == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded {Count} grade level(s) for tenant {TenantId}: {Codes}",
            added.Count,
            tenantId,
            string.Join(", ", added));
    }

    private async Task SeedSubjectsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existingCodes = await context.Subjects
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .Select(static s => s.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var codes = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<string>(SubjectCatalog.Length);

        foreach (var (code, name, nameAr) in SubjectCatalog)
        {
            if (!codes.Add(code))
            {
                logger.LogDebug(
                    "Subject {Code} already exists for tenant {TenantId}; left untouched.",
                    code,
                    tenantId);

                continue;
            }

            context.Subjects.Add(new Subject
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = code,
                Name = name,
                NameAr = nameAr,
                IsActive = true,
                SortOrder = codes.Count
            });

            added.Add(code);
        }

        if (added.Count == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded {Count} subject(s) for tenant {TenantId}: {Codes}",
            added.Count,
            tenantId,
            string.Join(", ", added));
    }
}

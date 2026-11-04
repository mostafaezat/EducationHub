using EducationHub.API.Seed;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The academic catalog seed's contract: add-only, idempotent, scoped to its tenant,
/// and using the product's own semantics when it demotes a stale current year.
/// </summary>
/// <remarks>
/// Every test owns a tenant with a unique slug: IX_Tenants_Slug is platform-unique, the
/// collection shares one database, and a fixed slug would make the tests collide with each
/// other rather than with the seeder. The seeder's parameterless overload (the production
/// path, pinned to <see cref="AcademicSeeder.DemoTenantSlug"/>) is covered by the no-op
/// test's argument check and by the pipeline test in <see cref="SeedingTests"/>.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AcademicSeedingTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task Seed_adds_the_catalog_and_demotes_the_stale_current_year()
    {
        var (slug, tenantId) = await CreateTenantWithLegacyDataAsync();

        await RunSeederAsync(slug);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var years = await db.AcademicYears
                .Where(y => y.TenantId == tenantId)
                .OrderBy(y => y.StartDate)
                .ToListAsync();

            Assert.Equal(2, years.Count);

            var stale = Assert.Single(years, y => y.Name == "2025/2026");
            Assert.False(stale.IsCurrent);
            Assert.Equal(AcademicYearStatus.Upcoming, stale.Status);

            // Demotion is flag-and-status only: history must stay exactly as it was.
            Assert.Equal(new DateOnly(2025, 9, 1), stale.StartDate);
            Assert.Equal(new DateOnly(2026, 6, 30), stale.EndDate);
            Assert.False(stale.IsArchived);
            Assert.False(stale.IsDeleted);

            var current = Assert.Single(years, y => y.Name == "2026/2027");
            Assert.True(current.IsCurrent);
            Assert.Equal(AcademicYearStatus.Current, current.Status);
            Assert.Equal(new DateOnly(2026, 9, 1), current.StartDate);
            Assert.Equal(new DateOnly(2027, 6, 30), current.EndDate);

            var grades = await db.GradeLevels
                .Where(g => g.TenantId == tenantId)
                .OrderBy(g => g.Order)
                .ToListAsync();

            // 8 catalog grades (KG1..G6): the pre-existing legacy G1 is skipped by code,
            // not duplicated, so 8 rows total.
            Assert.Equal(8, grades.Count);

            var legacyG1 = Assert.Single(grades, g => g.Code == "G1");
            Assert.Equal("Grade 1 (legacy)", legacyG1.Name);
            Assert.Equal(1, legacyG1.Order);

            Assert.Equal(
                ["G1", "KG1", "KG2", "G2", "G3", "G4", "G5", "G6"],
                grades.Select(g => g.Code).ToArray());

            // Orders are unique per tenant (database-enforced) and the legacy row keeps 1;
            // the seven new grades take the next free orders 2..8.
            Assert.Equal(grades.Count, grades.Select(g => g.Order).Distinct().Count());
            Assert.Equal(Enumerable.Range(2, 7), grades.Where(g => g.Code != "G1").Select(g => g.Order));

            var subjects = await db.Subjects
                .Where(s => s.TenantId == tenantId)
                .ToListAsync();

            Assert.Equal(5, subjects.Count);
            Assert.Contains(subjects, s => s.Code == "ARB" && s.Name == "Arabic");
            Assert.Contains(subjects, s => s.Code == "ENG" && s.Name == "English");
            Assert.Contains(subjects, s => s.Code == "SCI" && s.Name == "Science");
            Assert.Contains(subjects, s => s.Code == "SOC" && s.Name == "Social Studies");

            var math = Assert.Single(subjects, s => s.Code == "MATH");
            Assert.Equal("Mathematics (legacy)", math.Name);
        });
    }

    [Fact]
    public async Task Seed_is_idempotent_and_never_modifies_existing_rows()
    {
        var (slug, tenantId) = await CreateTenantWithLegacyDataAsync();

        await RunSeederAsync(slug);
        await RunSeederAsync(slug);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            Assert.Equal(2, await db.AcademicYears.CountAsync(y => y.TenantId == tenantId));
            Assert.Equal(8, await db.GradeLevels.CountAsync(g => g.TenantId == tenantId));
            Assert.Equal(5, await db.Subjects.CountAsync(s => s.TenantId == tenantId));

            // Exactly one current year survives the double run.
            Assert.Equal(1, await db.AcademicYears.CountAsync(y => y.TenantId == tenantId && y.IsCurrent));

            // The legacy rows are still untouched after the second pass.
            var legacyG1 = await db.GradeLevels
                .SingleAsync(g => g.TenantId == tenantId && g.Code == "G1");
            Assert.Equal("Grade 1 (legacy)", legacyG1.Name);
            Assert.Equal(1, legacyG1.Order);

            var math = await db.Subjects.SingleAsync(s => s.TenantId == tenantId && s.Code == "MATH");
            Assert.Equal("Mathematics (legacy)", math.Name);
        });
    }

    [Fact]
    public async Task Seed_never_touches_another_tenant()
    {
        var (demoSlug, demoId) = await CreateTenantWithLegacyDataAsync();
        var (_, controlId) = await CreateTenantWithLegacyDataAsync(prefix: "control");

        await RunSeederAsync(demoSlug);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // The control tenant keeps exactly what it had: its own legacy rows, nothing else.
            Assert.Equal(1, await db.GradeLevels.CountAsync(g => g.TenantId == controlId));
            Assert.Equal(1, await db.Subjects.CountAsync(s => s.TenantId == controlId));
            Assert.Equal(1, await db.AcademicYears.CountAsync(y => y.TenantId == controlId));

            var controlG1 = await db.GradeLevels.SingleAsync(g => g.TenantId == controlId);
            Assert.Equal("Grade 1 (legacy)", controlG1.Name);

            var controlYear = await db.AcademicYears.SingleAsync(y => y.TenantId == controlId);
            Assert.True(controlYear.IsCurrent);

            // And the demo tenant was seeded regardless.
            Assert.Equal(8, await db.GradeLevels.CountAsync(g => g.TenantId == demoId));
            Assert.Equal(5, await db.Subjects.CountAsync(s => s.TenantId == demoId));
            Assert.Equal(2, await db.AcademicYears.CountAsync(y => y.TenantId == demoId));
        });
    }

    [Fact]
    public async Task Seed_without_a_matching_tenant_changes_nothing()
    {
        var missingSlug = $"missing-{Guid.NewGuid():N}";

        // Before/after deltas: sibling tests in the shared database own rows too, so a
        // global zero-count would assert their state, not the seeder's.
        var before = await CountCatalogRowsAsync();

        await RunSeederAsync(missingSlug);

        var after = await CountCatalogRowsAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Seed_respects_soft_deleted_rows_holding_unique_codes()
    {
        var (slug, tenantId) = await CreateTenantAsync(prefix: "softdel", withLegacyData: false);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            // The (TenantId, Code) indexes are not filtered on IsDeleted, so a soft-deleted
            // KG1 still owns the code and the seeder must not insert a duplicate (which the
            // database would reject) nor resurrect the row.
            db.GradeLevels.Add(new GradeLevel
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = "KG1",
                Name = "Removed KG1",
                Order = 1,
                IsActive = false,
                IsDeleted = true
            });

            await db.SaveChangesAsync();
        });

        await RunSeederAsync(slug);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var kg1 = await db.GradeLevels
                .IgnoreQueryFilters()
                .Where(g => g.TenantId == tenantId && g.Code == "KG1")
                .ToListAsync();

            var row = Assert.Single(kg1);
            Assert.True(row.IsDeleted);
            Assert.Equal("Removed KG1", row.Name);
        });
    }

    private async Task RunSeederAsync(string tenantSlug)
    {
        // Await inside the usings, like SeedingTests does: disposing the scope while the
        // seeder is still running would close its DbContext connection mid-query.
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();

        await scope.ServiceProvider.GetRequiredService<AcademicSeeder>().SeedAsync(tenantSlug);
    }

    private async Task<int[]> CountCatalogRowsAsync()
    {
        int[] counts = [];

        await fixture.WithPlatformScopeAsync(async db =>
        {
            counts =
            [
                await db.AcademicYears.IgnoreQueryFilters().CountAsync(),
                await db.GradeLevels.IgnoreQueryFilters().CountAsync(),
                await db.Subjects.IgnoreQueryFilters().CountAsync()
            ];
        });

        return counts;
    }

    /// <summary>
    /// Mirrors the real demo tenant's starting state: a legacy G1 at Order 1, a MATH
    /// subject, and a stale current year 2025/2026 - exactly what the API had produced in
    /// the development database - under a slug unique to the calling test.
    /// </summary>
    private Task<(string Slug, Guid TenantId)> CreateTenantWithLegacyDataAsync(string prefix = "seed")
        => CreateTenantAsync(prefix, withLegacyData: true);

    private async Task<(string Slug, Guid TenantId)> CreateTenantAsync(
        string prefix,
        bool withLegacyData)
    {
        // Slug max length is 100; unique index is platform-wide and not IsDeleted-filtered.
        var slug = $"{prefix}-{Guid.NewGuid():N}";
        var tenantId = Guid.NewGuid();

        await fixture.WithPlatformScopeAsync(async db =>
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Slug = slug,
                Name = $"Seeded Academy {prefix}",
                Type = TenantType.EducationCenter,
                Status = TenantStatus.Active,
                Currency = "EGP",
                TimeZone = "Africa/Cairo",
                DefaultLanguage = "ar",
                IsActive = true,
                SubscriptionStartDate = DateTime.UtcNow,
                SubscriptionExpiryDate = DateTime.UtcNow.AddYears(1)
            });

            if (withLegacyData)
            {
                db.GradeLevels.Add(new GradeLevel
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = "G1",
                    Name = "Grade 1 (legacy)",
                    Order = 1,
                    IsActive = true
                });

                db.Subjects.Add(new Subject
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = "MATH",
                    Name = "Mathematics (legacy)",
                    NameAr = "الرياضيات",
                    IsActive = true,
                    SortOrder = 1
                });

                db.AcademicYears.Add(new AcademicYear
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "2025/2026",
                    StartDate = new DateOnly(2025, 9, 1),
                    EndDate = new DateOnly(2026, 6, 30),
                    Status = AcademicYearStatus.Current,
                    IsCurrent = true,
                    IsArchived = false
                });
            }

            await db.SaveChangesAsync();
        });

        return (slug, tenantId);
    }
}

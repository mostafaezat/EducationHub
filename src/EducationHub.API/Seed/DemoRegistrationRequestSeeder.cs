using EducationHub.Application.Common.Helpers;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EducationHub.API.Seed;

/// <summary>
/// Pending pre-registration requests for the demo tenant, so the staff review page has something
/// to approve or reject. The third one uses demo parent 1's mobile number, so approving it shows
/// an existing parent being linked instead of duplicated.
/// </summary>
/// <remarks>
/// Add-only and idempotent: a request is keyed by mobile number + student name, and is skipped
/// when any row with that key exists (any status, deleted included) - an approved or rejected demo
/// request is never put back to pending. No accounts or passwords are involved.
/// </remarks>
public sealed class DemoRegistrationRequestSeeder(
    EducationHubDbContext context,
    ILogger<DemoRegistrationRequestSeeder> logger)
{
    /// <summary>Guardian name, mobile, relationship, student name, notes.</summary>
    public static readonly IReadOnlyList<(string Guardian, string Phone, GuardianRelationship Relationship, string Student, string? Notes)> DemoRequests =
    [
        ("Ali Hassan", "01000000101", GuardianRelationship.Father, "Youssef Ali", "Prefers evening groups."),
        ("Mona Adel", "01000000102", GuardianRelationship.Mother, "Salma Khaled", null),
        ("Demo Parent 1", "01000000001", GuardianRelationship.Father, "Demo Student 3", "Sibling of a current student.")
    ];

    public Task<int> SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(AcademicSeeder.DemoTenantSlug, cancellationToken);

    /// <summary>Seeds the requests into the tenant with <paramref name="tenantSlug"/> (tests use their own tenant).</summary>
    public async Task<int> SeedAsync(string tenantSlug, CancellationToken cancellationToken = default)
    {
        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug && !t.IsDeleted, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            logger.LogWarning("No active tenant with slug {Slug}; demo registration requests skipped.", tenantSlug);
            return 0;
        }

        var grade = await context.GradeLevels
            .IgnoreQueryFilters()
            .Where(g => g.TenantId == tenant.Id && !g.IsDeleted && g.IsActive)
            .OrderBy(static g => g.Order)
            .Select(static g => (Guid?)g.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = 0;
        var age = 0;

        foreach (var (guardian, rawPhone, relationship, student, notes) in DemoRequests)
        {
            var phone = PhoneNumberFormat.ToCanonical(rawPhone)!;

            var exists = await context.RegistrationRequests
                .IgnoreQueryFilters()
                .AnyAsync(r => r.TenantId == tenant.Id && r.GuardianPhone == phone && r.StudentName == student, cancellationToken)
                .ConfigureAwait(false);

            age++;

            if (exists)
            {
                continue;
            }

            context.RegistrationRequests.Add(new RegistrationRequest
            {
                TenantId = tenant.Id,
                GuardianName = guardian,
                GuardianPhone = phone,
                Relationship = relationship,
                StudentName = student,
                BirthDate = new DateOnly(2015 + age, 3, 10),
                GradeLevelId = grade,
                Notes = notes,
                Status = RegistrationRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddHours(-6 * age)
            });

            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded {Count} demo registration requests for {Slug}.", added, tenantSlug);
        }

        return added;
    }
}

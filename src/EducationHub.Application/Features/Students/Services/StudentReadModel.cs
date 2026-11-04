using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Services;

/// <summary>
/// Builds the student and parent DTOs (with display names and guardian links) for a page
/// of profiles in a fixed number of round trips, shared by every query and command so the
/// payload shape is defined once.
/// </summary>
/// <remarks>
/// Every repository here carries the tenant + soft-delete filter, so a link to a deleted
/// parent or student simply drops out of the payload rather than leaking.
/// </remarks>
public sealed class StudentReadModel(
    IRepository<StudentGuardian> guardians,
    IRepository<StudentProfile> students,
    IRepository<ParentProfile> parents,
    IRepository<GradeLevel> grades,
    IRepository<Branch> branches,
    UserManager<ApplicationUser> userManager)
{
    public async Task<IReadOnlyList<StudentProfileDto>> BuildStudentsAsync(
        IReadOnlyList<StudentProfile> profiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return [];
        }

        var ids = profiles.Select(static p => p.Id).ToArray();

        var links = await guardians.Query
            .Where(g => ids.Contains(g.StudentProfileId))
            .Join(
                parents.Query,
                g => g.ParentProfileId,
                p => p.Id,
                (g, p) => new { g.StudentProfileId, Link = g, Parent = p })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var linksByStudent = links
            .GroupBy(static x => x.StudentProfileId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<GuardianLinkDto>)g
                    .OrderByDescending(static x => x.Link.IsPrimary)
                    .ThenBy(static x => x.Parent.FullName)
                    .Select(static x => new GuardianLinkDto(
                        x.Parent.Id,
                        x.Parent.FullName,
                        x.Parent.PhoneNumber,
                        x.Link.Relationship,
                        x.Link.IsPrimary))
                    .ToList());

        var gradeNames = await LoadGradeNamesAsync(
                profiles.Select(static p => p.GradeLevelId),
                cancellationToken)
            .ConfigureAwait(false);

        var branchIds = profiles
            .Where(static p => p.BranchId.HasValue)
            .Select(static p => p.BranchId!.Value)
            .Distinct()
            .ToArray();

        var branchNames = branchIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await branches.Query
                .Where(b => branchIds.Contains(b.Id))
                .ToDictionaryAsync(static b => b.Id, static b => b.Name, cancellationToken)
                .ConfigureAwait(false);

        var emails = await LoadAccountEmailsAsync(
                profiles.Select(static p => p.UserId),
                cancellationToken)
            .ConfigureAwait(false);

        return profiles
            .Select(p => new StudentProfileDto(
                p.Id,
                p.UserId,
                p.UserId.HasValue ? emails.GetValueOrDefault(p.UserId.Value) : null,
                p.FullName,
                p.StudentNumber,
                p.DateOfBirth,
                p.GradeLevelId,
                p.GradeLevelId.HasValue ? gradeNames.GetValueOrDefault(p.GradeLevelId.Value) : null,
                p.BranchId,
                p.BranchId.HasValue ? branchNames.GetValueOrDefault(p.BranchId.Value) : null,
                p.Status,
                p.EnrollmentDate,
                p.Notes,
                linksByStudent.GetValueOrDefault(p.Id, []),
                p.CreatedAt))
            .ToList();
    }

    public async Task<StudentProfileDto> BuildStudentAsync(
        StudentProfile profile,
        CancellationToken cancellationToken)
        => (await BuildStudentsAsync([profile], cancellationToken).ConfigureAwait(false))[0];

    public async Task<IReadOnlyList<ParentProfileDto>> BuildParentsAsync(
        IReadOnlyList<ParentProfile> profiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return [];
        }

        var children = await LoadChildrenAsync(
                profiles.Select(static p => p.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        var emails = await LoadAccountEmailsAsync(
                profiles.Select(static p => p.UserId),
                cancellationToken)
            .ConfigureAwait(false);

        return profiles
            .Select(p => new ParentProfileDto(
                p.Id,
                p.UserId,
                p.UserId.HasValue ? emails.GetValueOrDefault(p.UserId.Value) : null,
                p.FullName,
                p.PhoneNumber,
                p.Email,
                p.Notes,
                children.GetValueOrDefault(p.Id, []),
                p.CreatedAt))
            .ToList();
    }

    public async Task<ParentProfileDto> BuildParentAsync(
        ParentProfile profile,
        CancellationToken cancellationToken)
        => (await BuildParentsAsync([profile], cancellationToken).ConfigureAwait(false))[0];

    /// <summary>The students linked to each of the given parents.</summary>
    public async Task<Dictionary<Guid, IReadOnlyList<ChildLinkDto>>> LoadChildrenAsync(
        IReadOnlyCollection<Guid> parentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentIds);

        if (parentIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ChildLinkDto>>();
        }

        var ids = parentIds.ToArray();

        var rows = await guardians.Query
            .Where(g => ids.Contains(g.ParentProfileId))
            .Join(
                students.Query,
                g => g.StudentProfileId,
                s => s.Id,
                (g, s) => new { g.ParentProfileId, Link = g, Student = s })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var gradeNames = await LoadGradeNamesAsync(
                rows.Select(static r => r.Student.GradeLevelId),
                cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static r => r.ParentProfileId)
            .ToDictionary(
                static g => g.Key,
                g => (IReadOnlyList<ChildLinkDto>)g
                    .OrderBy(static r => r.Student.FullName)
                    .Select(r => new ChildLinkDto(
                        r.Student.Id,
                        r.Student.FullName,
                        r.Student.StudentNumber,
                        r.Student.GradeLevelId.HasValue
                            ? gradeNames.GetValueOrDefault(r.Student.GradeLevelId.Value)
                            : null,
                        r.Student.Status,
                        r.Link.Relationship,
                        r.Link.IsPrimary))
                    .ToList());
    }

    private async Task<Dictionary<Guid, string>> LoadGradeNamesAsync(
        IEnumerable<Guid?> gradeIds,
        CancellationToken cancellationToken)
    {
        var ids = gradeIds
            .Where(static id => id.HasValue)
            .Select(static id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await grades.Query
            .Where(g => ids.Contains(g.Id))
            .ToDictionaryAsync(static g => g.Id, static g => g.Name, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, string>> LoadAccountEmailsAsync(
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds
            .Where(static id => id.HasValue)
            .Select(static id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(static u => u.Id, static u => u.Email ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }
}

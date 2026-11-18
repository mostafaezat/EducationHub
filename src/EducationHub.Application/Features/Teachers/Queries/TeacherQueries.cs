using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Application.Features.Teachers.Services;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Teachers.Queries;

/// <summary>
/// Pages the tenant's teachers. Search matches name, e-mail and staff number; the join on
/// the account is what makes name/e-mail searchable, and both sides carry the tenant
/// filter, so isolation is unaffected by the join.
/// </summary>
public sealed record GetTeachersQuery(PaginationRequest Pagination, string? Search, bool ActiveOnly)
    : IRequest<Result<PaginatedList<TeacherProfileDto>>>;

public sealed class GetTeachersQueryHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<TeacherSubject> teacherSubjects,
    IRepository<Subject> subjects,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetTeachersQuery, Result<PaginatedList<TeacherProfileDto>>>
{
    public async Task<Result<PaginatedList<TeacherProfileDto>>> Handle(
        GetTeachersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query =
            from profile in profiles.Query
            join account in userManager.Users on profile.UserId equals account.Id
            select new { profile, account };

        if (request.ActiveOnly)
        {
            query = query.Where(static x => x.profile.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(x =>
                x.account.FullName.Contains(term)
                || (x.account.Email != null && x.account.Email.Contains(term))
                || (x.profile.EmployeeNumber != null && x.profile.EmployeeNumber.Contains(term)));
        }

        var page = await query
            .OrderBy(static x => x.account.FullName)
            .ThenBy(static x => x.profile.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var assigned = await LoadSubjectsAsync(
                page.Items.Select(static x => x.profile.Id),
                cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items
            .Select(x => TeacherProfileDto.FromEntity(
                x.profile,
                x.account,
                assigned.GetValueOrDefault(x.profile.Id, [])))
            .ToArray();

        return Result<PaginatedList<TeacherProfileDto>>.Success(
            new PaginatedList<TeacherProfileDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }

    private async Task<Dictionary<Guid, List<Subject>>> LoadSubjectsAsync(
        IEnumerable<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        var ids = profileIds.ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, List<Subject>>();
        }

        var rows = await teacherSubjects.Query
            .Where(t => ids.Contains(t.TeacherProfileId))
            .Join(
                subjects.Query,
                t => t.SubjectId,
                s => s.Id,
                (t, s) => new { t.TeacherProfileId, Subject = s })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static x => x.TeacherProfileId)
            .ToDictionary(
                static g => g.Key,
                static g => g.Select(static x => x.Subject).ToList());
    }
}

/// <summary>One teacher with their identity data and assigned subjects.</summary>
public sealed record GetTeacherQuery(Guid Id) : IRequest<Result<TeacherProfileDto>>;

public sealed class GetTeacherQueryHandler(
    IRepository<TeacherProfile> profiles,
    IRepository<TeacherSubject> teacherSubjects,
    IRepository<Subject> subjects,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetTeacherQuery, Result<TeacherProfileDto>>
{
    public async Task<Result<TeacherProfileDto>> Handle(
        GetTeacherQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await profiles.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return Result<TeacherProfileDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var user = await userManager.FindByIdAsync(profile.UserId.ToString()).ConfigureAwait(false);

        if (user is null)
        {
            // The account was hard-deleted while the profile was active; the cascade removes
            // the profile, so this only trips on hand-edited data.
            return Result<TeacherProfileDto>.Failure(
                "Teachers.UserNotFound",
                "The linked user account no longer exists.");
        }

        var assigned = await teacherSubjects.Query
            .Where(t => t.TeacherProfileId == profile.Id)
            .Join(subjects.Query, t => t.SubjectId, s => s.Id, (_, s) => s)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<TeacherProfileDto>.Success(
            TeacherProfileDto.FromEntity(profile, user, assigned));
    }
}

/// <summary>
/// Resolves the compensation rule in force for a teacher under an optional subject.
/// Answers with <c>rule: null</c> when nothing matches - a successful answer, not an error.
/// </summary>
public sealed record GetTeacherCompensationQuery(Guid TeacherId, Guid? SubjectId)
    : IRequest<Result<ResolvedCompensationDto>>;

public sealed class GetTeacherCompensationQueryHandler(
    IRepository<TeacherProfile> profiles,
    CompensationResolver resolver,
    DataScopeService dataScope)
    : IRequestHandler<GetTeacherCompensationQuery, Result<ResolvedCompensationDto>>
{
    public async Task<Result<ResolvedCompensationDto>> Handle(
        GetTeacherCompensationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Pay is private: a teacher reads their own compensation, never a colleague's.
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var profile = scope.IsUnrestricted || scope.TeacherProfileIds.Contains(request.TeacherId)
            ? await profiles.GetByIdAsync(request.TeacherId, cancellationToken).ConfigureAwait(false)
            : null;

        if (profile is null)
        {
            return Result<ResolvedCompensationDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var resolved = await resolver
            .ResolveAsync(profile.Id, request.SubjectId, studyGroupId: null, cancellationToken)
            .ConfigureAwait(false);

        return Result<ResolvedCompensationDto>.Success(resolved);
    }
}

/// <summary>Pages the tenant's compensation rules, optionally narrowed to one teacher.</summary>
public sealed record GetCompensationRulesQuery(PaginationRequest Pagination, Guid? TeacherProfileId)
    : IRequest<Result<PaginatedList<CompensationRuleDto>>>;

public sealed class GetCompensationRulesQueryHandler(IRepository<CompensationRule> rules, DataScopeService dataScope)
    : IRequestHandler<GetCompensationRulesQuery, Result<PaginatedList<CompensationRuleDto>>>
{
    public async Task<Result<PaginatedList<CompensationRuleDto>>> Handle(
        GetCompensationRulesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var query = rules.Query.AsQueryable();

        // A teacher sees the rules that apply to them: their own and the centre-wide defaults.
        if (!scope.IsUnrestricted)
        {
            var own = scope.TeacherProfileIds.ToArray();
            query = query.Where(r => r.TeacherProfileId == null || own.Contains(r.TeacherProfileId.Value));
        }

        if (request.TeacherProfileId.HasValue)
        {
            query = query.Where(r => r.TeacherProfileId == request.TeacherProfileId);
        }

        var page = await query
            .OrderByDescending(static r => r.TeacherProfileId.HasValue)
            .ThenBy(static r => r.SubjectId)
            .ThenBy(static r => r.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(CompensationRuleDto.FromEntity).ToArray();

        return Result<PaginatedList<CompensationRuleDto>>.Success(
            new PaginatedList<CompensationRuleDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

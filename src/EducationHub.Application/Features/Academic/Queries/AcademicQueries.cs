using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Academic.Queries;

/// <summary>Pages the tenant's academic years.</summary>
public sealed record GetAcademicYearsQuery(PaginationRequest Pagination, string? Search, bool IncludeArchived)
    : IRequest<Result<PaginatedList<AcademicYearDto>>>;

public sealed class GetAcademicYearsQueryHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext)
    : IRequestHandler<GetAcademicYearsQuery, Result<PaginatedList<AcademicYearDto>>>
{
    public async Task<Result<PaginatedList<AcademicYearDto>>> Handle(
        GetAcademicYearsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var query = years.Query.AsQueryable();

        // Archived years are hidden by default rather than filtered out of existence: the
        // dropdowns that pick a year must never offer one that is closed to new enrolment.
        if (!request.IncludeArchived)
        {
            query = query.Where(static y => !y.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(y => y.Name.Contains(term)
                || (y.NameAr != null && y.NameAr.Contains(term)));
        }

        var page = await query
            .OrderByDescending(static y => y.StartDate)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(AcademicYearDto.FromEntity).ToArray();

        return Result<PaginatedList<AcademicYearDto>>.Success(
            new PaginatedList<AcademicYearDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>The tenant's current academic year, or a failure when none is set.</summary>
/// <remarks>
/// A dedicated endpoint rather than a filter on the list, because the frontend needs this on
/// every page and a "none configured" state is meaningful: it must prompt the user to set
/// one, not look like an empty list.
/// </remarks>
public sealed record GetCurrentAcademicYearQuery : IRequest<Result<AcademicYearDto>>;

public sealed class GetCurrentAcademicYearQueryHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext)
    : IRequestHandler<GetCurrentAcademicYearQuery, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        GetCurrentAcademicYearQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var year = await years.Query
            .Where(static y => y.IsCurrent && !y.IsArchived)
            .OrderByDescending(static y => y.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (year is null)
        {
            return Result<AcademicYearDto>.Failure(
                "AcademicYears.NoneCurrent",
                "No current academic year has been configured for this tenant.");
        }

        return Result<AcademicYearDto>.Success(AcademicYearDto.FromEntity(year));
    }
}

public sealed record GetAcademicYearQuery(Guid Id) : IRequest<Result<AcademicYearDto>>;

public sealed class GetAcademicYearQueryHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext)
    : IRequestHandler<GetAcademicYearQuery, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        GetAcademicYearQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var year = await years.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return year is null
            ? Result<AcademicYearDto>.Failure("AcademicYears.NotFound", "Academic year not found.")
            : Result<AcademicYearDto>.Success(AcademicYearDto.FromEntity(year));
    }
}

public sealed record GetGradeLevelsQuery(PaginationRequest Pagination, string? Search, bool ActiveOnly)
    : IRequest<Result<PaginatedList<GradeLevelDto>>>;

public sealed class GetGradeLevelsQueryHandler(
    IRepository<GradeLevel> grades,
    ITenantContext tenantContext)
    : IRequestHandler<GetGradeLevelsQuery, Result<PaginatedList<GradeLevelDto>>>
{
    public async Task<Result<PaginatedList<GradeLevelDto>>> Handle(
        GetGradeLevelsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var query = grades.Query.AsQueryable();

        if (request.ActiveOnly)
        {
            query = query.Where(static g => g.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(g => g.Code.Contains(term)
                || g.Name.Contains(term)
                || (g.NameAr != null && g.NameAr.Contains(term)));
        }

        var page = await query
            .OrderBy(static g => g.Order)
            .ThenBy(static g => g.Code)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(GradeLevelDto.FromEntity).ToArray();

        return Result<PaginatedList<GradeLevelDto>>.Success(
            new PaginatedList<GradeLevelDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetGradeLevelQuery(Guid Id) : IRequest<Result<GradeLevelDto>>;

public sealed class GetGradeLevelQueryHandler(
    IRepository<GradeLevel> grades,
    ITenantContext tenantContext)
    : IRequestHandler<GetGradeLevelQuery, Result<GradeLevelDto>>
{
    public async Task<Result<GradeLevelDto>> Handle(
        GetGradeLevelQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var grade = await grades.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return grade is null
            ? Result<GradeLevelDto>.Failure("Grades.NotFound", "Grade level not found.")
            : Result<GradeLevelDto>.Success(GradeLevelDto.FromEntity(grade));
    }
}

public sealed record GetSubjectsQuery(PaginationRequest Pagination, string? Search, bool ActiveOnly)
    : IRequest<Result<PaginatedList<SubjectDto>>>;

public sealed class GetSubjectsQueryHandler(
    IRepository<Subject> subjects,
    ITenantContext tenantContext)
    : IRequestHandler<GetSubjectsQuery, Result<PaginatedList<SubjectDto>>>
{
    public async Task<Result<PaginatedList<SubjectDto>>> Handle(
        GetSubjectsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var query = subjects.Query.AsQueryable();

        if (request.ActiveOnly)
        {
            query = query.Where(static s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(s => s.Code.Contains(term)
                || s.Name.Contains(term)
                || s.NameAr.Contains(term));
        }

        var page = await query
            .OrderBy(static s => s.SortOrder)
            .ThenBy(static s => s.Code)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = page.Items.Select(SubjectDto.FromEntity).ToArray();

        return Result<PaginatedList<SubjectDto>>.Success(
            new PaginatedList<SubjectDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetSubjectQuery(Guid Id) : IRequest<Result<SubjectDto>>;

public sealed class GetSubjectQueryHandler(
    IRepository<Subject> subjects,
    ITenantContext tenantContext)
    : IRequestHandler<GetSubjectQuery, Result<SubjectDto>>
{
    public async Task<Result<SubjectDto>> Handle(
        GetSubjectQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var subject = await subjects.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return subject is null
            ? Result<SubjectDto>.Failure("Subjects.NotFound", "Subject not found.")
            : Result<SubjectDto>.Success(SubjectDto.FromEntity(subject));
    }
}

/// <summary>
/// The academic year that contains a given date.
/// </summary>
/// <remarks>
/// Resolved in the database with a range comparison rather than by loading candidates and
/// testing <c>Contains</c> in memory: attendance and fee calculations call this per student
/// per day, so it has to stay a single indexed round trip.
/// </remarks>
public sealed record GetAcademicYearForDateQuery(DateOnly Date) : IRequest<Result<AcademicYearDto>>;

public sealed class GetAcademicYearForDateQueryHandler(
    IRepository<AcademicYear> years,
    ITenantContext tenantContext)
    : IRequestHandler<GetAcademicYearForDateQuery, Result<AcademicYearDto>>
{
    public async Task<Result<AcademicYearDto>> Handle(
        GetAcademicYearForDateQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var date = request.Date;

        var year = await years.Query
            .Where(y => !y.IsArchived && y.StartDate <= date && y.EndDate >= date)
            .OrderByDescending(static y => y.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return year is null
            ? Result<AcademicYearDto>.Failure(
                "AcademicYears.NotFoundForDate",
                $"No academic year covers {date:yyyy-MM-dd}.")
            : Result<AcademicYearDto>.Success(AcademicYearDto.FromEntity(year));
    }
}

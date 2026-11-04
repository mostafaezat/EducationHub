using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Services;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Students.Queries;

/// <summary>
/// Pages the tenant's students. Search matches name and student number; optional filters
/// narrow by grade and status.
/// </summary>
public sealed record GetStudentsQuery(
    PaginationRequest Pagination,
    string? Search,
    Guid? GradeLevelId,
    StudentStatus? Status)
    : IRequest<Result<PaginatedList<StudentProfileDto>>>;

public sealed class GetStudentsQueryHandler(
    IRepository<StudentProfile> students,
    StudentReadModel readModel)
    : IRequestHandler<GetStudentsQuery, Result<PaginatedList<StudentProfileDto>>>
{
    public async Task<Result<PaginatedList<StudentProfileDto>>> Handle(
        GetStudentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = students.Query;

        if (request.GradeLevelId.HasValue)
        {
            query = query.Where(s => s.GradeLevelId == request.GradeLevelId);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(s => s.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(s =>
                s.FullName.Contains(term)
                || (s.StudentNumber != null && s.StudentNumber.Contains(term)));
        }

        var page = await query
            .OrderBy(static s => s.FullName)
            .ThenBy(static s => s.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.BuildStudentsAsync(page.Items, cancellationToken).ConfigureAwait(false);

        return Result<PaginatedList<StudentProfileDto>>.Success(
            new PaginatedList<StudentProfileDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetStudentQuery(Guid Id) : IRequest<Result<StudentProfileDto>>;

public sealed class GetStudentQueryHandler(
    IRepository<StudentProfile> students,
    StudentReadModel readModel)
    : IRequestHandler<GetStudentQuery, Result<StudentProfileDto>>
{
    public async Task<Result<StudentProfileDto>> Handle(
        GetStudentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await students.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return profile is null
            ? Result<StudentProfileDto>.Failure("Students.NotFound", "Student not found.")
            : Result<StudentProfileDto>.Success(
                await readModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Pages the tenant's parents. Search matches name, phone and e-mail.</summary>
public sealed record GetParentsQuery(PaginationRequest Pagination, string? Search)
    : IRequest<Result<PaginatedList<ParentProfileDto>>>;

public sealed class GetParentsQueryHandler(
    IRepository<ParentProfile> parents,
    StudentReadModel readModel)
    : IRequestHandler<GetParentsQuery, Result<PaginatedList<ParentProfileDto>>>
{
    public async Task<Result<PaginatedList<ParentProfileDto>>> Handle(
        GetParentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = parents.Query;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            query = query.Where(p =>
                p.FullName.Contains(term)
                || (p.PhoneNumber != null && p.PhoneNumber.Contains(term))
                || (p.Email != null && p.Email.Contains(term)));
        }

        var page = await query
            .OrderBy(static p => p.FullName)
            .ThenBy(static p => p.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.BuildParentsAsync(page.Items, cancellationToken).ConfigureAwait(false);

        return Result<PaginatedList<ParentProfileDto>>.Success(
            new PaginatedList<ParentProfileDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetParentQuery(Guid Id) : IRequest<Result<ParentProfileDto>>;

public sealed class GetParentQueryHandler(
    IRepository<ParentProfile> parents,
    StudentReadModel readModel)
    : IRequestHandler<GetParentQuery, Result<ParentProfileDto>>
{
    public async Task<Result<ParentProfileDto>> Handle(
        GetParentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await parents.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return profile is null
            ? Result<ParentProfileDto>.Failure("Parents.NotFound", "Parent not found.")
            : Result<ParentProfileDto>.Success(
                await readModel.BuildParentAsync(profile, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>
/// The signed-in parent's own children (deferred ownership requirement P1-4).
/// </summary>
/// <remarks>
/// Scoped by the caller's account, never by a client-supplied id: the parent profile is
/// found from <see cref="ICurrentUser.UserId"/>, so one parent can never ask for another
/// parent's children. A caller with no parent profile gets an empty list, not an error.
/// </remarks>
public sealed record GetMyChildrenQuery : IRequest<Result<IReadOnlyList<ChildLinkDto>>>;

public sealed class GetMyChildrenQueryHandler(
    IRepository<ParentProfile> parents,
    StudentReadModel readModel,
    ICurrentUser currentUser)
    : IRequestHandler<GetMyChildrenQuery, Result<IReadOnlyList<ChildLinkDto>>>
{
    public async Task<Result<IReadOnlyList<ChildLinkDto>>> Handle(
        GetMyChildrenQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return Result<IReadOnlyList<ChildLinkDto>>.Success(Array.Empty<ChildLinkDto>());
        }

        var parentId = await parents.Query
            .Where(p => p.UserId == userId)
            .Select(static p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (parentId is not { } id)
        {
            return Result<IReadOnlyList<ChildLinkDto>>.Success(Array.Empty<ChildLinkDto>());
        }

        var children = await readModel.LoadChildrenAsync([id], cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<ChildLinkDto>>.Success(children.GetValueOrDefault(id, []));
    }
}

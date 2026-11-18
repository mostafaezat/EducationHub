using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Scheduling;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Services;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Registration;

/// <summary>Shared reads for the staff side of pre-registration requests.</summary>
public sealed class RegistrationRequestReadModel(IRepository<GradeLevel> grades)
{
    public async Task<IReadOnlyList<RegistrationRequestDto>> BuildAsync(
        IReadOnlyList<RegistrationRequest> rows,
        CancellationToken cancellationToken)
    {
        var gradeIds = rows.Where(static r => r.GradeLevelId.HasValue).Select(static r => r.GradeLevelId!.Value).Distinct().ToArray();

        var names = gradeIds.Length == 0
            ? []
            : await grades.Query
                .Where(g => gradeIds.Contains(g.Id))
                .ToDictionaryAsync(static g => g.Id, static g => g.NameAr ?? g.Name, cancellationToken)
                .ConfigureAwait(false);

        return [.. rows.Select(r => new RegistrationRequestDto(
            r.Id,
            r.GuardianName,
            r.GuardianPhone,
            r.GuardianEmail,
            r.Relationship,
            r.StudentName,
            r.BirthDate,
            r.GradeLevelId,
            r.GradeLevelId is { } g ? names.GetValueOrDefault(g) : null,
            r.School,
            r.PreferredSubjects,
            r.Notes,
            r.Status,
            r.CreatedAt,
            r.ReviewedByUserId,
            r.ReviewedAt,
            r.RejectionReason,
            r.CreatedStudentProfileId))];
    }

    public async Task<RegistrationRequestDto> BuildAsync(RegistrationRequest row, CancellationToken cancellationToken)
        => (await BuildAsync([row], cancellationToken).ConfigureAwait(false))[0];
}

internal static class RegistrationRequestSupport
{
    /// <summary>
    /// Loads a request the caller may act on. Requests are centre-management data with no link to
    /// a teacher or a family, so a restricted scope sees none of them (404, like another tenant's id).
    /// </summary>
    public static async Task<RegistrationRequest?> FindAsync(
        IRepository<RegistrationRequest> requests,
        DataScopeService dataScope,
        Guid id,
        CancellationToken cancellationToken)
    {
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);

        return scope.IsUnrestricted
            ? await requests.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public static Result<T> NotFound<T>()
        => Result<T>.Failure("RegistrationRequests.NotFound", "Registration request not found.");

    public static Result<T> AlreadyReviewed<T>(IStringLocalizer<LocalizationResource> localizer)
    {
        var text = localizer[LocalizationKeys.RegistrationAlreadyReviewed];

        return Result<T>.Failure(
            "RegistrationRequests.ReviewConflict",
            text.ResourceNotFound ? "This request has already been reviewed." : text.Value);
    }
}

/// <summary>Paged list of the centre's requests, newest first.</summary>
public sealed record GetRegistrationRequestsQuery(
    PaginationRequest Pagination,
    RegistrationRequestStatus? Status,
    string? Search) : IRequest<Result<PaginatedList<RegistrationRequestDto>>>;

public sealed class GetRegistrationRequestsQueryHandler(
    IRepository<RegistrationRequest> requests,
    RegistrationRequestReadModel readModel,
    DataScopeService dataScope)
    : IRequestHandler<GetRegistrationRequestsQuery, Result<PaginatedList<RegistrationRequestDto>>>
{
    public async Task<Result<PaginatedList<RegistrationRequestDto>>> Handle(
        GetRegistrationRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var query = requests.Query;

        if (!scope.IsUnrestricted)
        {
            query = query.Where(static _ => false);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(r => r.Status == request.Status.Value);
        }

        if (Normalize.Optional(request.Search) is { } term)
        {
            // A number is matched in every stored shape; a name by substring.
            var variants = PhoneNumberFormat.LookupVariants(term).ToArray();

            query = query.Where(r =>
                r.StudentName.Contains(term)
                || r.GuardianName.Contains(term)
                || r.GuardianPhone.Contains(term)
                || variants.Contains(r.GuardianPhone));
        }

        var page = await query
            .OrderByDescending(static r => r.CreatedAt)
            .ThenBy(static r => r.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.BuildAsync(page.Items, cancellationToken).ConfigureAwait(false);

        return Result<PaginatedList<RegistrationRequestDto>>.Success(
            new PaginatedList<RegistrationRequestDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>
/// Approves a pending request: registers the student through <see cref="StudentRegistrationService"/>
/// (the same path as <c>POST /api/students</c>, guardian included), optionally enrolls them, and
/// marks the request approved - all in one transaction. The parent's WhatsApp sign-in details go
/// out after it committed.
/// </summary>
public sealed record ApproveRegistrationCommand(Guid Id, ApproveRegistrationRequest Request)
    : IRequest<Result<ApproveRegistrationResultDto>>;

public sealed class ApproveRegistrationCommandHandler(
    IRepository<RegistrationRequest> requests,
    IRepository<StudyGroup> groups,
    IRepository<StudentProfile> students,
    IRepository<Enrollment> enrollments,
    StudentRegistrationService registration,
    StudentReadModel studentReadModel,
    RegistrationRequestReadModel readModel,
    DataScopeService dataScope,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeService clock,
    IUnitOfWork unitOfWork,
    IStringLocalizer<LocalizationResource> localizer)
    : IRequestHandler<ApproveRegistrationCommand, Result<ApproveRegistrationResultDto>>
{
    public async Task<Result<ApproveRegistrationResultDto>> Handle(ApproveRegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var row = await RegistrationRequestSupport.FindAsync(requests, dataScope, command.Id, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return RegistrationRequestSupport.NotFound<ApproveRegistrationResultDto>();
        }

        if (row.Status != RegistrationRequestStatus.Pending)
        {
            return RegistrationRequestSupport.AlreadyReviewed<ApproveRegistrationResultDto>(localizer);
        }

        var overrides = command.Request;

        var studentRequest = new CreateStudentRequest(
            FullName: Normalize.Optional(overrides.StudentName) ?? row.StudentName,
            StudentNumber: null,
            DateOfBirth: overrides.BirthDate ?? row.BirthDate,
            GradeLevelId: overrides.GradeLevelId ?? row.GradeLevelId,
            BranchId: null,
            EnrollmentDate: DateOnly.FromDateTime(clock.UtcNow),
            Notes: Normalize.Optional(overrides.Notes) ?? StudentNotes(row),
            UserId: null,
            AccountEmail: null,
            AccountPassword: null,
            Guardian: new CreateStudentGuardianRequest(
                Normalize.Optional(overrides.GuardianName) ?? row.GuardianName,
                Normalize.Optional(overrides.GuardianPhone) ?? row.GuardianPhone,
                Normalize.Optional(overrides.GuardianEmail) ?? row.GuardianEmail,
                overrides.Relationship ?? row.Relationship,
                overrides.CreateAccount));

        var enrolled = false;

        var created = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var student = await registration.CreateAsync(studentRequest, ct).ConfigureAwait(false);

                if (!student.Succeeded || student.Data is not { } dto)
                {
                    return student;
                }

                if (overrides.StudyGroupId is { } groupId)
                {
                    var enrollment = await EnrollmentSupport
                        .EnrollAsync(groups, students, enrollments, clock, tenantId, groupId, dto.Id, null, ct)
                        .ConfigureAwait(false);

                    if (!enrollment.Succeeded)
                    {
                        return Result<StudentProfileDto>.FromFailure(enrollment);
                    }

                    enrolled = true;
                }

                // RowVersion makes a concurrent second approval fail here (409) and roll back
                // the student it created.
                row.Status = RegistrationRequestStatus.Approved;
                row.ReviewedByUserId = currentUser.UserId;
                row.ReviewedAt = clock.UtcNow;
                row.CreatedStudentProfileId = dto.Id;

                await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                return student;
            },
            cancellationToken).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return Result<ApproveRegistrationResultDto>.FromFailure(created);
        }

        // Committed: now the WhatsApp sign-in details, best-effort.
        var announced = await registration.AnnounceGuardianCredentialsAsync(created, cancellationToken).ConfigureAwait(false);
        var studentDto = announced.Data!;

        // Re-read so the enrollment shows in the student's groups.
        if (enrolled && await students.GetByIdAsync(studentDto.Id, cancellationToken).ConfigureAwait(false) is { } profile)
        {
            studentDto = (await studentReadModel.BuildStudentAsync(profile, cancellationToken).ConfigureAwait(false))
                with { NewGuardian = studentDto.NewGuardian };
        }

        return Result<ApproveRegistrationResultDto>.Success(
            new ApproveRegistrationResultDto(
                await readModel.BuildAsync(row, cancellationToken).ConfigureAwait(false),
                studentDto,
                enrolled),
            "Registration request approved.");
    }

    /// <summary>What the guardian told us that has no column on the student profile.</summary>
    private static string? StudentNotes(RegistrationRequest row)
    {
        var parts = new List<string>();

        if (row.School is not null)
        {
            parts.Add($"School: {row.School}");
        }

        if (row.PreferredSubjects is not null)
        {
            parts.Add($"Subjects: {row.PreferredSubjects}");
        }

        if (row.Notes is not null)
        {
            parts.Add(row.Notes);
        }

        var text = string.Join(" | ", parts);
        return text.Length == 0 ? null : text.Length > 1000 ? text[..1000] : text;
    }
}

/// <summary>Rejects a pending request with a reason.</summary>
public sealed record RejectRegistrationCommand(Guid Id, RejectRegistrationRequest Request)
    : IRequest<Result<RegistrationRequestDto>>;

public sealed class RejectRegistrationCommandHandler(
    IRepository<RegistrationRequest> requests,
    RegistrationRequestReadModel readModel,
    DataScopeService dataScope,
    ICurrentUser currentUser,
    IDateTimeService clock,
    IUnitOfWork unitOfWork,
    IStringLocalizer<LocalizationResource> localizer)
    : IRequestHandler<RejectRegistrationCommand, Result<RegistrationRequestDto>>
{
    public async Task<Result<RegistrationRequestDto>> Handle(RejectRegistrationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var row = await RegistrationRequestSupport.FindAsync(requests, dataScope, command.Id, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return RegistrationRequestSupport.NotFound<RegistrationRequestDto>();
        }

        if (row.Status != RegistrationRequestStatus.Pending)
        {
            return RegistrationRequestSupport.AlreadyReviewed<RegistrationRequestDto>(localizer);
        }

        row.Status = RegistrationRequestStatus.Rejected;
        row.RejectionReason = Normalize.Optional(command.Request.Reason);
        row.ReviewedByUserId = currentUser.UserId;
        row.ReviewedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<RegistrationRequestDto>.Success(
            await readModel.BuildAsync(row, cancellationToken).ConfigureAwait(false),
            "Registration request rejected.");
    }
}

/// <summary>The current centre's public slug, so staff can share the registration page link.</summary>
public sealed record GetRegistrationLinkQuery : IRequest<Result<RegistrationLinkDto>>;

public sealed class GetRegistrationLinkQueryHandler(
    IRepository<EducationHub.Domain.Entities.Tenancy.Tenant> tenants,
    ITenantContext tenantContext)
    : IRequestHandler<GetRegistrationLinkQuery, Result<RegistrationLinkDto>>
{
    public async Task<Result<RegistrationLinkDto>> Handle(GetRegistrationLinkQuery request, CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        return tenant is null
            ? Result<RegistrationLinkDto>.Failure("Tenants.NotFound", "Tenant not found.")
            : Result<RegistrationLinkDto>.Success(new RegistrationLinkDto(tenant.Slug));
    }
}

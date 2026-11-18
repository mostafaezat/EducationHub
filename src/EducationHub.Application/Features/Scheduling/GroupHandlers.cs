using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Scheduling;

// ---- Queries -----------------------------------------------------------------

public sealed record GetStudyGroupsQuery(
    PaginationRequest Pagination,
    string? Search,
    Guid? SubjectId,
    Guid? TeacherProfileId,
    bool ActiveOnly) : IRequest<Result<PaginatedList<StudyGroupDto>>>;

public sealed class GetStudyGroupsQueryHandler(IRepository<StudyGroup> groups, SchedulingReadModel readModel, DataScopeService dataScope)
    : IRequestHandler<GetStudyGroupsQuery, Result<PaginatedList<StudyGroupDto>>>
{
    public async Task<Result<PaginatedList<StudyGroupDto>>> Handle(GetStudyGroupsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var query = groups.Query;

        if (!scope.IsUnrestricted)
        {
            var visible = scope.GroupIds.ToArray();
            query = query.Where(g => visible.Contains(g.Id));
        }

        if (request.SubjectId.HasValue)
        {
            query = query.Where(g => g.SubjectId == request.SubjectId.Value);
        }

        if (request.TeacherProfileId.HasValue)
        {
            query = query.Where(g => g.TeacherProfileId == request.TeacherProfileId);
        }

        if (request.ActiveOnly)
        {
            query = query.Where(static g => g.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(g => g.Name.Contains(term) || (g.Code != null && g.Code.Contains(term)));
        }

        var page = await query
            .OrderBy(static g => g.Name)
            .ThenBy(static g => g.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.BuildGroupsAsync(page.Items, cancellationToken).ConfigureAwait(false);

        return Result<PaginatedList<StudyGroupDto>>.Success(
            new PaginatedList<StudyGroupDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetStudyGroupQuery(Guid Id) : IRequest<Result<StudyGroupDto>>;

public sealed class GetStudyGroupQueryHandler(IRepository<StudyGroup> groups, SchedulingReadModel readModel, DataScopeService dataScope)
    : IRequestHandler<GetStudyGroupQuery, Result<StudyGroupDto>>
{
    public async Task<Result<StudyGroupDto>> Handle(GetStudyGroupQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var group = scope.CanSeeGroup(request.Id)
            ? await groups.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            : null;

        return group is null
            ? Result<StudyGroupDto>.Failure("Groups.NotFound", "Study group not found.")
            : Result<StudyGroupDto>.Success(await readModel.BuildGroupAsync(group, cancellationToken).ConfigureAwait(false));
    }
}

public sealed record GetEnrollmentsQuery(Guid StudyGroupId, bool ActiveOnly) : IRequest<Result<IReadOnlyList<EnrollmentDto>>>;

public sealed class GetEnrollmentsQueryHandler(
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments,
    SchedulingReadModel readModel,
    DataScopeService dataScope)
    : IRequestHandler<GetEnrollmentsQuery, Result<IReadOnlyList<EnrollmentDto>>>
{
    public async Task<Result<IReadOnlyList<EnrollmentDto>>> Handle(GetEnrollmentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!scope.CanSeeGroup(request.StudyGroupId)
            || !await groups.ExistsAsync(request.StudyGroupId, cancellationToken).ConfigureAwait(false))
        {
            return Result<IReadOnlyList<EnrollmentDto>>.Failure("Groups.NotFound", "Study group not found.");
        }

        var query = enrollments.Query.Where(e => e.StudyGroupId == request.StudyGroupId);

        // A family sees its own children on the roster, never the other students.
        if (scope.IsFamily)
        {
            var own = scope.StudentIds.ToArray();
            query = query.Where(e => own.Contains(e.StudentProfileId));
        }

        if (request.ActiveOnly)
        {
            query = query.Where(static e => e.Status == EnrollmentStatus.Active);
        }

        var rows = await query.OrderBy(static e => e.EnrolledOn).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<EnrollmentDto>>.Success(
            await readModel.BuildEnrollmentsAsync(rows, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>A student's enrollments across groups (used by the student screen and fees).</summary>
public sealed record GetStudentEnrollmentsQuery(Guid StudentProfileId) : IRequest<Result<IReadOnlyList<EnrollmentDto>>>;

public sealed class GetStudentEnrollmentsQueryHandler(
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    SchedulingReadModel readModel,
    DataScopeService dataScope)
    : IRequestHandler<GetStudentEnrollmentsQuery, Result<IReadOnlyList<EnrollmentDto>>>
{
    public async Task<Result<IReadOnlyList<EnrollmentDto>>> Handle(GetStudentEnrollmentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);

        // Another tenant's (or a deleted, or an out-of-scope) student answers 404, like every
        // other student route, instead of an empty list that would still differ in shape.
        if (!scope.CanSeeStudent(request.StudentProfileId)
            || !await students.ExistsAsync(request.StudentProfileId, cancellationToken).ConfigureAwait(false))
        {
            return Result<IReadOnlyList<EnrollmentDto>>.Failure("Students.NotFound", "Student not found.");
        }

        var query = enrollments.Query.Where(e => e.StudentProfileId == request.StudentProfileId);

        // A teacher sees the student's place in their own groups only.
        if (scope.IsTeacher)
        {
            var visible = scope.GroupIds.ToArray();
            query = query.Where(e => visible.Contains(e.StudyGroupId));
        }

        var rows = await query
            .OrderByDescending(static e => e.EnrolledOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<EnrollmentDto>>.Success(
            await readModel.BuildEnrollmentsAsync(rows, cancellationToken).ConfigureAwait(false));
    }
}

// ---- Group commands ----------------------------------------------------------

internal static class GroupSupport
{
    /// <summary>Checks every referenced row exists in this tenant; null when all do.</summary>
    public static async Task<Result<StudyGroupDto>?> CheckReferencesAsync(
        IRepository<Subject> subjects,
        IRepository<GradeLevel> grades,
        IRepository<TeacherProfile> teachers,
        IRepository<AcademicYear> years,
        IRepository<Branch> branches,
        Guid? subjectId,
        Guid? gradeLevelId,
        Guid? teacherProfileId,
        Guid? academicYearId,
        Guid? branchId,
        CancellationToken ct)
    {
        if (subjectId is { } s && !await subjects.ExistsAsync(s, ct).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("Subjects.NotFound", "Subject not found.");
        }

        if (gradeLevelId is { } g && !await grades.ExistsAsync(g, ct).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("Grades.NotFound", "Grade level not found.");
        }

        if (teacherProfileId is { } t && !await teachers.ExistsAsync(t, ct).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        if (academicYearId is { } y && !await years.ExistsAsync(y, ct).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("AcademicYears.NotFound", "Academic year not found.");
        }

        if (branchId is { } b && !await branches.ExistsAsync(b, ct).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("Branches.NotFound", "Branch not found.");
        }

        return null;
    }

    public static Task<bool> NameTakenAsync(IRepository<StudyGroup> groups, string name, Guid? exceptId, CancellationToken ct)
        => groups.Query.AnyAsync(g => g.Name == name && (!exceptId.HasValue || g.Id != exceptId.Value), ct);
}

public sealed record CreateStudyGroupCommand(CreateStudyGroupRequest Request) : IRequest<Result<StudyGroupDto>>;

public sealed class CreateStudyGroupCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<Subject> subjects,
    IRepository<GradeLevel> grades,
    IRepository<TeacherProfile> teachers,
    IRepository<AcademicYear> years,
    IRepository<Branch> branches,
    SchedulingReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateStudyGroupCommand, Result<StudyGroupDto>>
{
    public async Task<Result<StudyGroupDto>> Handle(CreateStudyGroupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var name = Normalize.Required(r.Name, string.Empty);

        if (await GroupSupport.NameTakenAsync(groups, name, null, cancellationToken).ConfigureAwait(false))
        {
            return Result<StudyGroupDto>.Failure("Groups.NameTaken", $"A study group named '{name}' already exists.");
        }

        var failure = await GroupSupport.CheckReferencesAsync(
            subjects, grades, teachers, years, branches,
            r.SubjectId, r.GradeLevelId, r.TeacherProfileId, r.AcademicYearId, r.BranchId,
            cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            return failure;
        }

        var group = new StudyGroup
        {
            TenantId = tenantId,
            Name = name,
            Code = Normalize.Optional(r.Code)?.ToUpperInvariant(),
            SubjectId = r.SubjectId,
            GradeLevelId = r.GradeLevelId,
            TeacherProfileId = r.TeacherProfileId,
            AcademicYearId = r.AcademicYearId,
            BranchId = r.BranchId,
            Capacity = r.Capacity,
            IsActive = true,
            Notes = Normalize.Optional(r.Notes)
        };

        await groups.AddAsync(group, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StudyGroupDto>.Success(
            await readModel.BuildGroupAsync(group, cancellationToken).ConfigureAwait(false),
            "Study group created successfully.");
    }
}

public sealed record UpdateStudyGroupCommand(Guid Id, UpdateStudyGroupRequest Request) : IRequest<Result<StudyGroupDto>>;

public sealed class UpdateStudyGroupCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<Subject> subjects,
    IRepository<GradeLevel> grades,
    IRepository<TeacherProfile> teachers,
    IRepository<AcademicYear> years,
    IRepository<Branch> branches,
    SchedulingReadModel readModel,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateStudyGroupCommand, Result<StudyGroupDto>>
{
    public async Task<Result<StudyGroupDto>> Handle(UpdateStudyGroupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (group is null)
        {
            return Result<StudyGroupDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var r = command.Request;

        if (r.Name is not null)
        {
            var name = Normalize.Required(r.Name, group.Name);

            if (await GroupSupport.NameTakenAsync(groups, name, group.Id, cancellationToken).ConfigureAwait(false))
            {
                return Result<StudyGroupDto>.Failure("Groups.NameTaken", $"A study group named '{name}' already exists.");
            }

            group.Name = name;
        }

        var failure = await GroupSupport.CheckReferencesAsync(
            subjects, grades, teachers, years, branches,
            r.SubjectId, r.GradeLevelId, r.TeacherProfileId, r.AcademicYearId, r.BranchId,
            cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            return failure;
        }

        if (r.Code is not null)
        {
            group.Code = Normalize.Optional(r.Code)?.ToUpperInvariant();
        }

        if (r.SubjectId.HasValue)
        {
            group.SubjectId = r.SubjectId.Value;
        }

        if (r.GradeLevelId.HasValue)
        {
            group.GradeLevelId = r.GradeLevelId;
        }

        if (r.TeacherProfileId.HasValue)
        {
            group.TeacherProfileId = r.TeacherProfileId;
        }

        if (r.AcademicYearId.HasValue)
        {
            group.AcademicYearId = r.AcademicYearId;
        }

        if (r.BranchId.HasValue)
        {
            group.BranchId = r.BranchId;
        }

        if (r.Capacity.HasValue)
        {
            group.Capacity = r.Capacity.Value <= 0 ? null : r.Capacity;
        }

        if (r.IsActive.HasValue)
        {
            group.IsActive = r.IsActive.Value;
        }

        if (r.Notes is not null)
        {
            group.Notes = Normalize.Optional(r.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<StudyGroupDto>.Success(
            await readModel.BuildGroupAsync(group, cancellationToken).ConfigureAwait(false),
            "Study group updated successfully.");
    }
}

/// <summary>Soft-deletes a group. Refused while it still has active enrollments.</summary>
public sealed record DeleteStudyGroupCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteStudyGroupCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments,
    IUnitOfWork unitOfWork) : IRequestHandler<DeleteStudyGroupCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteStudyGroupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var group = await groups.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (group is null)
        {
            return Result<bool>.Failure("Groups.NotFound", "Study group not found.");
        }

        var hasActive = await enrollments.Query
            .AnyAsync(e => e.StudyGroupId == group.Id && e.Status == EnrollmentStatus.Active, cancellationToken)
            .ConfigureAwait(false);

        if (hasActive)
        {
            return Result<bool>.Failure("Groups.InUse", "End the group's active enrollments before deleting it.");
        }

        groups.SoftRemove(group);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Study group deleted successfully.");
    }
}

// ---- Enrollment commands -----------------------------------------------------

/// <summary>
/// Enrolls a student in a group. Re-enrolling a student whose earlier enrollment ended
/// reactivates that row (the unique index allows one live row per pair).
/// </summary>
public sealed record EnrollStudentCommand(Guid StudyGroupId, EnrollStudentRequest Request) : IRequest<Result<EnrollmentDto>>;

public sealed class EnrollStudentCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<StudentProfile> students,
    IRepository<Enrollment> enrollments,
    SchedulingReadModel readModel,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<EnrollStudentCommand, Result<EnrollmentDto>>
{
    public async Task<Result<EnrollmentDto>> Handle(EnrollStudentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        var enrolled = await EnrollmentSupport.EnrollAsync(
                groups, students, enrollments, clock, tenantId, command.StudyGroupId, command.Request.StudentProfileId, command.Request.EnrolledOn, cancellationToken)
            .ConfigureAwait(false);

        if (!enrolled.Succeeded || enrolled.Data is not { } existing)
        {
            return Result<EnrollmentDto>.FromFailure(enrolled);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<EnrollmentDto>.Success(
            (await readModel.BuildEnrollmentsAsync([existing], cancellationToken).ConfigureAwait(false))[0],
            "Student enrolled successfully.");
    }
}

/// <summary>Ends an enrollment (withdrawn) - the row stays for attendance and fee history.</summary>
public sealed record EndEnrollmentCommand(Guid StudyGroupId, Guid EnrollmentId) : IRequest<Result<bool>>;

public sealed class EndEnrollmentCommandHandler(IRepository<Enrollment> enrollments, IDateTimeService clock, IUnitOfWork unitOfWork)
    : IRequestHandler<EndEnrollmentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(EndEnrollmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var enrollment = await enrollments.GetByIdAsync(command.EnrollmentId, cancellationToken).ConfigureAwait(false);

        if (enrollment is null || enrollment.StudyGroupId != command.StudyGroupId)
        {
            return Result<bool>.Failure("Enrollments.NotFound", "Enrollment not found.");
        }

        enrollment.Status = EnrollmentStatus.Withdrawn;
        enrollment.EndedOn = DateOnly.FromDateTime(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Enrollment ended successfully.");
    }
}

// ---- Schedule slots ----------------------------------------------------------

public sealed record CreateScheduleSlotCommand(Guid StudyGroupId, CreateScheduleSlotRequest Request) : IRequest<Result<ScheduleSlotDto>>;

public sealed class CreateScheduleSlotCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<ScheduleSlot> slots,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateScheduleSlotCommand, Result<ScheduleSlotDto>>
{
    public async Task<Result<ScheduleSlotDto>> Handle(CreateScheduleSlotCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        if (!await groups.ExistsAsync(command.StudyGroupId, cancellationToken).ConfigureAwait(false))
        {
            return Result<ScheduleSlotDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var r = command.Request;

        // Overlap with another slot of the same group on the same day is a scheduling error.
        var overlaps = await slots.Query
            .AnyAsync(
                s => s.StudyGroupId == command.StudyGroupId
                    && s.DayOfWeek == r.DayOfWeek
                    && s.StartTime < r.EndTime
                    && r.StartTime < s.EndTime,
                cancellationToken)
            .ConfigureAwait(false);

        if (overlaps)
        {
            return Result<ScheduleSlotDto>.Failure("Slots.Conflict", "The slot overlaps another slot of this group.");
        }

        var slot = new ScheduleSlot
        {
            TenantId = tenantId,
            StudyGroupId = command.StudyGroupId,
            DayOfWeek = r.DayOfWeek,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            Room = Normalize.Optional(r.Room),
            EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo
        };

        await slots.AddAsync(slot, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ScheduleSlotDto>.Success(SchedulingReadModel.ToDto(slot), "Schedule slot created successfully.");
    }
}

public sealed record DeleteScheduleSlotCommand(Guid StudyGroupId, Guid SlotId) : IRequest<Result<bool>>;

public sealed class DeleteScheduleSlotCommandHandler(IRepository<ScheduleSlot> slots, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteScheduleSlotCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteScheduleSlotCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slot = await slots.GetByIdAsync(command.SlotId, cancellationToken).ConfigureAwait(false);

        if (slot is null || slot.StudyGroupId != command.StudyGroupId)
        {
            return Result<bool>.Failure("Slots.NotFound", "Schedule slot not found.");
        }

        // Soft delete: sessions already materialized from it keep their reference.
        slots.SoftRemove(slot);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Schedule slot deleted successfully.");
    }
}

// ---- Validators --------------------------------------------------------------

public sealed class CreateStudyGroupCommandValidator : LocalizedValidator<CreateStudyGroupCommand>
{
    public CreateStudyGroupCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).NotEmpty().MinimumLength(2).MaximumLength(200).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Code).MaximumLength(50).When(static x => x.Request is not null);
        RuleFor(x => x.Request.SubjectId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Capacity).GreaterThan(0).When(static x => x.Request is not null && x.Request.Capacity.HasValue);
        RuleFor(x => x.Request.Notes).MaximumLength(1000).When(static x => x.Request is not null);
    }
}

public sealed class UpdateStudyGroupCommandValidator : LocalizedValidator<UpdateStudyGroupCommand>
{
    public UpdateStudyGroupCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).MinimumLength(2).MaximumLength(200).When(static x => x.Request?.Name is not null);
        RuleFor(x => x.Request.Code).MaximumLength(50).When(static x => x.Request?.Code is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(1000).When(static x => x.Request?.Notes is not null);
    }
}

public sealed class EnrollStudentCommandValidator : LocalizedValidator<EnrollStudentCommand>
{
    public EnrollStudentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudentProfileId).NotEmpty().When(static x => x.Request is not null);
    }
}

public sealed class CreateScheduleSlotCommandValidator : LocalizedValidator<CreateScheduleSlotCommand>
{
    public CreateScheduleSlotCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.DayOfWeek).IsInEnum().When(static x => x.Request is not null);
        RuleFor(x => x.Request.EndTime)
            .GreaterThan(static x => x.Request.StartTime)
            .WithMessage("End time must be after start time.")
            .When(static x => x.Request is not null);
        RuleFor(x => x.Request)
            .Must(static r => r.EffectiveFrom is null || r.EffectiveTo is null || r.EffectiveTo >= r.EffectiveFrom)
            .WithMessage("The effective range must end on or after its start.")
            .When(static x => x.Request is not null);
        RuleFor(x => x.Request.Room).MaximumLength(100).When(static x => x.Request is not null);
    }
}

/// <summary>
/// The enrollment rules (group active, student exists, not already enrolled, capacity), shared by
/// <see cref="EnrollStudentCommandHandler"/> and the approval of a pre-registration request.
/// Adds or reactivates the row without saving.
/// </summary>
internal static class EnrollmentSupport
{
    public static async Task<Result<Enrollment>> EnrollAsync(
        IRepository<StudyGroup> groups,
        IRepository<StudentProfile> students,
        IRepository<Enrollment> enrollments,
        IDateTimeService clock,
        Guid tenantId,
        Guid groupId,
        Guid studentId,
        DateOnly? requestedOn,
        CancellationToken cancellationToken)
    {
        var group = await groups.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);

        if (group is null)
        {
            return Result<Enrollment>.Failure("Groups.NotFound", "Study group not found.");
        }

        if (!group.IsActive)
        {
            return Result<Enrollment>.Failure("Groups.Inactive", "The study group is inactive.");
        }

        if (!await students.ExistsAsync(studentId, cancellationToken).ConfigureAwait(false))
        {
            return Result<Enrollment>.Failure("Students.NotFound", "Student not found.");
        }

        var existing = await enrollments.Query
            .FirstOrDefaultAsync(e => e.StudyGroupId == group.Id && e.StudentProfileId == studentId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is { Status: EnrollmentStatus.Active })
        {
            return Result<Enrollment>.Failure("Enrollments.AlreadyExists", "The student is already enrolled in this group.");
        }

        if (group.Capacity is { } capacity)
        {
            var active = await enrollments.Query
                .CountAsync(e => e.StudyGroupId == group.Id && e.Status == EnrollmentStatus.Active, cancellationToken)
                .ConfigureAwait(false);

            if (active >= capacity)
            {
                return Result<Enrollment>.Failure("Groups.Full", "The study group is at capacity.");
            }
        }

        var enrolledOn = requestedOn ?? DateOnly.FromDateTime(clock.UtcNow);

        if (existing is not null)
        {
            existing.Status = EnrollmentStatus.Active;
            existing.EnrolledOn = enrolledOn;
            existing.EndedOn = null;
        }
        else
        {
            existing = new Enrollment
            {
                TenantId = tenantId,
                StudyGroupId = group.Id,
                StudentProfileId = studentId,
                EnrolledOn = enrolledOn,
                Status = EnrollmentStatus.Active
            };

            await enrollments.AddAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        return Result<Enrollment>.Success(existing);
    }
}

using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Scheduling;

/// <summary>
/// Turns weekly schedule slots into dated <see cref="ClassSession"/> rows.
/// </summary>
/// <remarks>
/// Idempotent: a (slot, date) pair that already has a session - including a deleted one,
/// so a session removed on purpose is not resurrected - is skipped. Only active groups and
/// dates inside each slot's effective window are materialized.
/// </remarks>
public sealed class SessionMaterializer(
    IRepository<StudyGroup> groups,
    IRepository<ScheduleSlot> slots,
    IRepository<ClassSession> sessions)
{
    public const int MaxRangeDays = 120;

    public async Task<GenerateSessionsResultDto> MaterializeAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? studyGroupId,
        CancellationToken cancellationToken)
    {
        var groupQuery = groups.Query.Where(static g => g.IsActive);

        if (studyGroupId.HasValue)
        {
            groupQuery = groupQuery.Where(g => g.Id == studyGroupId.Value);
        }

        var groupRows = await groupQuery
            .Select(static g => new { g.Id, g.TeacherProfileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groupIds = groupRows.Select(static g => g.Id).ToArray();
        var teacherByGroup = groupRows.ToDictionary(static g => g.Id, static g => g.TeacherProfileId);

        var slotRows = await slots.Query
            .Where(s => groupIds.Contains(s.StudyGroupId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var slotIds = slotRows.Select(static s => (Guid?)s.Id).ToArray();

        var existing = (await sessions.Query
                .IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId
                    && s.ScheduleSlotId != null
                    && slotIds.Contains(s.ScheduleSlotId)
                    && s.Date >= from
                    && s.Date <= to)
                .Select(static s => new { s.ScheduleSlotId, s.Date })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(static s => (s.ScheduleSlotId!.Value, s.Date))
            .ToHashSet();

        var created = 0;
        var skipped = 0;

        foreach (var slot in slotRows)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                if (date.DayOfWeek != slot.DayOfWeek
                    || (slot.EffectiveFrom.HasValue && date < slot.EffectiveFrom.Value)
                    || (slot.EffectiveTo.HasValue && date > slot.EffectiveTo.Value))
                {
                    continue;
                }

                if (existing.Contains((slot.Id, date)))
                {
                    skipped++;
                    continue;
                }

                await sessions.AddAsync(
                        new ClassSession
                        {
                            TenantId = tenantId,
                            StudyGroupId = slot.StudyGroupId,
                            ScheduleSlotId = slot.Id,
                            Date = date,
                            StartTime = slot.StartTime,
                            EndTime = slot.EndTime,
                            TeacherProfileId = teacherByGroup.GetValueOrDefault(slot.StudyGroupId),
                            Status = SessionStatus.Scheduled
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                created++;
            }
        }

        return new GenerateSessionsResultDto(created, skipped);
    }
}

// ---- Queries -----------------------------------------------------------------

public sealed record GetSessionsQuery(
    PaginationRequest Pagination,
    DateOnly? From,
    DateOnly? To,
    Guid? StudyGroupId,
    Guid? TeacherProfileId,
    SessionStatus? Status) : IRequest<Result<PaginatedList<ClassSessionDto>>>;

public sealed class GetSessionsQueryHandler(IRepository<ClassSession> sessions, SchedulingReadModel readModel)
    : IRequestHandler<GetSessionsQuery, Result<PaginatedList<ClassSessionDto>>>
{
    public async Task<Result<PaginatedList<ClassSessionDto>>> Handle(GetSessionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = sessions.Query;

        if (request.From.HasValue)
        {
            query = query.Where(s => s.Date >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(s => s.Date <= request.To.Value);
        }

        if (request.StudyGroupId.HasValue)
        {
            query = query.Where(s => s.StudyGroupId == request.StudyGroupId.Value);
        }

        if (request.TeacherProfileId.HasValue)
        {
            query = query.Where(s => s.TeacherProfileId == request.TeacherProfileId);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(s => s.Status == request.Status.Value);
        }

        var page = await query
            .OrderBy(static s => s.Date)
            .ThenBy(static s => s.StartTime)
            .ThenBy(static s => s.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.BuildSessionsAsync(page.Items, cancellationToken).ConfigureAwait(false);

        return Result<PaginatedList<ClassSessionDto>>.Success(
            new PaginatedList<ClassSessionDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetSessionQuery(Guid Id) : IRequest<Result<ClassSessionDto>>;

public sealed class GetSessionQueryHandler(IRepository<ClassSession> sessions, SchedulingReadModel readModel)
    : IRequestHandler<GetSessionQuery, Result<ClassSessionDto>>
{
    public async Task<Result<ClassSessionDto>> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await sessions.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return session is null
            ? Result<ClassSessionDto>.Failure("Sessions.NotFound", "Session not found.")
            : Result<ClassSessionDto>.Success((await readModel.BuildSessionsAsync([session], cancellationToken).ConfigureAwait(false))[0]);
    }
}

/// <summary>
/// The attendance roster of a session: every student enrolled in the group on the session
/// date (active, or ended after it), with their mark if one exists.
/// </summary>
public sealed record GetSessionAttendanceQuery(Guid SessionId) : IRequest<Result<IReadOnlyList<AttendanceRowDto>>>;

public sealed class GetSessionAttendanceQueryHandler(
    IRepository<ClassSession> sessions,
    IRepository<Enrollment> enrollments,
    IRepository<SessionAttendance> attendance,
    IRepository<StudentProfile> students)
    : IRequestHandler<GetSessionAttendanceQuery, Result<IReadOnlyList<AttendanceRowDto>>>
{
    public async Task<Result<IReadOnlyList<AttendanceRowDto>>> Handle(GetSessionAttendanceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await sessions.GetByIdAsync(request.SessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<IReadOnlyList<AttendanceRowDto>>.Failure("Sessions.NotFound", "Session not found.");
        }

        var roster = await AttendanceSupport.RosterAsync(enrollments, students, session, cancellationToken).ConfigureAwait(false);

        var marks = await attendance.Query
            .Where(a => a.ClassSessionId == session.Id)
            .ToDictionaryAsync(static a => a.StudentProfileId, cancellationToken)
            .ConfigureAwait(false);

        // A mark for a student no longer on the roster is still shown, so history is never hidden.
        var extraIds = marks.Keys.Where(id => roster.All(r => r.Id != id)).ToArray();
        var extras = extraIds.Length == 0
            ? new List<RosterStudent>()
            : await students.Query
                .Where(s => extraIds.Contains(s.Id))
                .Select(static s => new RosterStudent(s.Id, s.FullName, s.StudentNumber))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var rows = roster.Concat(extras)
            .OrderBy(static s => s.FullName)
            .Select(s =>
            {
                var mark = marks.GetValueOrDefault(s.Id);
                return new AttendanceRowDto(s.Id, s.FullName, s.StudentNumber, mark?.Status, mark?.Notes);
            })
            .ToList();

        return Result<IReadOnlyList<AttendanceRowDto>>.Success(rows);
    }
}

internal sealed record RosterStudent(Guid Id, string FullName, string? StudentNumber);

internal static class AttendanceSupport
{
    public static async Task<List<RosterStudent>> RosterAsync(
        IRepository<Enrollment> enrollments,
        IRepository<StudentProfile> students,
        ClassSession session,
        CancellationToken cancellationToken)
    {
        var date = session.Date;

        return await enrollments.Query
            .Where(e => e.StudyGroupId == session.StudyGroupId
                && e.EnrolledOn <= date
                && (e.Status == EnrollmentStatus.Active || (e.EndedOn != null && e.EndedOn >= date)))
            .Join(students.Query, e => e.StudentProfileId, s => s.Id, (_, s) => new RosterStudent(s.Id, s.FullName, s.StudentNumber))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

// ---- Commands ----------------------------------------------------------------

public sealed record GenerateSessionsCommand(GenerateSessionsRequest Request) : IRequest<Result<GenerateSessionsResultDto>>;

public sealed class GenerateSessionsCommandHandler(
    SessionMaterializer materializer,
    IRepository<StudyGroup> groups,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<GenerateSessionsCommand, Result<GenerateSessionsResultDto>>
{
    public async Task<Result<GenerateSessionsResultDto>> Handle(GenerateSessionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;

        if (r.StudyGroupId is { } groupId && !await groups.ExistsAsync(groupId, cancellationToken).ConfigureAwait(false))
        {
            return Result<GenerateSessionsResultDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var result = await materializer.MaterializeAsync(tenantId, r.From, r.To, r.StudyGroupId, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<GenerateSessionsResultDto>.Success(result, $"{result.Created} session(s) created.");
    }
}

/// <summary>Creates an ad-hoc session (make-up class, extra lesson) not tied to a slot.</summary>
public sealed record CreateSessionCommand(CreateSessionRequest Request) : IRequest<Result<ClassSessionDto>>;

public sealed class CreateSessionCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<TeacherProfile> teachers,
    IRepository<ClassSession> sessions,
    SchedulingReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateSessionCommand, Result<ClassSessionDto>>
{
    public async Task<Result<ClassSessionDto>> Handle(CreateSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var group = await groups.GetByIdAsync(r.StudyGroupId, cancellationToken).ConfigureAwait(false);

        if (group is null)
        {
            return Result<ClassSessionDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        if (r.TeacherProfileId is { } t && !await teachers.ExistsAsync(t, cancellationToken).ConfigureAwait(false))
        {
            return Result<ClassSessionDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var session = new ClassSession
        {
            TenantId = tenantId,
            StudyGroupId = group.Id,
            Date = r.Date,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            TeacherProfileId = r.TeacherProfileId ?? group.TeacherProfileId,
            Status = SessionStatus.Scheduled,
            Topic = Normalize.Optional(r.Topic)
        };

        await sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ClassSessionDto>.Success(
            (await readModel.BuildSessionsAsync([session], cancellationToken).ConfigureAwait(false))[0],
            "Session created successfully.");
    }
}

public sealed record UpdateSessionCommand(Guid Id, UpdateSessionRequest Request) : IRequest<Result<ClassSessionDto>>;

public sealed class UpdateSessionCommandHandler(
    IRepository<ClassSession> sessions,
    IRepository<TeacherProfile> teachers,
    SchedulingReadModel readModel,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSessionCommand, Result<ClassSessionDto>>
{
    public async Task<Result<ClassSessionDto>> Handle(UpdateSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await sessions.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<ClassSessionDto>.Failure("Sessions.NotFound", "Session not found.");
        }

        var r = command.Request;

        if (r.TeacherProfileId is { } t)
        {
            if (!await teachers.ExistsAsync(t, cancellationToken).ConfigureAwait(false))
            {
                return Result<ClassSessionDto>.Failure("Teachers.NotFound", "Teacher not found.");
            }

            session.TeacherProfileId = t;
        }

        if (r.Status.HasValue)
        {
            session.Status = r.Status.Value;
        }

        if (r.Topic is not null)
        {
            session.Topic = Normalize.Optional(r.Topic);
        }

        if (r.Notes is not null)
        {
            session.Notes = Normalize.Optional(r.Notes);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ClassSessionDto>.Success(
            (await readModel.BuildSessionsAsync([session], cancellationToken).ConfigureAwait(false))[0],
            "Session updated successfully.");
    }
}

/// <summary>Soft-deletes a session (its attendance rows stay with it).</summary>
public sealed record DeleteSessionCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteSessionCommandHandler(IRepository<ClassSession> sessions, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteSessionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await sessions.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<bool>.Failure("Sessions.NotFound", "Session not found.");
        }

        sessions.SoftRemove(session);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Session deleted successfully.");
    }
}

/// <summary>
/// Records attendance marks for a session (upsert per student). Only students on the
/// session's roster can be marked; a cancelled session cannot take attendance.
/// </summary>
public sealed record MarkAttendanceCommand(Guid SessionId, MarkAttendanceRequest Request) : IRequest<Result<IReadOnlyList<AttendanceRowDto>>>;

public sealed class MarkAttendanceCommandHandler(
    IRepository<ClassSession> sessions,
    IRepository<Enrollment> enrollments,
    IRepository<SessionAttendance> attendance,
    IRepository<StudentProfile> students,
    IMediator mediator,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<MarkAttendanceCommand, Result<IReadOnlyList<AttendanceRowDto>>>
{
    public async Task<Result<IReadOnlyList<AttendanceRowDto>>> Handle(MarkAttendanceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var session = await sessions.GetByIdAsync(command.SessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<IReadOnlyList<AttendanceRowDto>>.Failure("Sessions.NotFound", "Session not found.");
        }

        if (session.Status == SessionStatus.Cancelled)
        {
            return Result<IReadOnlyList<AttendanceRowDto>>.Failure("Sessions.Cancelled", "A cancelled session cannot take attendance.");
        }

        var roster = (await AttendanceSupport.RosterAsync(enrollments, students, session, cancellationToken).ConfigureAwait(false))
            .Select(static s => s.Id)
            .ToHashSet();

        var records = command.Request.Records ?? [];

        if (records.Any(r => !roster.Contains(r.StudentProfileId)))
        {
            return Result<IReadOnlyList<AttendanceRowDto>>.Failure(
                "Attendance.StudentNotEnrolled",
                "Attendance can only be marked for students enrolled in the group.");
        }

        var existing = await attendance.Query
            .Where(a => a.ClassSessionId == session.Id)
            .ToDictionaryAsync(static a => a.StudentProfileId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            if (existing.TryGetValue(record.StudentProfileId, out var row))
            {
                row.Status = record.Status;
                row.Notes = Normalize.Optional(record.Notes);
            }
            else
            {
                await attendance.AddAsync(
                        new SessionAttendance
                        {
                            TenantId = tenantId,
                            ClassSessionId = session.Id,
                            StudentProfileId = record.StudentProfileId,
                            Status = record.Status,
                            Notes = Normalize.Optional(record.Notes)
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await mediator.Send(new GetSessionAttendanceQuery(session.Id), cancellationToken).ConfigureAwait(false);
    }
}

// ---- Validators --------------------------------------------------------------

public sealed class GenerateSessionsCommandValidator : LocalizedValidator<GenerateSessionsCommand>
{
    public GenerateSessionsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request)
            .Must(static r => r.To >= r.From && r.To.DayNumber - r.From.DayNumber <= SessionMaterializer.MaxRangeDays)
            .WithMessage($"The range must end on or after its start and span at most {SessionMaterializer.MaxRangeDays} days.")
            .When(static x => x.Request is not null);
    }
}

public sealed class CreateSessionCommandValidator : LocalizedValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudyGroupId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.EndTime)
            .GreaterThan(static x => x.Request.StartTime)
            .WithMessage("End time must be after start time.")
            .When(static x => x.Request is not null);
        RuleFor(x => x.Request.Topic).MaximumLength(300).When(static x => x.Request is not null);
    }
}

public sealed class UpdateSessionCommandValidator : LocalizedValidator<UpdateSessionCommand>
{
    public UpdateSessionCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Status).IsInEnum().When(static x => x.Request?.Status is not null);
        RuleFor(x => x.Request.Topic).MaximumLength(300).When(static x => x.Request?.Topic is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(1000).When(static x => x.Request?.Notes is not null);
    }
}

public sealed class MarkAttendanceCommandValidator : LocalizedValidator<MarkAttendanceCommand>
{
    public MarkAttendanceCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Records)
            .NotNull()
            .Must(static list => list is null || list.Select(static r => r.StudentProfileId).Distinct().Count() == list.Count)
            .WithMessage("A student can appear only once.")
            .When(static x => x.Request is not null);
        RuleForEach(x => x.Request.Records).ChildRules(static r =>
        {
            r.RuleFor(m => m.StudentProfileId).NotEmpty();
            r.RuleFor(m => m.Status).IsInEnum();
            r.RuleFor(m => m.Notes).MaximumLength(500);
        }).When(static x => x.Request is not null);
    }
}

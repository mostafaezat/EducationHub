using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Messaging;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Scheduling;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Learning;

// ---- DTOs --------------------------------------------------------------------

public sealed record HomeworkDto(
    Guid Id, Guid StudyGroupId, string? GroupName, string Title, string? Description, DateOnly AssignedOn, DateOnly DueDate,
    decimal? MaxGrade, int Submitted, int Graded, int RosterSize);

public sealed record SubmissionRowDto(
    Guid StudentProfileId, string StudentName, SubmissionStatus Status, DateTime? SubmittedAt, decimal? Grade, string? Feedback,
    IReadOnlyList<SubmissionFileDto> Files);

/// <summary>A file a student (or their parent) uploaded as the answer to a homework item.</summary>
public sealed record SubmissionFileDto(Guid Id, string FileName, string ContentType, long SizeInBytes, string? Note, DateTime UploadedAt);

public sealed record CreateHomeworkRequest(Guid StudyGroupId, string? Title, string? Description, DateOnly? AssignedOn, DateOnly DueDate, decimal? MaxGrade);

public sealed record UpdateHomeworkRequest(string? Title, string? Description, DateOnly? DueDate, decimal? MaxGrade);

public sealed record SubmissionUpdateRequest(Guid StudentProfileId, SubmissionStatus Status, decimal? Grade, string? Feedback);

public sealed record UpdateSubmissionsRequest(IReadOnlyList<SubmissionUpdateRequest>? Submissions);

public sealed record ExamDto(
    Guid Id, Guid StudyGroupId, string? GroupName, string Title, DateOnly ExamDate, decimal MaxScore, string? Notes,
    int ResultsRecorded, decimal? AverageScore);

public sealed record ExamResultRowDto(Guid StudentProfileId, string StudentName, decimal? Score, bool IsAbsent, string? Notes, bool Recorded);

public sealed record CreateExamRequest(Guid StudyGroupId, string? Title, DateOnly ExamDate, decimal MaxScore, string? Notes);

public sealed record UpdateExamRequest(string? Title, DateOnly? ExamDate, decimal? MaxScore, string? Notes);

public sealed record ExamResultRequest(Guid StudentProfileId, decimal? Score, bool IsAbsent, string? Notes);

public sealed record RecordExamResultsRequest(IReadOnlyList<ExamResultRequest>? Results);

public sealed record NotificationDto(Guid Id, string Title, string? Body, string? Link, bool IsRead, DateTime CreatedAt);

public sealed record NotificationFeedDto(int UnreadCount, IReadOnlyList<NotificationDto> Items);

// ---- Services ----------------------------------------------------------------

/// <summary>
/// Writes in-app notifications. Recipients are the sign-in accounts of the given students
/// and of their linked parents; students / parents without an account are skipped.
/// </summary>
public sealed class NotificationService(
    IRepository<Notification> notifications,
    IRepository<StudentProfile> students,
    IRepository<StudentGuardian> guardians,
    IRepository<ParentProfile> parents)
{
    /// <summary>Accounts of the students plus their parents, per student.</summary>
    public async Task<Dictionary<Guid, List<Guid>>> RecipientsByStudentAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        var ids = studentIds.ToArray();
        var result = ids.ToDictionary(static id => id, static _ => new List<Guid>());

        var own = await students.Query.Where(s => ids.Contains(s.Id) && s.UserId != null)
            .Select(static s => new { s.Id, UserId = s.UserId!.Value }).ToListAsync(ct).ConfigureAwait(false);

        foreach (var row in own)
        {
            result[row.Id].Add(row.UserId);
        }

        var parentAccounts = await guardians.Query.Where(g => ids.Contains(g.StudentProfileId))
            .Join(parents.Query.Where(static p => p.UserId != null), g => g.ParentProfileId, p => p.Id, (g, p) => new { g.StudentProfileId, UserId = p.UserId!.Value })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var row in parentAccounts)
        {
            result[row.StudentProfileId].Add(row.UserId);
        }

        return result;
    }

    public async Task NotifyAsync(Guid tenantId, IEnumerable<Guid> userIds, string title, string? body, string? link, CancellationToken ct)
    {
        foreach (var userId in userIds.Distinct())
        {
            await notifications.AddAsync(
                    new Notification { TenantId = tenantId, UserId = userId, Title = title, Body = body, Link = link },
                    ct)
                .ConfigureAwait(false);
        }
    }
}

internal static class LearningSupport
{
    /// <summary>Students actively enrolled in the group, by name.</summary>
    public static Task<List<RosterEntry>> ActiveRosterAsync(
        IRepository<Enrollment> enrollments, IRepository<StudentProfile> students, Guid groupId, CancellationToken ct)
        => enrollments.Query
            .Where(e => e.StudyGroupId == groupId && e.Status == EnrollmentStatus.Active)
            .Join(students.Query, e => e.StudentProfileId, s => s.Id, (_, s) => s)
            .OrderBy(static s => s.FullName)
            .Select(static s => new RosterEntry(s.Id, s.FullName))
            .ToListAsync(ct);
}

internal sealed record RosterEntry(Guid Id, string FullName);

/// <summary>
/// Homework answers are stored files owned by the submission row
/// (<c>OwnerEntityName = HomeworkSubmission</c>, <c>OwnerEntityId = submission id</c>).
/// </summary>
public static class HomeworkFiles
{
    public const string OwnerName = "HomeworkSubmission";

    /// <summary>The files of each submission, newest first.</summary>
    public static async Task<Dictionary<Guid, List<SubmissionFileDto>>> BySubmissionAsync(
        IRepository<StoredFile> files,
        IReadOnlyCollection<Guid> submissionIds,
        CancellationToken ct)
    {
        if (submissionIds.Count == 0)
        {
            return new Dictionary<Guid, List<SubmissionFileDto>>();
        }

        var ids = submissionIds.ToArray();

        var rows = await files.Query
            .Where(f => f.OwnerEntityName == OwnerName && f.OwnerEntityId != null && ids.Contains(f.OwnerEntityId.Value))
            .OrderByDescending(static f => f.CreatedAt)
            .Select(static f => new
            {
                SubmissionId = f.OwnerEntityId!.Value,
                File = new SubmissionFileDto(f.Id, f.OriginalFileName, f.ContentType, f.SizeInBytes, f.Description, f.CreatedAt)
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .GroupBy(static r => r.SubmissionId)
            .ToDictionary(static g => g.Key, static g => g.Select(static r => r.File).ToList());
    }
}

// ---- Homework ----------------------------------------------------------------

public sealed record GetHomeworkListQuery(Guid? StudyGroupId, DateOnly? DueFrom, DateOnly? DueTo) : IRequest<Result<IReadOnlyList<HomeworkDto>>>;

public sealed class GetHomeworkListQueryHandler(
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    SchedulingReadModel scheduling,
    DataScopeService dataScope)
    : IRequestHandler<GetHomeworkListQuery, Result<IReadOnlyList<HomeworkDto>>>
{
    public async Task<Result<IReadOnlyList<HomeworkDto>>> Handle(GetHomeworkListQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var query = homework.Query;

        if (!scope.IsUnrestricted)
        {
            var visible = scope.GroupIds.ToArray();
            query = query.Where(h => visible.Contains(h.StudyGroupId));
        }

        if (request.StudyGroupId.HasValue)
        {
            query = query.Where(h => h.StudyGroupId == request.StudyGroupId.Value);
        }

        if (request.DueFrom.HasValue)
        {
            query = query.Where(h => h.DueDate >= request.DueFrom.Value);
        }

        if (request.DueTo.HasValue)
        {
            query = query.Where(h => h.DueDate <= request.DueTo.Value);
        }

        var rows = await query.OrderByDescending(static h => h.DueDate).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<HomeworkDto>>.Success(await BuildAsync(rows, cancellationToken).ConfigureAwait(false));
    }

    internal async Task<IReadOnlyList<HomeworkDto>> BuildAsync(IReadOnlyList<Homework> rows, CancellationToken ct)
        => await HomeworkMapper.BuildAsync(rows, submissions, enrollments, scheduling, ct).ConfigureAwait(false);
}

internal static class HomeworkMapper
{
    public static async Task<IReadOnlyList<HomeworkDto>> BuildAsync(
        IReadOnlyList<Homework> rows, IRepository<HomeworkSubmission> submissions, IRepository<Enrollment> enrollments,
        SchedulingReadModel scheduling, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(static h => h.Id).ToArray();
        var groupIds = rows.Select(static h => h.StudyGroupId).Distinct().ToArray();
        var names = await scheduling.GroupNamesAsync(groupIds, ct).ConfigureAwait(false);

        var stats = await submissions.Query.Where(s => ids.Contains(s.HomeworkId))
            .GroupBy(static s => s.HomeworkId)
            .Select(static g => new
            {
                g.Key,
                Submitted = g.Count(static s => s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.Graded),
                Graded = g.Count(static s => s.Status == SubmissionStatus.Graded)
            })
            .ToDictionaryAsync(static x => x.Key, ct).ConfigureAwait(false);

        var rosters = await enrollments.Query.Where(e => groupIds.Contains(e.StudyGroupId) && e.Status == EnrollmentStatus.Active)
            .GroupBy(static e => e.StudyGroupId).Select(static g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.Key, static x => x.Count, ct).ConfigureAwait(false);

        return rows.Select(h =>
            {
                var s = stats.GetValueOrDefault(h.Id);
                return new HomeworkDto(h.Id, h.StudyGroupId, names.GetValueOrDefault(h.StudyGroupId), h.Title, h.Description, h.AssignedOn,
                    h.DueDate, h.MaxGrade, s?.Submitted ?? 0, s?.Graded ?? 0, rosters.GetValueOrDefault(h.StudyGroupId));
            })
            .ToList();
    }
}

public sealed record GetHomeworkSubmissionsQuery(Guid HomeworkId) : IRequest<Result<IReadOnlyList<SubmissionRowDto>>>;

public sealed class GetHomeworkSubmissionsQueryHandler(
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    IRepository<StoredFile> files,
    DataScopeService dataScope)
    : IRequestHandler<GetHomeworkSubmissionsQuery, Result<IReadOnlyList<SubmissionRowDto>>>
{
    public async Task<Result<IReadOnlyList<SubmissionRowDto>>> Handle(GetHomeworkSubmissionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var item = await homework.GetByIdAsync(request.HomeworkId, cancellationToken).ConfigureAwait(false);

        if (item is null || !scope.CanSeeGroup(item.StudyGroupId))
        {
            return Result<IReadOnlyList<SubmissionRowDto>>.Failure("Homework.NotFound", "Homework not found.");
        }

        var roster = await LearningSupport.ActiveRosterAsync(enrollments, students, item.StudyGroupId, cancellationToken).ConfigureAwait(false);
        var rows = await submissions.Query.Where(s => s.HomeworkId == item.Id)
            .ToDictionaryAsync(static s => s.StudentProfileId, cancellationToken).ConfigureAwait(false);

        var filesBySubmission = await HomeworkFiles
            .BySubmissionAsync(files, [.. rows.Values.Select(static v => v.Id)], cancellationToken)
            .ConfigureAwait(false);

        // A family sees its own children's rows only.
        var list = roster.Where(r => !scope.IsFamily || scope.StudentIds.Contains(r.Id)).Select(r =>
            {
                var s = rows.GetValueOrDefault(r.Id);
                IReadOnlyList<SubmissionFileDto> attached = s is not null && filesBySubmission.TryGetValue(s.Id, out var found) ? found : [];
                return new SubmissionRowDto(r.Id, r.FullName, s?.Status ?? SubmissionStatus.Pending, s?.SubmittedAt, s?.Grade, s?.Feedback, attached);
            })
            .ToList();

        return Result<IReadOnlyList<SubmissionRowDto>>.Success(list);
    }
}

/// <summary>Assigns homework to a group and notifies its students and their parents.</summary>
public sealed record CreateHomeworkCommand(CreateHomeworkRequest Request) : IRequest<Result<HomeworkDto>>;

public sealed class CreateHomeworkCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    SchedulingReadModel scheduling,
    NotificationService notifier,
    WhatsAppMessenger whatsApp,
    IDateTimeService clock,
    DataScopeService dataScope,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateHomeworkCommand, Result<HomeworkDto>>
{
    public async Task<Result<HomeworkDto>> Handle(CreateHomeworkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var group = scope.CanManageGroup(r.StudyGroupId)
            ? await groups.GetByIdAsync(r.StudyGroupId, cancellationToken).ConfigureAwait(false)
            : null;

        if (group is null)
        {
            return Result<HomeworkDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var item = new Homework
        {
            TenantId = tenantId,
            StudyGroupId = group.Id,
            Title = Normalize.Required(r.Title, string.Empty),
            Description = Normalize.Optional(r.Description),
            AssignedOn = r.AssignedOn ?? clock.GetTenantToday(),
            DueDate = r.DueDate,
            MaxGrade = r.MaxGrade
        };

        await homework.AddAsync(item, cancellationToken).ConfigureAwait(false);

        var roster = await LearningSupport.ActiveRosterAsync(enrollments, students, group.Id, cancellationToken).ConfigureAwait(false);
        var recipients = await notifier.RecipientsByStudentAsync(roster.Select(static s => s.Id).ToArray(), cancellationToken).ConfigureAwait(false);
        await notifier.NotifyAsync(tenantId, recipients.Values.SelectMany(static v => v), $"New homework: {item.Title}",
            $"{group.Name} - due {item.DueDate:yyyy-MM-dd}", "/my-family", cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After the commit, best-effort: parents with a mobile number also get it on WhatsApp.
        await whatsApp.NotifyParentsAsync(
                [.. roster.Select(r => new ParentUpdate(r.Id, $"واجب جديد: {item.Title}", $"{group.Name} - آخر موعد {item.DueDate:yyyy-MM-dd}"))],
                cancellationToken)
            .ConfigureAwait(false);

        return Result<HomeworkDto>.Success((await HomeworkMapper.BuildAsync([item], submissions, enrollments, scheduling, cancellationToken).ConfigureAwait(false))[0],
            "Homework created successfully.");
    }
}

public sealed record UpdateHomeworkCommand(Guid Id, UpdateHomeworkRequest Request) : IRequest<Result<HomeworkDto>>;

public sealed class UpdateHomeworkCommandHandler(
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    SchedulingReadModel scheduling,
    DataScopeService dataScope,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateHomeworkCommand, Result<HomeworkDto>>
{
    public async Task<Result<HomeworkDto>> Handle(UpdateHomeworkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var item = await homework.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (item is null || !scope.CanManageGroup(item.StudyGroupId))
        {
            return Result<HomeworkDto>.Failure("Homework.NotFound", "Homework not found.");
        }

        var r = command.Request;
        item.Title = r.Title is null ? item.Title : Normalize.Required(r.Title, item.Title);
        item.Description = r.Description is null ? item.Description : Normalize.Optional(r.Description);
        item.DueDate = r.DueDate ?? item.DueDate;
        item.MaxGrade = r.MaxGrade ?? item.MaxGrade;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<HomeworkDto>.Success((await HomeworkMapper.BuildAsync([item], submissions, enrollments, scheduling, cancellationToken).ConfigureAwait(false))[0],
            "Homework updated successfully.");
    }
}

public sealed record DeleteHomeworkCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteHomeworkCommandHandler(IRepository<Homework> homework, DataScopeService dataScope, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteHomeworkCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteHomeworkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var item = await homework.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (item is null || !scope.CanManageGroup(item.StudyGroupId))
        {
            return Result<bool>.Failure("Homework.NotFound", "Homework not found.");
        }

        homework.SoftRemove(item);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(true, "Homework deleted successfully.");
    }
}

/// <summary>Upserts submission states / grades for students on the group's active roster.</summary>
public sealed record UpdateSubmissionsCommand(Guid HomeworkId, UpdateSubmissionsRequest Request) : IRequest<Result<IReadOnlyList<SubmissionRowDto>>>;

public sealed class UpdateSubmissionsCommandHandler(
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    IMediator mediator,
    IDateTimeService clock,
    DataScopeService dataScope,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSubmissionsCommand, Result<IReadOnlyList<SubmissionRowDto>>>
{
    public async Task<Result<IReadOnlyList<SubmissionRowDto>>> Handle(UpdateSubmissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var item = await homework.GetByIdAsync(command.HomeworkId, cancellationToken).ConfigureAwait(false);

        if (item is null || !scope.CanManageGroup(item.StudyGroupId))
        {
            return Result<IReadOnlyList<SubmissionRowDto>>.Failure("Homework.NotFound", "Homework not found.");
        }

        var roster = (await LearningSupport.ActiveRosterAsync(enrollments, students, item.StudyGroupId, cancellationToken).ConfigureAwait(false))
            .Select(static s => s.Id).ToHashSet();
        var updates = command.Request.Submissions ?? [];

        if (updates.Any(u => !roster.Contains(u.StudentProfileId)))
        {
            return Result<IReadOnlyList<SubmissionRowDto>>.Failure("Homework.StudentNotEnrolled", "Only students enrolled in the group can be updated.");
        }

        if (item.MaxGrade is { } max && updates.Any(u => u.Grade > max))
        {
            return Result<IReadOnlyList<SubmissionRowDto>>.Failure("Homework.GradeAboveMax", $"A grade cannot exceed {max}.");
        }

        var existing = await submissions.Query.Where(s => s.HomeworkId == item.Id)
            .ToDictionaryAsync(static s => s.StudentProfileId, cancellationToken).ConfigureAwait(false);

        foreach (var u in updates)
        {
            if (!existing.TryGetValue(u.StudentProfileId, out var row))
            {
                row = new HomeworkSubmission { TenantId = tenantId, HomeworkId = item.Id, StudentProfileId = u.StudentProfileId };
                await submissions.AddAsync(row, cancellationToken).ConfigureAwait(false);
            }

            if (u.Status is SubmissionStatus.Submitted or SubmissionStatus.Graded && row.SubmittedAt is null)
            {
                row.SubmittedAt = clock.UtcNow;
            }

            row.Status = u.Status;
            row.Grade = u.Grade;
            row.Feedback = Normalize.Optional(u.Feedback);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await mediator.Send(new GetHomeworkSubmissionsQuery(item.Id), cancellationToken).ConfigureAwait(false);
    }
}

// ---- Exams -------------------------------------------------------------------

public sealed record GetExamsQuery(Guid? StudyGroupId) : IRequest<Result<IReadOnlyList<ExamDto>>>;

public sealed class GetExamsQueryHandler(IRepository<Exam> exams, IRepository<ExamResult> results, SchedulingReadModel scheduling, DataScopeService dataScope)
    : IRequestHandler<GetExamsQuery, Result<IReadOnlyList<ExamDto>>>
{
    public async Task<Result<IReadOnlyList<ExamDto>>> Handle(GetExamsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var query = exams.Query;

        if (!scope.IsUnrestricted)
        {
            var visible = scope.GroupIds.ToArray();
            query = query.Where(e => visible.Contains(e.StudyGroupId));
        }

        if (request.StudyGroupId.HasValue)
        {
            query = query.Where(e => e.StudyGroupId == request.StudyGroupId.Value);
        }

        var rows = await query.OrderByDescending(static e => e.ExamDate).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ExamDto>>.Success(await ExamMapper.BuildAsync(rows, results, scheduling, cancellationToken).ConfigureAwait(false));
    }
}

internal static class ExamMapper
{
    public static async Task<IReadOnlyList<ExamDto>> BuildAsync(IReadOnlyList<Exam> rows, IRepository<ExamResult> results, SchedulingReadModel scheduling, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(static e => e.Id).ToArray();
        var names = await scheduling.GroupNamesAsync(rows.Select(static e => e.StudyGroupId), ct).ConfigureAwait(false);
        var stats = await results.Query.Where(r => ids.Contains(r.ExamId))
            .GroupBy(static r => r.ExamId)
            .Select(static g => new { g.Key, Count = g.Count(), Avg = g.Where(static r => !r.IsAbsent && r.Score != null).Average(static r => r.Score) })
            .ToDictionaryAsync(static x => x.Key, ct).ConfigureAwait(false);

        return rows.Select(e =>
            {
                var s = stats.GetValueOrDefault(e.Id);
                return new ExamDto(e.Id, e.StudyGroupId, names.GetValueOrDefault(e.StudyGroupId), e.Title, e.ExamDate, e.MaxScore, e.Notes,
                    s?.Count ?? 0, s?.Avg is { } avg ? Math.Round(avg, 2) : null);
            })
            .ToList();
    }
}

public sealed record GetExamResultsQuery(Guid ExamId) : IRequest<Result<IReadOnlyList<ExamResultRowDto>>>;

public sealed class GetExamResultsQueryHandler(
    IRepository<Exam> exams,
    IRepository<ExamResult> results,
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    DataScopeService dataScope)
    : IRequestHandler<GetExamResultsQuery, Result<IReadOnlyList<ExamResultRowDto>>>
{
    public async Task<Result<IReadOnlyList<ExamResultRowDto>>> Handle(GetExamResultsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var exam = await exams.GetByIdAsync(request.ExamId, cancellationToken).ConfigureAwait(false);

        if (exam is null || !scope.CanSeeGroup(exam.StudyGroupId))
        {
            return Result<IReadOnlyList<ExamResultRowDto>>.Failure("Exams.NotFound", "Exam not found.");
        }

        var roster = await LearningSupport.ActiveRosterAsync(enrollments, students, exam.StudyGroupId, cancellationToken).ConfigureAwait(false);
        var rows = await results.Query.Where(r => r.ExamId == exam.Id)
            .ToDictionaryAsync(static r => r.StudentProfileId, cancellationToken).ConfigureAwait(false);

        // A family sees its own children's results only.
        return Result<IReadOnlyList<ExamResultRowDto>>.Success(roster.Where(s => !scope.IsFamily || scope.StudentIds.Contains(s.Id)).Select(s =>
            {
                var r = rows.GetValueOrDefault(s.Id);
                return new ExamResultRowDto(s.Id, s.FullName, r?.Score, r?.IsAbsent ?? false, r?.Notes, r is not null);
            })
            .ToList());
    }
}

public sealed record CreateExamCommand(CreateExamRequest Request) : IRequest<Result<ExamDto>>;

public sealed class CreateExamCommandHandler(
    IRepository<StudyGroup> groups,
    IRepository<Exam> exams,
    IRepository<ExamResult> results,
    SchedulingReadModel scheduling,
    DataScopeService dataScope,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateExamCommand, Result<ExamDto>>
{
    public async Task<Result<ExamDto>> Handle(CreateExamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!scope.CanManageGroup(r.StudyGroupId)
            || !await groups.ExistsAsync(r.StudyGroupId, cancellationToken).ConfigureAwait(false))
        {
            return Result<ExamDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var exam = new Exam
        {
            TenantId = tenantId,
            StudyGroupId = r.StudyGroupId,
            Title = Normalize.Required(r.Title, string.Empty),
            ExamDate = r.ExamDate,
            MaxScore = r.MaxScore,
            Notes = Normalize.Optional(r.Notes)
        };

        await exams.AddAsync(exam, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ExamDto>.Success((await ExamMapper.BuildAsync([exam], results, scheduling, cancellationToken).ConfigureAwait(false))[0], "Exam created successfully.");
    }
}

public sealed record UpdateExamCommand(Guid Id, UpdateExamRequest Request) : IRequest<Result<ExamDto>>;

public sealed class UpdateExamCommandHandler(
    IRepository<Exam> exams, IRepository<ExamResult> results, SchedulingReadModel scheduling, DataScopeService dataScope, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateExamCommand, Result<ExamDto>>
{
    public async Task<Result<ExamDto>> Handle(UpdateExamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var exam = await exams.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (exam is null || !scope.CanManageGroup(exam.StudyGroupId))
        {
            return Result<ExamDto>.Failure("Exams.NotFound", "Exam not found.");
        }

        var r = command.Request;
        exam.Title = r.Title is null ? exam.Title : Normalize.Required(r.Title, exam.Title);
        exam.ExamDate = r.ExamDate ?? exam.ExamDate;
        exam.MaxScore = r.MaxScore ?? exam.MaxScore;
        exam.Notes = r.Notes is null ? exam.Notes : Normalize.Optional(r.Notes);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<ExamDto>.Success((await ExamMapper.BuildAsync([exam], results, scheduling, cancellationToken).ConfigureAwait(false))[0], "Exam updated successfully.");
    }
}

public sealed record DeleteExamCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteExamCommandHandler(IRepository<Exam> exams, DataScopeService dataScope, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteExamCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteExamCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var exam = await exams.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (exam is null || !scope.CanManageGroup(exam.StudyGroupId))
        {
            return Result<bool>.Failure("Exams.NotFound", "Exam not found.");
        }

        exams.SoftRemove(exam);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(true, "Exam deleted successfully.");
    }
}

/// <summary>Upserts exam results and notifies each graded student and their parents.</summary>
public sealed record RecordExamResultsCommand(Guid ExamId, RecordExamResultsRequest Request) : IRequest<Result<IReadOnlyList<ExamResultRowDto>>>;

public sealed class RecordExamResultsCommandHandler(
    IRepository<Exam> exams,
    IRepository<ExamResult> results,
    IRepository<Enrollment> enrollments,
    IRepository<StudentProfile> students,
    NotificationService notifier,
    WhatsAppMessenger whatsApp,
    IMediator mediator,
    DataScopeService dataScope,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordExamResultsCommand, Result<IReadOnlyList<ExamResultRowDto>>>
{
    public async Task<Result<IReadOnlyList<ExamResultRowDto>>> Handle(RecordExamResultsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var scope = await dataScope.GetAsync(cancellationToken).ConfigureAwait(false);
        var exam = await exams.GetByIdAsync(command.ExamId, cancellationToken).ConfigureAwait(false);

        if (exam is null || !scope.CanManageGroup(exam.StudyGroupId))
        {
            return Result<IReadOnlyList<ExamResultRowDto>>.Failure("Exams.NotFound", "Exam not found.");
        }

        var roster = (await LearningSupport.ActiveRosterAsync(enrollments, students, exam.StudyGroupId, cancellationToken).ConfigureAwait(false))
            .Select(static s => s.Id).ToHashSet();
        var input = command.Request.Results ?? [];

        if (input.Any(i => !roster.Contains(i.StudentProfileId)))
        {
            return Result<IReadOnlyList<ExamResultRowDto>>.Failure("Exams.StudentNotEnrolled", "Only students enrolled in the group can get results.");
        }

        if (input.Any(i => i.Score > exam.MaxScore))
        {
            return Result<IReadOnlyList<ExamResultRowDto>>.Failure("Exams.ScoreAboveMax", $"A score cannot exceed {exam.MaxScore}.");
        }

        var existing = await results.Query.Where(r => r.ExamId == exam.Id)
            .ToDictionaryAsync(static r => r.StudentProfileId, cancellationToken).ConfigureAwait(false);

        foreach (var i in input)
        {
            if (!existing.TryGetValue(i.StudentProfileId, out var row))
            {
                row = new ExamResult { TenantId = tenantId, ExamId = exam.Id, StudentProfileId = i.StudentProfileId };
                await results.AddAsync(row, cancellationToken).ConfigureAwait(false);
            }

            row.IsAbsent = i.IsAbsent;
            row.Score = i.IsAbsent ? null : i.Score;
            row.Notes = Normalize.Optional(i.Notes);
        }

        var recipients = await notifier.RecipientsByStudentAsync(input.Select(static i => i.StudentProfileId).ToArray(), cancellationToken).ConfigureAwait(false);

        foreach (var i in input)
        {
            var body = i.IsAbsent ? "Absent" : $"{i.Score}/{exam.MaxScore}";
            await notifier.NotifyAsync(tenantId, recipients.GetValueOrDefault(i.StudentProfileId, []), $"Exam result: {exam.Title}", body, "/my-family", cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await whatsApp.NotifyParentsAsync(
                [.. input.Select(i => new ParentUpdate(
                    i.StudentProfileId,
                    $"نتيجة امتحان: {exam.Title}",
                    i.IsAbsent ? "غائب" : $"{i.Score}/{exam.MaxScore}"))],
                cancellationToken)
            .ConfigureAwait(false);

        return await mediator.Send(new GetExamResultsQuery(exam.Id), cancellationToken).ConfigureAwait(false);
    }
}

// ---- Notifications -----------------------------------------------------------

/// <summary>The caller's own notifications. Scoped by the signed-in account, never by an id.</summary>
public sealed record GetMyNotificationsQuery(bool UnreadOnly, int Take) : IRequest<Result<NotificationFeedDto>>;

public sealed class GetMyNotificationsQueryHandler(IRepository<Notification> notifications, ICurrentUser currentUser)
    : IRequestHandler<GetMyNotificationsQuery, Result<NotificationFeedDto>>
{
    public async Task<Result<NotificationFeedDto>> Handle(GetMyNotificationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return Result<NotificationFeedDto>.Success(new NotificationFeedDto(0, []));
        }

        var mine = notifications.Query.Where(n => n.UserId == userId);
        var unread = await mine.CountAsync(static n => !n.IsRead, cancellationToken).ConfigureAwait(false);
        var query = request.UnreadOnly ? mine.Where(static n => !n.IsRead) : mine;

        var items = await query.OrderByDescending(static n => n.CreatedAt).Take(Math.Clamp(request.Take, 1, 100))
            .Select(static n => new NotificationDto(n.Id, n.Title, n.Body, n.Link, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result<NotificationFeedDto>.Success(new NotificationFeedDto(unread, items));
    }
}

/// <summary>Marks one (or, with a null id, all) of the caller's notifications as read.</summary>
public sealed record MarkNotificationsReadCommand(Guid? Id) : IRequest<Result<int>>;

public sealed class MarkNotificationsReadCommandHandler(
    IRepository<Notification> notifications,
    ICurrentUser currentUser,
    IDateTimeService clock,
    IUnitOfWork unitOfWork) : IRequestHandler<MarkNotificationsReadCommand, Result<int>>
{
    public async Task<Result<int>> Handle(MarkNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId)
        {
            return Result<int>.Success(0);
        }

        var query = notifications.Query.Where(n => n.UserId == userId && !n.IsRead);

        if (command.Id is { } id)
        {
            query = query.Where(n => n.Id == id);
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        if (command.Id.HasValue && rows.Count == 0
            && !await notifications.Query.AnyAsync(n => n.Id == command.Id.Value && n.UserId == userId, cancellationToken).ConfigureAwait(false))
        {
            return Result<int>.Failure("Notifications.NotFound", "Notification not found.");
        }

        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAt = clock.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<int>.Success(rows.Count);
    }
}

// ---- Validators --------------------------------------------------------------

public sealed class CreateHomeworkCommandValidator : LocalizedValidator<CreateHomeworkCommand>
{
    public CreateHomeworkCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudyGroupId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Description).MaximumLength(2000).When(static x => x.Request is not null);
        RuleFor(x => x.Request.MaxGrade).GreaterThan(0m).When(static x => x.Request?.MaxGrade is not null);
        RuleFor(x => x.Request).Must(static r => r.AssignedOn is null || r.DueDate >= r.AssignedOn)
            .WithMessage("The due date cannot be before the assignment date.")
            .When(static x => x.Request is not null);
    }
}

public sealed class UpdateHomeworkCommandValidator : LocalizedValidator<UpdateHomeworkCommand>
{
    public UpdateHomeworkCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Title).MaximumLength(200).When(static x => x.Request?.Title is not null);
        RuleFor(x => x.Request.Description).MaximumLength(2000).When(static x => x.Request?.Description is not null);
    }
}

public sealed class UpdateSubmissionsCommandValidator : LocalizedValidator<UpdateSubmissionsCommand>
{
    public UpdateSubmissionsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Submissions).NotNull()
            .Must(static l => l is null || l.Select(static s => s.StudentProfileId).Distinct().Count() == l.Count)
            .WithMessage("A student can appear only once.")
            .When(static x => x.Request is not null);
        RuleForEach(x => x.Request.Submissions).ChildRules(static s =>
        {
            s.RuleFor(i => i.StudentProfileId).NotEmpty();
            s.RuleFor(i => i.Status).IsInEnum();
            s.RuleFor(i => i.Grade).GreaterThanOrEqualTo(0m).When(static i => i.Grade.HasValue);
            s.RuleFor(i => i.Feedback).MaximumLength(1000);
        }).When(static x => x.Request?.Submissions is not null);
    }
}

public sealed class CreateExamCommandValidator : LocalizedValidator<CreateExamCommand>
{
    public CreateExamCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudyGroupId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(200).When(static x => x.Request is not null);
        RuleFor(x => x.Request.MaxScore).GreaterThan(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(1000).When(static x => x.Request is not null);
    }
}

public sealed class UpdateExamCommandValidator : LocalizedValidator<UpdateExamCommand>
{
    public UpdateExamCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Title).MaximumLength(200).When(static x => x.Request?.Title is not null);
        RuleFor(x => x.Request.MaxScore).GreaterThan(0m).When(static x => x.Request?.MaxScore is not null);
    }
}

public sealed class RecordExamResultsCommandValidator : LocalizedValidator<RecordExamResultsCommand>
{
    public RecordExamResultsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Results).NotNull()
            .Must(static l => l is null || l.Select(static s => s.StudentProfileId).Distinct().Count() == l.Count)
            .WithMessage("A student can appear only once.")
            .When(static x => x.Request is not null);
        RuleForEach(x => x.Request.Results).ChildRules(static r =>
        {
            r.RuleFor(i => i.StudentProfileId).NotEmpty();
            r.RuleFor(i => i.Score).GreaterThanOrEqualTo(0m).When(static i => i.Score.HasValue);
            r.RuleFor(i => i.Notes).MaximumLength(500);
        }).When(static x => x.Request?.Results is not null);
    }
}

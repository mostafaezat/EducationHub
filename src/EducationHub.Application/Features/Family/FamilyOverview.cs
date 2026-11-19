using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Finance;
using EducationHub.Application.Features.Learning;
using EducationHub.Application.Features.Scheduling;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Family;

// ---- DTOs --------------------------------------------------------------------

public sealed record FamilyAttendanceSummaryDto(int Present, int Absent, int Late, int Excused, decimal? AttendanceRate);

/// <summary>A session of one of the student's groups, with the student's own attendance mark.</summary>
public sealed record FamilySessionDto(ClassSessionDto Session, AttendanceStatus? Attendance);

/// <summary>A homework item with the student's own submission state and grade.</summary>
public sealed record FamilyHomeworkDto(
    Guid Id,
    Guid StudyGroupId,
    string? GroupName,
    string Title,
    string? Description,
    DateOnly AssignedOn,
    DateOnly DueDate,
    decimal? MaxGrade,
    SubmissionStatus Status,
    DateTime? SubmittedAt,
    decimal? Grade,
    string? Feedback,
    IReadOnlyList<SubmissionFileDto> Files,
    bool CanSubmit);

/// <summary>An exam with the student's own result (when it has been recorded).</summary>
public sealed record FamilyExamDto(
    Guid Id,
    Guid StudyGroupId,
    string? GroupName,
    string Title,
    DateOnly ExamDate,
    decimal MaxScore,
    bool Recorded,
    decimal? Score,
    bool IsAbsent,
    string? Notes);

/// <summary>Everything a student, or their parent, follows about that one student.</summary>
public sealed record FamilyStudentOverviewDto(
    Guid StudentProfileId,
    string FullName,
    string? StudentNumber,
    IReadOnlyList<StudyGroupDto> Groups,
    FamilyAttendanceSummaryDto Attendance,
    IReadOnlyList<FamilySessionDto> RecentSessions,
    IReadOnlyList<FamilySessionDto> UpcomingSessions,
    IReadOnlyList<FamilyHomeworkDto> Homework,
    IReadOnlyList<FamilyExamDto> Exams,
    StudentBalanceDto? Balance,
    string Currency = "EGP");

// ---- Query -------------------------------------------------------------------

/// <summary>
/// The signed-in account's family view: the student themself, or every child linked to the
/// parent. Scoped by the caller's own account and takes no id, so one family can never ask
/// for another's data; staff accounts simply get an empty list.
/// </summary>
public sealed record GetMyFamilyOverviewQuery : IRequest<Result<IReadOnlyList<FamilyStudentOverviewDto>>>;

public sealed class GetMyFamilyOverviewQueryHandler(
    ICurrentUser currentUser,
    DataScopeService dataScope,
    IRepository<StudentProfile> students,
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments,
    IRepository<ClassSession> sessions,
    IRepository<SessionAttendance> attendance,
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<StoredFile> files,
    IRepository<Exam> exams,
    IRepository<ExamResult> results,
    SchedulingReadModel scheduling,
    IDateTimeService clock,
    IMediator mediator,
    IRepository<EducationHub.Domain.Entities.Tenancy.Tenant> tenants)
    : IRequestHandler<GetMyFamilyOverviewQuery, Result<IReadOnlyList<FamilyStudentOverviewDto>>>
{
    private const int RecentSessionCount = 15;
    private const int UpcomingSessionCount = 10;
    private const int LearningItemCount = 30;

    public async Task<Result<IReadOnlyList<FamilyStudentOverviewDto>>> Handle(
        GetMyFamilyOverviewQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return Result<IReadOnlyList<FamilyStudentOverviewDto>>.Failure("Auth.Unauthenticated", "Authentication is required.");
        }

        var studentIds = await dataScope.FamilyStudentIdsAsync(userId, cancellationToken).ConfigureAwait(false);

        if (studentIds.Count == 0)
        {
            return Result<IReadOnlyList<FamilyStudentOverviewDto>>.Success([]);
        }

        var ids = studentIds.ToArray();
        var profiles = await students.Query
            .Where(s => ids.Contains(s.Id))
            .OrderBy(static s => s.FullName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var today = clock.GetTenantToday();
        var list = new List<FamilyStudentOverviewDto>(profiles.Count);

        // The centre's currency (ISO code, e.g. EGP), so the family pages can label amounts.
        var currency = profiles.Count == 0
            ? null
            : await tenants.Query
                .Where(t => t.Id == profiles[0].TenantId)
                .Select(static t => t.Currency)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

        foreach (var profile in profiles)
        {
            var overview = await BuildAsync(profile, today, cancellationToken).ConfigureAwait(false);
            list.Add(overview with { Currency = string.IsNullOrWhiteSpace(currency) ? "EGP" : currency });
        }

        return Result<IReadOnlyList<FamilyStudentOverviewDto>>.Success(list);
    }

    private async Task<FamilyStudentOverviewDto> BuildAsync(StudentProfile student, DateOnly today, CancellationToken ct)
    {
        var studentEnrollments = await enrollments.Query
            .Where(e => e.StudentProfileId == student.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var groupIds = studentEnrollments.Select(static e => e.StudyGroupId).Distinct().ToArray();

        // Only the time the student was actually in each group counts: sessions, homework and
        // exams of a group they left belong to the period they were enrolled.
        bool Covers(Guid groupId, DateOnly date)
            => studentEnrollments.Any(e => e.StudyGroupId == groupId
                && e.EnrolledOn <= date
                && (e.Status == EnrollmentStatus.Active || (e.EndedOn != null && e.EndedOn >= date)));

        var activeGroupIds = studentEnrollments
            .Where(static e => e.Status == EnrollmentStatus.Active)
            .Select(static e => e.StudyGroupId)
            .Distinct()
            .ToArray();

        var groupRows = await groups.Query
            .Where(g => activeGroupIds.Contains(g.Id))
            .OrderBy(static g => g.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var groupDtos = await scheduling.BuildGroupsAsync(groupRows, ct).ConfigureAwait(false);
        var groupNames = await scheduling.GroupNamesAsync(groupIds, ct).ConfigureAwait(false);

        // ---- Sessions and attendance ----
        var recentRows = (await sessions.Query
                .Where(s => groupIds.Contains(s.StudyGroupId) && s.Date < today)
                .OrderByDescending(static s => s.Date)
                .ThenByDescending(static s => s.StartTime)
                .Take(RecentSessionCount * 3)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(s => Covers(s.StudyGroupId, s.Date))
            .Take(RecentSessionCount)
            .ToList();

        var upcomingRows = (await sessions.Query
                .Where(s => groupIds.Contains(s.StudyGroupId) && s.Date >= today && s.Status != SessionStatus.Cancelled)
                .OrderBy(static s => s.Date)
                .ThenBy(static s => s.StartTime)
                .Take(UpcomingSessionCount * 3)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(s => Covers(s.StudyGroupId, s.Date))
            .Take(UpcomingSessionCount)
            .ToList();

        var marks = await attendance.Query
            .Where(a => a.StudentProfileId == student.Id)
            .Join(sessions.Query, a => a.ClassSessionId, s => s.Id, (a, s) => new { a.ClassSessionId, a.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var markBySession = marks
            .GroupBy(static m => m.ClassSessionId)
            .ToDictionary(static g => g.Key, static g => g.First().Status);

        var recent = await ToFamilySessionsAsync(recentRows, markBySession, ct).ConfigureAwait(false);
        var upcoming = await ToFamilySessionsAsync(upcomingRows, markBySession, ct).ConfigureAwait(false);

        var present = marks.Count(static m => m.Status == AttendanceStatus.Present);
        var absent = marks.Count(static m => m.Status == AttendanceStatus.Absent);
        var late = marks.Count(static m => m.Status == AttendanceStatus.Late);
        var excused = marks.Count(static m => m.Status == AttendanceStatus.Excused);
        var counted = present + absent + late;
        decimal? rate = counted == 0 ? null : Math.Round((present + late) * 100m / counted, 1);

        // ---- Homework with the student's own submission ----
        var homeworkRows = (await homework.Query
                .Where(h => groupIds.Contains(h.StudyGroupId))
                .OrderByDescending(static h => h.DueDate)
                .Take(LearningItemCount * 2)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(h => Covers(h.StudyGroupId, h.DueDate) || Covers(h.StudyGroupId, h.AssignedOn))
            .Take(LearningItemCount)
            .ToList();

        var homeworkIds = homeworkRows.Select(static h => h.Id).ToArray();
        var ownSubmissions = await submissions.Query
            .Where(s => s.StudentProfileId == student.Id && homeworkIds.Contains(s.HomeworkId))
            .ToDictionaryAsync(static s => s.HomeworkId, ct)
            .ConfigureAwait(false);

        var filesBySubmission = await HomeworkFiles
            .BySubmissionAsync(files, [.. ownSubmissions.Values.Select(static v => v.Id)], ct)
            .ConfigureAwait(false);

        // Answers can be uploaded while the student is still in the group and the work is not graded.
        var activeGroupSet = activeGroupIds.ToHashSet();

        var homeworkDtos = homeworkRows
            .Select(h =>
            {
                var sub = ownSubmissions.GetValueOrDefault(h.Id);
                IReadOnlyList<SubmissionFileDto> attached = sub is not null && filesBySubmission.TryGetValue(sub.Id, out var found) ? found : [];
                return new FamilyHomeworkDto(
                    h.Id, h.StudyGroupId, groupNames.GetValueOrDefault(h.StudyGroupId), h.Title, h.Description,
                    h.AssignedOn, h.DueDate, h.MaxGrade,
                    sub?.Status ?? SubmissionStatus.Pending, sub?.SubmittedAt, sub?.Grade, sub?.Feedback,
                    attached,
                    activeGroupSet.Contains(h.StudyGroupId) && sub?.Status != SubmissionStatus.Graded);
            })
            .ToList();

        // ---- Exams with the student's own result ----
        var examRows = (await exams.Query
                .Where(e => groupIds.Contains(e.StudyGroupId))
                .OrderByDescending(static e => e.ExamDate)
                .Take(LearningItemCount * 2)
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .Where(e => Covers(e.StudyGroupId, e.ExamDate))
            .Take(LearningItemCount)
            .ToList();

        var examIds = examRows.Select(static e => e.Id).ToArray();
        var ownResults = await results.Query
            .Where(r => r.StudentProfileId == student.Id && examIds.Contains(r.ExamId))
            .ToDictionaryAsync(static r => r.ExamId, ct)
            .ConfigureAwait(false);

        var examDtos = examRows
            .Select(e =>
            {
                var res = ownResults.GetValueOrDefault(e.Id);
                return new FamilyExamDto(
                    e.Id, e.StudyGroupId, groupNames.GetValueOrDefault(e.StudyGroupId), e.Title, e.ExamDate, e.MaxScore,
                    res is not null, res?.Score, res?.IsAbsent ?? false, res?.Notes);
            })
            .ToList();

        // ---- Fees: the same balance the centre sees for this student ----
        var balance = await mediator.Send(new GetStudentBalanceQuery(student.Id), ct).ConfigureAwait(false);

        return new FamilyStudentOverviewDto(
            student.Id,
            student.FullName,
            student.StudentNumber,
            groupDtos,
            new FamilyAttendanceSummaryDto(present, absent, late, excused, rate),
            recent,
            upcoming,
            homeworkDtos,
            examDtos,
            balance.Succeeded ? balance.Data : null);
    }

    private async Task<IReadOnlyList<FamilySessionDto>> ToFamilySessionsAsync(
        IReadOnlyList<ClassSession> rows,
        Dictionary<Guid, AttendanceStatus> markBySession,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var dtos = await scheduling.BuildSessionsAsync(rows, ct).ConfigureAwait(false);

        return dtos
            .Select(d => new FamilySessionDto(d, markBySession.TryGetValue(d.Id, out var mark) ? mark : (AttendanceStatus?)null))
            .ToList();
    }
}

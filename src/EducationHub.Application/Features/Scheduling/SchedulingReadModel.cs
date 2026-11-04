using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Scheduling;

/// <summary>
/// Builds scheduling DTOs (with subject/grade/teacher/student display names) in a fixed
/// number of round trips. Every repository is tenant-filtered.
/// </summary>
public sealed class SchedulingReadModel(
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments,
    IRepository<ScheduleSlot> slots,
    IRepository<SessionAttendance> attendance,
    IRepository<Subject> subjects,
    IRepository<GradeLevel> grades,
    IRepository<TeacherProfile> teachers,
    IRepository<StudentProfile> students,
    UserManager<ApplicationUser> userManager)
{
    public async Task<Dictionary<Guid, string>> TeacherNamesAsync(
        IEnumerable<Guid?> teacherIds,
        CancellationToken cancellationToken)
    {
        var ids = teacherIds.Where(static i => i.HasValue).Select(static i => i!.Value).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await teachers.Query
            .Where(t => ids.Contains(t.Id))
            .Join(userManager.Users, t => t.UserId, u => u.Id, (t, u) => new { t.Id, u.FullName })
            .ToDictionaryAsync(static x => x.Id, static x => x.FullName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<Guid, string>> GroupNamesAsync(IEnumerable<Guid> groupIds, CancellationToken cancellationToken)
    {
        var ids = groupIds.Distinct().ToArray();

        return ids.Length == 0
            ? new Dictionary<Guid, string>()
            : await groups.Query
                .Where(g => ids.Contains(g.Id))
                .ToDictionaryAsync(static g => g.Id, static g => g.Name, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudyGroupDto>> BuildGroupsAsync(
        IReadOnlyList<StudyGroup> page,
        CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(static g => g.Id).ToArray();

        var subjectIds = page.Select(static g => g.SubjectId).Distinct().ToArray();
        var subjectNames = await subjects.Query
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(static s => s.Id, static s => s.Name, cancellationToken)
            .ConfigureAwait(false);

        var gradeIds = page.Where(static g => g.GradeLevelId.HasValue).Select(static g => g.GradeLevelId!.Value).Distinct().ToArray();
        var gradeNames = gradeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await grades.Query
                .Where(g => gradeIds.Contains(g.Id))
                .ToDictionaryAsync(static g => g.Id, static g => g.Name, cancellationToken)
                .ConfigureAwait(false);

        var teacherNames = await TeacherNamesAsync(page.Select(static g => g.TeacherProfileId), cancellationToken)
            .ConfigureAwait(false);

        var counts = await enrollments.Query
            .Where(e => ids.Contains(e.StudyGroupId) && e.Status == EnrollmentStatus.Active)
            .GroupBy(static e => e.StudyGroupId)
            .Select(static g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.Key, static x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var slotRows = await slots.Query
            .Where(s => ids.Contains(s.StudyGroupId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var slotsByGroup = slotRows
            .GroupBy(static s => s.StudyGroupId)
            .ToDictionary(
                static g => g.Key,
                static g => (IReadOnlyList<ScheduleSlotDto>)g
                    .OrderBy(static s => s.DayOfWeek)
                    .ThenBy(static s => s.StartTime)
                    .Select(ToDto)
                    .ToList());

        return page
            .Select(g => new StudyGroupDto(
                g.Id,
                g.Name,
                g.Code,
                g.SubjectId,
                subjectNames.GetValueOrDefault(g.SubjectId),
                g.GradeLevelId,
                g.GradeLevelId.HasValue ? gradeNames.GetValueOrDefault(g.GradeLevelId.Value) : null,
                g.TeacherProfileId,
                g.TeacherProfileId.HasValue ? teacherNames.GetValueOrDefault(g.TeacherProfileId.Value) : null,
                g.AcademicYearId,
                g.BranchId,
                g.Capacity,
                g.IsActive,
                g.Notes,
                counts.GetValueOrDefault(g.Id),
                slotsByGroup.GetValueOrDefault(g.Id, []),
                g.CreatedAt))
            .ToList();
    }

    public async Task<StudyGroupDto> BuildGroupAsync(StudyGroup group, CancellationToken cancellationToken)
        => (await BuildGroupsAsync([group], cancellationToken).ConfigureAwait(false))[0];

    public static ScheduleSlotDto ToDto(ScheduleSlot s)
        => new(s.Id, s.StudyGroupId, s.DayOfWeek, s.StartTime, s.EndTime, s.Room, s.EffectiveFrom, s.EffectiveTo);

    public async Task<IReadOnlyList<EnrollmentDto>> BuildEnrollmentsAsync(
        IReadOnlyList<Enrollment> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var studentIds = rows.Select(static r => r.StudentProfileId).Distinct().ToArray();
        var studentInfo = await students.Query
            .Where(s => studentIds.Contains(s.Id))
            .Select(static s => new { s.Id, s.FullName, s.StudentNumber })
            .ToDictionaryAsync(static s => s.Id, cancellationToken)
            .ConfigureAwait(false);

        var groupNames = await GroupNamesAsync(rows.Select(static r => r.StudyGroupId), cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r =>
            {
                var info = studentInfo.GetValueOrDefault(r.StudentProfileId);
                return new EnrollmentDto(
                    r.Id,
                    r.StudyGroupId,
                    groupNames.GetValueOrDefault(r.StudyGroupId),
                    r.StudentProfileId,
                    info?.FullName,
                    info?.StudentNumber,
                    r.EnrolledOn,
                    r.EndedOn,
                    r.Status);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ClassSessionDto>> BuildSessionsAsync(
        IReadOnlyList<ClassSession> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(static r => r.Id).ToArray();
        var groupNames = await GroupNamesAsync(rows.Select(static r => r.StudyGroupId), cancellationToken).ConfigureAwait(false);
        var teacherNames = await TeacherNamesAsync(rows.Select(static r => r.TeacherProfileId), cancellationToken).ConfigureAwait(false);

        var marks = await attendance.Query
            .Where(a => ids.Contains(a.ClassSessionId))
            .GroupBy(static a => a.ClassSessionId)
            .Select(static g => new
            {
                g.Key,
                Marked = g.Count(),
                Present = g.Count(static a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late)
            })
            .ToDictionaryAsync(static x => x.Key, cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r =>
            {
                var mark = marks.GetValueOrDefault(r.Id);
                return new ClassSessionDto(
                    r.Id,
                    r.StudyGroupId,
                    groupNames.GetValueOrDefault(r.StudyGroupId),
                    r.ScheduleSlotId,
                    r.Date,
                    r.StartTime,
                    r.EndTime,
                    r.TeacherProfileId,
                    r.TeacherProfileId.HasValue ? teacherNames.GetValueOrDefault(r.TeacherProfileId.Value) : null,
                    r.Status,
                    r.Topic,
                    r.Notes,
                    mark?.Marked ?? 0,
                    mark?.Present ?? 0);
            })
            .ToList();
    }
}

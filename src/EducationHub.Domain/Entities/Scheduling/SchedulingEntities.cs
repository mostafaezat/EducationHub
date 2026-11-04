using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Scheduling;

// Mission Phase 3 (original Phase 4). The scheduling aggregate is deliberately
// navigation-free: every relationship is an FK id configured in
// StudyGroupsConfigurations, and handlers join explicitly. That keeps each read's shape
// visible in the handler and avoids accidental lazy graphs across tenant-filtered sets.

/// <summary>
/// A class: one subject taught to a set of students, usually by one teacher, on a weekly
/// timetable. Enrollments, schedule slots, sessions, fees and homework all hang from it.
/// </summary>
public class StudyGroup : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional short code, e.g. <c>G4-MATH-A</c>.</summary>
    public string? Code { get; set; }

    public Guid SubjectId { get; set; }

    public Guid? GradeLevelId { get; set; }

    /// <summary>Default teacher; materialized sessions inherit it.</summary>
    public Guid? TeacherProfileId { get; set; }

    public Guid? AcademicYearId { get; set; }

    public Guid? BranchId { get; set; }

    /// <summary>Maximum active enrollments; null means unlimited.</summary>
    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}

/// <summary>A student's membership in a study group. Ending it keeps the row for history.</summary>
public class Enrollment : TenantOwnedEntity
{
    public Guid StudyGroupId { get; set; }

    public Guid StudentProfileId { get; set; }

    public DateOnly EnrolledOn { get; set; }

    public DateOnly? EndedOn { get; set; }

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
}

/// <summary>A weekly timetable entry for a group (e.g. every Sunday 16:00-17:30).</summary>
public class ScheduleSlot : TenantOwnedEntity
{
    public Guid StudyGroupId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public string? Room { get; set; }

    /// <summary>First date the slot applies; null = from the start.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Last date the slot applies; null = open-ended.</summary>
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>
/// One dated meeting of a group - materialized from a schedule slot, or created ad hoc
/// (no slot). Unique per (slot, date), so materializing twice never duplicates.
/// </summary>
public class ClassSession : TenantOwnedEntity
{
    public Guid StudyGroupId { get; set; }

    public Guid? ScheduleSlotId { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Who teaches this session (defaults to the group's teacher).</summary>
    public Guid? TeacherProfileId { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

    public string? Topic { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A student's attendance mark for one session. One row per (session, student).</summary>
public class SessionAttendance : TenantOwnedEntity
{
    public Guid ClassSessionId { get; set; }

    public Guid StudentProfileId { get; set; }

    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    public string? Notes { get; set; }
}

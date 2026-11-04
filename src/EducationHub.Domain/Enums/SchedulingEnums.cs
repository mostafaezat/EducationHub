namespace EducationHub.Domain.Enums;

/// <summary>Lifecycle of a student's membership in a study group. Stored as an int.</summary>
public enum EnrollmentStatus
{
    Active = 0,
    Completed = 1,
    Withdrawn = 2
}

/// <summary>Lifecycle of one class session. Stored as an int.</summary>
public enum SessionStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2
}

/// <summary>A student's attendance at one session. Stored as an int.</summary>
public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    Late = 2,
    Excused = 3
}

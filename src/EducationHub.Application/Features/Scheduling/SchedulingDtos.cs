using EducationHub.Domain.Enums;

namespace EducationHub.Application.Features.Scheduling;

public sealed record ScheduleSlotDto(
    Guid Id,
    Guid StudyGroupId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Room,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record StudyGroupDto(
    Guid Id,
    string Name,
    string? Code,
    Guid SubjectId,
    string? SubjectName,
    Guid? GradeLevelId,
    string? GradeLevelName,
    Guid? TeacherProfileId,
    string? TeacherName,
    Guid? AcademicYearId,
    Guid? BranchId,
    int? Capacity,
    bool IsActive,
    string? Notes,
    int ActiveEnrollments,
    IReadOnlyList<ScheduleSlotDto> Slots,
    DateTime CreatedAt);

public sealed record EnrollmentDto(
    Guid Id,
    Guid StudyGroupId,
    string? GroupName,
    Guid StudentProfileId,
    string? StudentName,
    string? StudentNumber,
    DateOnly EnrolledOn,
    DateOnly? EndedOn,
    EnrollmentStatus Status);

public sealed record ClassSessionDto(
    Guid Id,
    Guid StudyGroupId,
    string? GroupName,
    Guid? ScheduleSlotId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    Guid? TeacherProfileId,
    string? TeacherName,
    SessionStatus Status,
    string? Topic,
    string? Notes,
    int MarkedCount,
    int PresentCount);

/// <summary>One roster line: an actively enrolled student and their mark (null = not marked yet).</summary>
public sealed record AttendanceRowDto(
    Guid StudentProfileId,
    string StudentName,
    string? StudentNumber,
    AttendanceStatus? Status,
    string? Notes);

public sealed record CreateStudyGroupRequest(
    string? Name,
    string? Code,
    Guid SubjectId,
    Guid? GradeLevelId,
    Guid? TeacherProfileId,
    Guid? AcademicYearId,
    Guid? BranchId,
    int? Capacity,
    string? Notes);

/// <summary>Partial update: a null member leaves the field unchanged.</summary>
public sealed record UpdateStudyGroupRequest(
    string? Name,
    string? Code,
    Guid? SubjectId,
    Guid? GradeLevelId,
    Guid? TeacherProfileId,
    Guid? AcademicYearId,
    Guid? BranchId,
    int? Capacity,
    bool? IsActive,
    string? Notes);

public sealed record EnrollStudentRequest(Guid StudentProfileId, DateOnly? EnrolledOn);

public sealed record CreateScheduleSlotRequest(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? Room,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record CreateSessionRequest(
    Guid StudyGroupId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    Guid? TeacherProfileId,
    string? Topic);

/// <summary>Partial update of a session: status, topic, notes, substitute teacher.</summary>
public sealed record UpdateSessionRequest(
    SessionStatus? Status,
    string? Topic,
    string? Notes,
    Guid? TeacherProfileId);

/// <summary>Materializes sessions from schedule slots for a date range (max 120 days).</summary>
public sealed record GenerateSessionsRequest(DateOnly From, DateOnly To, Guid? StudyGroupId);

public sealed record GenerateSessionsResultDto(int Created, int Skipped);

public sealed record AttendanceMarkRequest(Guid StudentProfileId, AttendanceStatus Status, string? Notes);

public sealed record MarkAttendanceRequest(IReadOnlyList<AttendanceMarkRequest>? Records);

using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Learning;

// Mission Phase 6 (original Phases 5 + 7). Navigation-free, like scheduling and finance.

/// <summary>An assignment given to a study group.</summary>
public class Homework : TenantOwnedEntity
{
    public Guid StudyGroupId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly AssignedOn { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>Optional maximum grade; null = not graded numerically.</summary>
    public decimal? MaxGrade { get; set; }
}

/// <summary>One student's state for one homework. Unique per (homework, student).</summary>
public class HomeworkSubmission : TenantOwnedEntity
{
    public Guid HomeworkId { get; set; }

    public Guid StudentProfileId { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;

    public DateTime? SubmittedAt { get; set; }

    public decimal? Grade { get; set; }

    public string? Feedback { get; set; }
}

/// <summary>An exam / quiz held for a study group.</summary>
public class Exam : TenantOwnedEntity
{
    public Guid StudyGroupId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateOnly ExamDate { get; set; }

    public decimal MaxScore { get; set; }

    public string? Notes { get; set; }
}

/// <summary>One student's result in one exam. Unique per (exam, student).</summary>
public class ExamResult : TenantOwnedEntity
{
    public Guid ExamId { get; set; }

    public Guid StudentProfileId { get; set; }

    public decimal? Score { get; set; }

    public bool IsAbsent { get; set; }

    public string? Notes { get; set; }
}

/// <summary>An in-app notification for one user account.</summary>
public class Notification : TenantOwnedEntity
{
    public Guid UserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    /// <summary>Relative SPA route the notification points at, e.g. <c>/homework</c>.</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }
}

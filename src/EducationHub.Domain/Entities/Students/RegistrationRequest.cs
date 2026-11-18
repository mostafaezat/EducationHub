using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Students;

/// <summary>
/// A parent's request, sent from the centre's public form, to register their child. Staff
/// approve it (which creates the student and links or creates the guardian) or reject it.
/// </summary>
public class RegistrationRequest : TenantOwnedEntity
{
    public string GuardianName { get; set; } = string.Empty;

    /// <summary>Canonical international form (<c>+201012345678</c>).</summary>
    public string GuardianPhone { get; set; } = string.Empty;

    public string? GuardianEmail { get; set; }

    public GuardianRelationship Relationship { get; set; } = GuardianRelationship.Father;

    public string StudentName { get; set; } = string.Empty;

    public DateOnly? BirthDate { get; set; }

    public Guid? GradeLevelId { get; set; }

    public string? School { get; set; }

    /// <summary>Subject names the guardian ticked on the form, comma separated.</summary>
    public string? PreferredSubjects { get; set; }

    public string? Notes { get; set; }

    public RegistrationRequestStatus Status { get; set; } = RegistrationRequestStatus.Pending;

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>The student created on approval.</summary>
    public Guid? CreatedStudentProfileId { get; set; }

    /// <summary>Remote address the form was posted from (abuse investigation only).</summary>
    public string? SourceIp { get; set; }

    /// <summary>Optimistic concurrency: two staff members cannot both review the same request.</summary>
    public byte[] RowVersion { get; set; } = [];
}

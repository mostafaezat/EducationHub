using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Students;

/// <summary>
/// Links a student to one of their parents / guardians.
/// </summary>
/// <remarks>
/// A replaceable junction, like <c>TeacherSubject</c>: replacing a student's guardians
/// hard-removes and re-adds rows, because the unique index on
/// (TenantId, StudentProfileId, ParentProfileId) has no <c>IsDeleted</c> filter.
/// At most one link per student is <see cref="IsPrimary"/>; the handler enforces it.
/// </remarks>
public class StudentGuardian : TenantOwnedEntity
{
    public Guid StudentProfileId { get; set; }

    public Guid ParentProfileId { get; set; }

    public GuardianRelationship Relationship { get; set; } = GuardianRelationship.Guardian;

    /// <summary>The main contact for the student.</summary>
    public bool IsPrimary { get; set; }

    public StudentProfile? Student { get; set; }

    public ParentProfile? Parent { get; set; }
}

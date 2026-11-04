using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Students;

/// <summary>
/// A student's record in a tenant.
/// </summary>
/// <remarks>
/// <para>
/// Unlike a teacher, a student does not need a sign-in account: young children are managed
/// through their parents. The name therefore lives on the profile, and <see cref="UserId"/>
/// is an optional one-to-one link to an Identity account (for example the demo students the
/// startup seeder produces).
/// </para>
/// <para>
/// Grade and branch references use restrict-on-delete: both are soft-deleted in practice,
/// and a hard delete must never silently orphan or remove student records.
/// </para>
/// </remarks>
public class StudentProfile : TenantOwnedEntity
{
    /// <summary>Optional linked sign-in account. At most one profile per account.</summary>
    public Guid? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Optional per-tenant student number, e.g. <c>S-2026-001</c>. Unique when present.</summary>
    public string? StudentNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>The grade the student is currently in.</summary>
    public Guid? GradeLevelId { get; set; }

    public Guid? BranchId { get; set; }

    public StudentStatus Status { get; set; } = StudentStatus.Active;

    /// <summary>When the student joined the school.</summary>
    public DateOnly? EnrollmentDate { get; set; }

    public string? Notes { get; set; }

    public ApplicationUser? User { get; set; }

    public GradeLevel? GradeLevel { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>The student's parents / guardians.</summary>
    public ICollection<StudentGuardian> Guardians { get; set; } = new List<StudentGuardian>();
}

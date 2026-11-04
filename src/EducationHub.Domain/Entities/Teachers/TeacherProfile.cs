using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Identity;

namespace EducationHub.Domain.Entities.Teachers;

/// <summary>
/// A teacher's staff record, linked one-to-one with the Identity account they sign in with.
/// </summary>
/// <remarks>
/// <para>
/// The person behind the account (name, e-mail, phone) lives on <c>ApplicationUser</c> and
/// stays owned by the Users module; this row carries only what is specific to being a
/// teacher: staff number, hire date, teaching status and notes. That split keeps a single
/// source of truth for identity data while giving the teachers module a stable aggregate
/// for subjects and compensation rules to hang from.
/// </para>
/// <para>
/// Created atomically together with a brand-new user, or linked to an existing account
/// (for example the demo teachers the startup seeder produces) - never both at once.
/// </para>
/// </remarks>
public class TeacherProfile : TenantOwnedEntity
{
    /// <summary>The Identity account this profile is attached to. One profile per user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Optional per-tenant staff number, e.g. <c>T-001</c>. Unique when present.</summary>
    public string? EmployeeNumber { get; set; }

    /// <summary>When the teacher joined. Informational only; drives no rules yet.</summary>
    public DateOnly? HireDate { get; set; }

    /// <summary>Teaching status. Deactivating keeps the record and its history intact.</summary>
    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>Subjects this teacher is assigned to teach.</summary>
    public ICollection<TeacherSubject> Subjects { get; set; } = new List<TeacherSubject>();

    /// <summary>Compensation rules scoped to this teacher.</summary>
    public ICollection<CompensationRule> CompensationRules { get; set; } = new List<CompensationRule>();
}

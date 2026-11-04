using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Identity;

namespace EducationHub.Domain.Entities.Students;

/// <summary>
/// A parent or guardian in a tenant.
/// </summary>
/// <remarks>
/// Contact details live on the profile because many parents never sign in. When a parent
/// does have an account (<see cref="UserId"/>), that account is what scopes the
/// "my children" view: a parent sees exactly the students linked to their own profile.
/// </remarks>
public class ParentProfile : TenantOwnedEntity
{
    /// <summary>Optional linked sign-in account. At most one profile per account.</summary>
    public Guid? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>Contact e-mail. Independent of the linked account's sign-in e-mail.</summary>
    public string? Email { get; set; }

    public string? Notes { get; set; }

    public ApplicationUser? User { get; set; }

    /// <summary>The students this parent is linked to.</summary>
    public ICollection<StudentGuardian> Children { get; set; } = new List<StudentGuardian>();
}

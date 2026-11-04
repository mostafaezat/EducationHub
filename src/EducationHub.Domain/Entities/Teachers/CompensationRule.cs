using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Teachers;

/// <summary>
/// A compensation rate, scoped by any combination of teacher, subject and study group.
/// </summary>
/// <remarks>
/// <para>
/// Resolution follows an explicit precedence ladder (see
/// <c>EducationHub.Application.Features.Teachers.Services.CompensationResolver</c>):
/// Group+Teacher &gt; Teacher+Subject &gt; Teacher &gt; Group &gt; Subject &gt; TenantDefault.
/// A <c>null</c> dimension acts as a wildcard, so a rule with every dimension null is the
/// tenant's default rate.
/// </para>
/// <para>
/// <c>StudyGroupId</c> is a plain column with no foreign key yet: the <c>StudyGroups</c>
/// entity arrives with the scheduling module (Mission Phase 3), which will also add the
/// constraint. Until then the API rejects non-null group ids so no orphan references can
/// be written through the product.
/// </para>
/// </remarks>
public class CompensationRule : TenantOwnedEntity
{
    /// <summary>Teacher scope, or null for a subject-/group-/tenant-wide rule.</summary>
    public Guid? TeacherProfileId { get; set; }

    /// <summary>Subject scope, or null when the rule does not care about the subject.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Study-group scope. Unconstrained until Mission Phase 3 introduces StudyGroups.</summary>
    public Guid? StudyGroupId { get; set; }

    /// <summary>The rate expressed in the tenant's currency. Never negative.</summary>
    public decimal Rate { get; set; }

    /// <summary>What one unit of <see cref="Rate"/> buys: a session or an hour.</summary>
    public CompensationRateBasis RateBasis { get; set; } = CompensationRateBasis.PerSession;

    /// <summary>Inactive rules are kept for history but never resolved.</summary>
    public bool IsActive { get; set; } = true;

    public TeacherProfile? TeacherProfile { get; set; }

    public Subject? Subject { get; set; }
}

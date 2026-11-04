using EducationHub.Domain.Common;
using EducationHub.Domain.Entities.Academic;

namespace EducationHub.Domain.Entities.Teachers;

/// <summary>
/// Which subjects a teacher is assigned to teach.
/// </summary>
/// <remarks>
/// A plain junction row. Replacing a teacher's subject set removes and re-adds rows rather
/// than soft-deleting them: the unique index <c>IX_TeacherSubjects_TenantId_TeacherProfileId_SubjectId</c>
/// has no <c>IsDeleted</c> filter, so a soft-deleted link would still occupy its slot and
/// re-assigning the same subject would fail.
/// </remarks>
public class TeacherSubject : TenantOwnedEntity
{
    public Guid TeacherProfileId { get; set; }

    public Guid SubjectId { get; set; }

    public TeacherProfile? TeacherProfile { get; set; }

    public Subject? Subject { get; set; }
}

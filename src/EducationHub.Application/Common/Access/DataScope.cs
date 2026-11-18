using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Shared.Common.Constants;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Common.Access;

/// <summary>
/// What the signed-in caller is allowed to see of a tenant's teaching data (groups, students,
/// sessions, homework, exams).
/// </summary>
/// <remarks>
/// <para>
/// Endpoint permissions answer "may this caller open the screen at all"; this answers "which
/// rows". A permission like <c>Groups.View</c> held by a teacher must not reveal a colleague's
/// groups, and the same permission held by a parent must not reveal other families' children.
/// </para>
/// <list type="bullet">
/// <item><b>Unrestricted</b> - management roles (owner, admin, accountant) and any account
/// whose roles are neither teaching nor family roles: the endpoint permission decides.</item>
/// <item><b>Teacher</b> - the groups the teacher is assigned to, the students ever enrolled in
/// them, and sessions the teacher took (a substitute still sees the session they taught).</item>
/// <item><b>Family</b> - a student sees themself, a parent sees their linked children; both see
/// the groups those students are (or were) enrolled in, and only those students' own marks,
/// submissions and results. Family access is read-only.</item>
/// </list>
/// </remarks>
public sealed class DataScope
{
    private DataScope(
        DataScopeKind kind,
        IReadOnlySet<Guid> groupIds,
        IReadOnlySet<Guid> studentIds,
        IReadOnlySet<Guid> teacherProfileIds,
        IReadOnlySet<Guid> manageableGroupIds)
    {
        Kind = kind;
        GroupIds = groupIds;
        StudentIds = studentIds;
        TeacherProfileIds = teacherProfileIds;
        ManageableGroupIds = manageableGroupIds;
    }

    public static DataScope Unrestricted { get; } =
        new(DataScopeKind.Unrestricted, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    public DataScopeKind Kind { get; }

    public bool IsUnrestricted => Kind == DataScopeKind.Unrestricted;

    /// <summary>Student or parent: sees only their own (children's) rows, never edits.</summary>
    public bool IsFamily => Kind == DataScopeKind.Family;

    public bool IsTeacher => Kind == DataScopeKind.Teacher;

    /// <summary>Visible groups. Ignored when <see cref="IsUnrestricted"/>.</summary>
    public IReadOnlySet<Guid> GroupIds { get; }

    /// <summary>Visible students. Ignored when <see cref="IsUnrestricted"/>.</summary>
    public IReadOnlySet<Guid> StudentIds { get; }

    /// <summary>The caller's own teacher profile(s); empty for non-teachers.</summary>
    public IReadOnlySet<Guid> TeacherProfileIds { get; }

    /// <summary>Groups the caller teaches (and may therefore change). Empty for family accounts.</summary>
    public IReadOnlySet<Guid> ManageableGroupIds { get; }

    public bool CanSeeGroup(Guid groupId) => IsUnrestricted || GroupIds.Contains(groupId);

    public bool CanSeeStudent(Guid studentId) => IsUnrestricted || StudentIds.Contains(studentId);

    /// <summary>A session is visible through its group, or because the caller taught it.</summary>
    public bool CanSeeSession(ClassSession session)
        => IsUnrestricted
            || GroupIds.Contains(session.StudyGroupId)
            || (session.TeacherProfileId is { } t && TeacherProfileIds.Contains(t));

    /// <summary>Teachers may change data of their own groups; family accounts never may.</summary>
    public bool CanManageGroup(Guid groupId) => IsUnrestricted || ManageableGroupIds.Contains(groupId);

    /// <summary>Same rule as <see cref="CanManageGroup"/>, widened to sessions the teacher took.</summary>
    public bool CanManageSession(ClassSession session)
        => IsUnrestricted
            || ManageableGroupIds.Contains(session.StudyGroupId)
            || (session.TeacherProfileId is { } t && TeacherProfileIds.Contains(t));

    internal static DataScope Create(
        DataScopeKind kind,
        IEnumerable<Guid> groupIds,
        IEnumerable<Guid> studentIds,
        IEnumerable<Guid> teacherProfileIds,
        IEnumerable<Guid> manageableGroupIds)
        => new(kind, groupIds.ToHashSet(), studentIds.ToHashSet(), teacherProfileIds.ToHashSet(), manageableGroupIds.ToHashSet());
}

public enum DataScopeKind
{
    Unrestricted = 0,
    Teacher = 1,
    Family = 2
}

/// <summary>
/// Resolves <see cref="DataScope"/> for the current request (once; the service is scoped).
/// </summary>
public sealed class DataScopeService(
    ICurrentUser currentUser,
    IRepository<TeacherProfile> teachers,
    IRepository<StudentProfile> students,
    IRepository<ParentProfile> parents,
    IRepository<StudentGuardian> guardians,
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments)
{
    /// <summary>Roles that see the whole tenant: they run the centre.</summary>
    private static readonly string[] FullAccessRoles =
    [
        RoleNames.PlatformAdmin,
        RoleNames.TenantOwner,
        RoleNames.TenantAdmin,
        RoleNames.Accountant
    ];

    private DataScope? _resolved;

    public async Task<DataScope> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        _resolved = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        return _resolved;
    }

    private async Task<DataScope> ResolveAsync(CancellationToken ct)
    {
        var roles = currentUser.Roles;

        bool Has(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);

        if (currentUser.IsPlatformAdmin || FullAccessRoles.Any(Has))
        {
            return DataScope.Unrestricted;
        }

        var isTeacher = Has(RoleNames.Teacher);
        var isFamily = Has(RoleNames.Student) || Has(RoleNames.Parent);

        // Assistants and custom roles keep the behaviour their permissions describe.
        if (!isTeacher && !isFamily)
        {
            return DataScope.Unrestricted;
        }

        // Fail closed: an authenticated teaching/family role without an account id sees nothing.
        if (currentUser.UserId is not { } userId)
        {
            return DataScope.Create(isTeacher ? DataScopeKind.Teacher : DataScopeKind.Family, [], [], [], []);
        }

        var groupIds = new HashSet<Guid>();
        var studentIds = new HashSet<Guid>();
        var teacherProfileIds = new HashSet<Guid>();
        var manageableGroupIds = new HashSet<Guid>();

        if (isTeacher)
        {
            teacherProfileIds.UnionWith(await teachers.Query
                .Where(t => t.UserId == userId)
                .Select(static t => t.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false));

            if (teacherProfileIds.Count > 0)
            {
                var ownProfiles = teacherProfileIds.ToArray();

                var teacherGroups = await groups.Query
                    .Where(g => g.TeacherProfileId != null && ownProfiles.Contains(g.TeacherProfileId.Value))
                    .Select(static g => g.Id)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                groupIds.UnionWith(teacherGroups);
                manageableGroupIds.UnionWith(teacherGroups);
                studentIds.UnionWith(await StudentsOfGroupsAsync(teacherGroups, ct).ConfigureAwait(false));
            }
        }

        if (isFamily)
        {
            var family = await FamilyStudentIdsAsync(userId, ct).ConfigureAwait(false);
            studentIds.UnionWith(family);
            groupIds.UnionWith(await GroupsOfStudentsAsync(family, ct).ConfigureAwait(false));
        }

        // A teacher who is also a parent sees their children's groups but manages only their own.
        var kind = isTeacher ? DataScopeKind.Teacher : DataScopeKind.Family;

        return DataScope.Create(kind, groupIds, studentIds, teacherProfileIds, manageableGroupIds);
    }

    /// <summary>The caller's own student profile(s) plus the children linked to their parent profile(s).</summary>
    public async Task<IReadOnlyList<Guid>> FamilyStudentIdsAsync(Guid userId, CancellationToken ct)
    {
        var own = await students.Query
            .Where(s => s.UserId == userId)
            .Select(static s => s.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var children = await guardians.Query
            .Join(
                parents.Query.Where(p => p.UserId == userId),
                g => g.ParentProfileId,
                p => p.Id,
                (g, _) => g.StudentProfileId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. own.Concat(children).Distinct()];
    }

    private async Task<List<Guid>> StudentsOfGroupsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        if (groupIds.Count == 0)
        {
            return [];
        }

        var ids = groupIds.ToArray();

        return await enrollments.Query
            .Where(e => ids.Contains(e.StudyGroupId))
            .Select(static e => e.StudentProfileId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<List<Guid>> GroupsOfStudentsAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        var ids = studentIds.ToArray();

        return await enrollments.Query
            .Where(e => ids.Contains(e.StudentProfileId))
            .Select(static e => e.StudyGroupId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}

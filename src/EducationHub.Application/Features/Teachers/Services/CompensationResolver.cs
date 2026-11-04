using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Domain.Entities.Teachers;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Teachers.Services;

/// <summary>
/// The precedence ladder a compensation query climbs, in order. A rule whose dimension is
/// null acts as a wildcard for that dimension; a rule that specifies a dimension the query
/// did not supply never matches.
/// </summary>
public enum CompensationPrecedence
{
    /// <summary>Teacher + group (subject optional).</summary>
    GroupTeacher = 1,

    /// <summary>Teacher + subject.</summary>
    TeacherSubject = 2,

    /// <summary>Teacher only.</summary>
    Teacher = 3,

    /// <summary>Group only (subject optional).</summary>
    Group = 4,

    /// <summary>Subject only.</summary>
    Subject = 5,

    /// <summary>No dimensions: the tenant's default rate.</summary>
    TenantDefault = 6
}

/// <summary>
/// Resolves the compensation rule that applies to a teacher under an optional subject
/// (and, from Mission Phase 3 on, study group), following the explicit ladder
/// Group+Teacher &gt; Teacher+Subject &gt; Teacher &gt; Group &gt; Subject &gt; TenantDefault.
/// </summary>
/// <remarks>
/// <para>
/// Ranking is a pure static function so the ladder can be unit-tested without a database.
/// The scope-shape buckets total every possible combination: the two shapes the ladder does
/// not name explicitly (teacher+group+subject and group+subject) fall into the bucket of the
/// closest documented shape - group+teacher and group respectively - so the outcome is
/// always deterministic.
/// </para>
/// <para>
/// The filtered unique indexes on <c>CompensationRules</c> guarantee at most one rule per
/// bucket per tenant, so two rules can never tie at the same rank through the product; the
/// id tie-break exists for in-memory scenarios (tests) and is documented as the rule.
/// </para>
/// </remarks>
public sealed class CompensationResolver(IRepository<CompensationRule> rules)
{
    /// <summary>Rank of a rule's scope shape, 1 (most specific) … 6 (tenant default).</summary>
    public static int RankScope(Guid? teacherProfileId, Guid? subjectId, Guid? studyGroupId)
        => (teacherProfileId.HasValue, studyGroupId.HasValue, subjectId.HasValue) switch
        {
            (true, true, _) => (int)CompensationPrecedence.GroupTeacher,
            (true, false, true) => (int)CompensationPrecedence.TeacherSubject,
            (true, false, false) => (int)CompensationPrecedence.Teacher,
            (false, true, _) => (int)CompensationPrecedence.Group,
            (false, false, true) => (int)CompensationPrecedence.Subject,
            _ => (int)CompensationPrecedence.TenantDefault
        };

    /// <summary>
    /// Whether a rule applies to the query dimensions. A non-null rule dimension demands an
    /// equal query dimension; a null rule dimension matches anything.
    /// </summary>
    public static bool Matches(
        CompensationRule rule,
        Guid teacherProfileId,
        Guid? subjectId,
        Guid? studyGroupId)
        => (!rule.TeacherProfileId.HasValue || rule.TeacherProfileId == teacherProfileId)
            && (!rule.SubjectId.HasValue || (subjectId.HasValue && rule.SubjectId == subjectId))
            && (!rule.StudyGroupId.HasValue || (studyGroupId.HasValue && rule.StudyGroupId == studyGroupId));

    /// <summary>
    /// Returns the winning rule with its rank, or (null, null) when no active rule matches.
    /// </summary>
    public async Task<ResolvedCompensationDto> ResolveAsync(
        Guid teacherProfileId,
        Guid? subjectId,
        Guid? studyGroupId,
        CancellationToken cancellationToken = default)
    {
        // Tenant default and subject rules live alongside the teacher's own rules, so the
        // candidate set is the tenant's active rules (the global filter scopes them), not
        // just this teacher's. Volumes are small per tenant by design.
        var candidates = await rules.Query
            .Where(static r => r.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ResolveIn(candidates, teacherProfileId, subjectId, studyGroupId);
    }

    /// <summary>
    /// The ladder itself, over an already-materialised candidate set - pure, so unit tests
    /// can exercise every precedence step without a database. Ties (impossible through the
    /// product, thanks to the per-bucket unique indexes) break by lowest rule id.
    /// </summary>
    public static ResolvedCompensationDto ResolveIn(
        IEnumerable<CompensationRule> candidates,
        Guid teacherProfileId,
        Guid? subjectId,
        Guid? studyGroupId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var best = candidates
            .Where(r => r.IsActive && Matches(r, teacherProfileId, subjectId, studyGroupId))
            .Select(r => new { Rule = r, Rank = RankScope(r.TeacherProfileId, r.SubjectId, r.StudyGroupId) })
            .OrderBy(static x => x.Rank)
            .ThenBy(static x => x.Rule.Id)
            .FirstOrDefault();

        return best is null
            ? new ResolvedCompensationDto(null, null)
            : new ResolvedCompensationDto(CompensationRuleDto.FromEntity(best.Rule), best.Rank);
    }
}

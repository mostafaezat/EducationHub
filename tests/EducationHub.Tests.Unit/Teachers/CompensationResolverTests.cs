using EducationHub.Application.Features.Teachers.Services;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;

namespace EducationHub.Tests.Unit.Teachers;

/// <summary>
/// The compensation precedence ladder: Group+Teacher &gt; Teacher+Subject &gt; Teacher &gt;
/// Group &gt; Subject &gt; TenantDefault, plus the wildcard matching rules and the
/// deterministic tie-break.
/// </summary>
public sealed class CompensationResolverTests
{
    private static readonly Guid TeacherA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TeacherB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid SubjectMath = Guid.Parse("00000000-0000-0000-0000-0000000000e1");
    private static readonly Guid SubjectEng = Guid.Parse("00000000-0000-0000-0000-0000000000e2");
    private static readonly Guid GroupOne = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    [Theory]
    [InlineData(true, true, true, 1)]   // group + teacher (+ subject) - closest documented shape
    [InlineData(true, true, false, 1)]  // group + teacher
    [InlineData(true, false, true, 2)]  // teacher + subject
    [InlineData(true, false, false, 3)] // teacher only
    [InlineData(false, true, true, 4)]  // group + subject - falls to the group bucket
    [InlineData(false, true, false, 4)] // group only
    [InlineData(false, false, true, 5)] // subject only
    [InlineData(false, false, false, 6)] // tenant default
    public void RankScope_ranks_every_scope_shape(
        bool hasTeacher,
        bool hasGroup,
        bool hasSubject,
        int expectedRank)
    {
        var rank = CompensationResolver.RankScope(
            hasTeacher ? TeacherA : null,
            hasSubject ? SubjectMath : null,
            hasGroup ? GroupOne : null);

        Assert.Equal(expectedRank, rank);
    }

    [Fact]
    public void The_documented_ladder_is_strictly_ordered()
    {
        Assert.Equal(1, (int)CompensationPrecedence.GroupTeacher);
        Assert.Equal(2, (int)CompensationPrecedence.TeacherSubject);
        Assert.Equal(3, (int)CompensationPrecedence.Teacher);
        Assert.Equal(4, (int)CompensationPrecedence.Group);
        Assert.Equal(5, (int)CompensationPrecedence.Subject);
        Assert.Equal(6, (int)CompensationPrecedence.TenantDefault);
    }

    [Fact]
    public void Null_rule_dimensions_are_wildcards()
    {
        var tenantDefault = Rule(null, null, null, 10);

        Assert.True(CompensationResolver.Matches(tenantDefault, TeacherA, SubjectMath, null));
        Assert.True(CompensationResolver.Matches(tenantDefault, TeacherA, null, null));
        Assert.True(CompensationResolver.Matches(tenantDefault, TeacherB, SubjectEng, GroupOne));
    }

    [Fact]
    public void A_rule_scoped_to_a_subject_does_not_match_a_query_without_that_subject()
    {
        var subjectRule = Rule(null, SubjectMath, null, 10);

        Assert.False(CompensationResolver.Matches(subjectRule, TeacherA, null, null));
        Assert.False(CompensationResolver.Matches(subjectRule, TeacherA, SubjectEng, null));
        Assert.True(CompensationResolver.Matches(subjectRule, TeacherA, SubjectMath, null));
    }

    [Fact]
    public void A_teacher_rule_does_not_apply_to_another_teacher()
    {
        var teacherRule = Rule(TeacherA, null, null, 10);

        Assert.True(CompensationResolver.Matches(teacherRule, TeacherA, null, null));
        Assert.False(CompensationResolver.Matches(teacherRule, TeacherB, null, null));
    }

    [Fact]
    public void The_most_specific_matching_rule_wins()
    {
        var candidates = new[]
        {
            Rule(null, null, null, 10),            // 6 tenant default
            Rule(null, SubjectMath, null, 20),     // 5 subject
            Rule(TeacherA, null, null, 30),        // 3 teacher
            Rule(TeacherA, SubjectMath, null, 40)  // 2 teacher+subject
        };

        var resolved = CompensationResolver.ResolveIn(candidates, TeacherA, SubjectMath, null);

        Assert.Equal(40, resolved.Rule!.Rate);
        Assert.Equal(2, resolved.Rank);
    }

    [Fact]
    public void Ladder_levels_outrank_ones_below_them()
    {
        // Every documented level present at once: the top of the ladder must win.
        var candidates = new[]
        {
            Rule(null, null, null, 10),                  // 6
            Rule(null, SubjectMath, null, 20),           // 5
            Rule(null, null, GroupOne, 25),              // 4
            Rule(TeacherA, null, null, 30),              // 3
            Rule(TeacherA, SubjectMath, null, 40),       // 2
            Rule(TeacherA, null, GroupOne, 50)           // 1
        };

        var resolved = CompensationResolver.ResolveIn(candidates, TeacherA, SubjectMath, GroupOne);

        Assert.Equal(50, resolved.Rule!.Rate);
        Assert.Equal(1, resolved.Rank);
    }

    [Fact]
    public void Falls_through_to_the_tenant_default_when_nothing_scoped_matches()
    {
        var candidates = new[]
        {
            Rule(TeacherB, SubjectEng, null, 40),
            Rule(null, SubjectEng, null, 20),
            Rule(null, null, null, 10)
        };

        var resolved = CompensationResolver.ResolveIn(candidates, TeacherA, SubjectMath, null);

        Assert.Equal(10, resolved.Rule!.Rate);
        Assert.Equal(6, resolved.Rank);
    }

    [Fact]
    public void Inactive_rules_never_win()
    {
        var inactive = Rule(TeacherA, null, null, 99);
        inactive.IsActive = false;

        var resolved = CompensationResolver.ResolveIn([inactive], TeacherA, null, null);

        Assert.Null(resolved.Rule);
        Assert.Null(resolved.Rank);
    }

    [Fact]
    public void No_match_returns_null_rule_and_null_rank()
    {
        var resolved = CompensationResolver.ResolveIn(
            [Rule(TeacherB, null, null, 30)],
            TeacherA,
            null,
            null);

        Assert.Null(resolved.Rule);
        Assert.Null(resolved.Rank);
    }

    [Fact]
    public void A_rank_tie_breaks_by_lowest_rule_id()
    {
        var lower = Rule(TeacherA, null, null, 30);
        lower.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var higher = Rule(TeacherA, null, null, 31);
        higher.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var resolved = CompensationResolver.ResolveIn([higher, lower], TeacherA, null, null);

        Assert.Equal(30, resolved.Rule!.Rate);
    }

    private static CompensationRule Rule(
        Guid? teacherProfileId,
        Guid? subjectId,
        Guid? studyGroupId,
        decimal rate)
        => new()
        {
            Id = Guid.NewGuid(),
            TeacherProfileId = teacherProfileId,
            SubjectId = subjectId,
            StudyGroupId = studyGroupId,
            Rate = rate,
            RateBasis = CompensationRateBasis.PerSession,
            IsActive = true
        };
}

using EducationHub.Application.Features.Finance;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Enums;

namespace EducationHub.Tests.Unit.Students;

/// <summary>Pure finance rules: fee precedence, FIFO allocation, charge status math, compensation pricing.</summary>
public sealed class FinanceEngineTests
{
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Grade = Guid.NewGuid();

    private static FeeRule Rule(decimal amount, Guid? group = null, Guid? grade = null, bool active = true, FeeFrequency frequency = FeeFrequency.Monthly)
        => new() { Amount = amount, StudyGroupId = group, GradeLevelId = grade, IsActive = active, Frequency = frequency, Name = amount.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    [Fact]
    public void Group_rule_beats_grade_rule_beats_tenant_default()
    {
        var rules = new[] { Rule(100), Rule(200, grade: Grade), Rule(300, group: Group) };

        Assert.Equal(300, FeeEngine.ResolveMonthlyRule(rules, Group, Grade)!.Amount);
        Assert.Equal(200, FeeEngine.ResolveMonthlyRule(rules, Guid.NewGuid(), Grade)!.Amount);
        Assert.Equal(100, FeeEngine.ResolveMonthlyRule(rules, Guid.NewGuid(), null)!.Amount);
    }

    [Fact]
    public void Inactive_and_one_time_rules_are_ignored()
    {
        var rules = new[] { Rule(300, group: Group, active: false), Rule(250, group: Group, frequency: FeeFrequency.OneTime) };

        Assert.Null(FeeEngine.ResolveMonthlyRule(rules, Group, null));
    }

    [Theory]
    [InlineData("2026-11", true)]
    [InlineData("2026-1", false)]
    [InlineData("November", false)]
    [InlineData(null, false)]
    public void Period_must_be_year_dash_month(string? period, bool valid)
        => Assert.Equal(valid, FeeEngine.TryParsePeriod(period, out _));

    [Fact]
    public void Fifo_settles_oldest_due_first_and_leaves_credit()
    {
        var older = new Charge { Amount = 400, DueDate = new DateOnly(2026, 9, 5) };
        var newer = new Charge { Amount = 400, DueDate = new DateOnly(2026, 10, 5) };

        var plan = ChargeMath.PlanFifo([newer, older], 500);

        Assert.Equal(2, plan.Count);
        Assert.Same(older, plan[0].Charge);
        Assert.Equal(400, plan[0].Amount);
        Assert.Equal(100, plan[1].Amount);
    }

    [Fact]
    public void Charge_status_follows_amount_paid()
    {
        var charge = new Charge { Amount = 300 };

        ChargeMath.Apply(charge, 100);
        Assert.Equal(ChargeStatus.PartiallyPaid, charge.Status);

        ChargeMath.Apply(charge, 200);
        Assert.Equal(ChargeStatus.Paid, charge.Status);
        Assert.Equal(0, ChargeMath.Outstanding(charge));

        ChargeMath.Reverse(charge, 300);
        Assert.Equal(ChargeStatus.Open, charge.Status);
    }

    [Theory]
    [InlineData(CompensationRateBasis.PerSession, 120, 120)]
    [InlineData(CompensationRateBasis.PerHour, 100, 150)]
    public void Compensation_price_uses_rate_basis(CompensationRateBasis basis, int rate, int expected)
        => Assert.Equal((decimal)expected, CompensationEngine.Price(rate, basis, new TimeOnly(16, 0), new TimeOnly(17, 30)));
}

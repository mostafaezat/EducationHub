using System.Globalization;
using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Teachers.Services;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Tenancy;
using EducationHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Finance;

/// <summary>
/// Posts money movements to cash accounts. Every posting carries its source, and the unique
/// index on (SourceType, SourceId) makes a second posting of the same payment, expense or
/// teacher payment impossible - this service also checks first so callers get a clean answer.
/// </summary>
public sealed class CashPostingService(IRepository<CashAccount> accounts, IRepository<CashTransaction> transactions)
{
    public const string DefaultAccountName = "Main cash box";

    /// <summary>
    /// The account to post to: the given one (must exist in this tenant), else the default,
    /// else the first active account, else a newly created "Main cash box".
    /// </summary>
    /// <summary>True when an explicit account id does not exist in the caller's tenant (handlers answer 404).</summary>
    public async Task<bool> IsUnknownAccountAsync(Guid? accountId, CancellationToken cancellationToken)
        => accountId is { } id && !await accounts.ExistsAsync(id, cancellationToken).ConfigureAwait(false);

    public async Task<Guid> ResolveAccountAsync(Guid tenantId, Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId is { } id)
        {
            return await accounts.ExistsAsync(id, cancellationToken).ConfigureAwait(false)
                ? id
                : throw new BusinessRuleException("CashAccounts.NotFound", "Cash account not found.");
        }

        var existing = await accounts.Query
            .Where(static a => a.IsActive)
            .OrderByDescending(static a => a.IsDefault)
            .ThenBy(static a => a.CreatedAt)
            .Select(static a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is { } found)
        {
            return found;
        }

        var created = new CashAccount
        {
            TenantId = tenantId,
            Name = DefaultAccountName,
            Type = CashAccountType.Cash,
            IsActive = true,
            IsDefault = true
        };

        await accounts.AddAsync(created, cancellationToken).ConfigureAwait(false);

        return created.Id;
    }

    /// <summary>Stages a posting; returns false (and stages nothing) when the source is already posted.</summary>
    public async Task<bool> PostAsync(
        Guid tenantId,
        Guid accountId,
        CashDirection direction,
        decimal amount,
        DateOnly occurredOn,
        CashSourceType sourceType,
        Guid? sourceId,
        string? description,
        CancellationToken cancellationToken)
    {
        if (sourceId is { } source
            && await transactions.Query
                .AnyAsync(t => t.SourceType == sourceType && t.SourceId == source, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        await transactions.AddAsync(
                new CashTransaction
                {
                    TenantId = tenantId,
                    CashAccountId = accountId,
                    Direction = direction,
                    Amount = amount,
                    OccurredOn = occurredOn,
                    SourceType = sourceType,
                    SourceId = sourceId,
                    Description = description
                },
                cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>Soft-removes the posting of a source (used when an expense is deleted).</summary>
    public async Task RemovePostingAsync(CashSourceType sourceType, Guid sourceId, CancellationToken cancellationToken)
    {
        var rows = await transactions.Query
            .Where(t => t.SourceType == sourceType && t.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            transactions.SoftRemove(row);
        }
    }

    /// <summary>Balance per account: opening + ins - outs over all live postings.</summary>
    public async Task<IReadOnlyList<CashAccountDto>> BalancesAsync(CancellationToken cancellationToken)
    {
        var list = await accounts.Query.OrderBy(static a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

        var sums = await transactions.Query
            .GroupBy(static t => t.CashAccountId)
            .Select(static g => new
            {
                g.Key,
                In = g.Where(static t => t.Direction == CashDirection.In).Sum(static t => (decimal?)t.Amount) ?? 0m,
                Out = g.Where(static t => t.Direction == CashDirection.Out).Sum(static t => (decimal?)t.Amount) ?? 0m
            })
            .ToDictionaryAsync(static x => x.Key, cancellationToken)
            .ConfigureAwait(false);

        return list
            .Select(a =>
            {
                var s = sums.GetValueOrDefault(a.Id);
                var balance = a.OpeningBalance + (s?.In ?? 0m) - (s?.Out ?? 0m);
                return new CashAccountDto(a.Id, a.Name, a.Type, a.OpeningBalance, balance, a.IsActive, a.IsDefault);
            })
            .ToList();
    }
}

/// <summary>
/// Monthly fee generation. Precedence for a group's monthly price: a rule scoped to the group,
/// else one scoped to the group's grade, else the tenant default (no group, no grade).
/// </summary>
public sealed class FeeEngine(
    IRepository<FeeRule> rules,
    IRepository<StudyGroup> groups,
    IRepository<Enrollment> enrollments,
    IRepository<Charge> charges,
    IRepository<TenantSettings> settings)
{
    /// <summary>Pure precedence ladder over a candidate set (exposed for unit tests).</summary>
    public static FeeRule? ResolveMonthlyRule(IEnumerable<FeeRule> candidates, Guid groupId, Guid? gradeLevelId)
    {
        var active = candidates
            .Where(static r => r.IsActive && r.Frequency == FeeFrequency.Monthly)
            .OrderBy(static r => r.CreatedAt)
            .ThenBy(static r => r.Id)
            .ToList();

        return active.FirstOrDefault(r => r.StudyGroupId == groupId)
            ?? (gradeLevelId.HasValue
                ? active.FirstOrDefault(r => r.StudyGroupId == null && r.GradeLevelId == gradeLevelId)
                : null)
            ?? active.FirstOrDefault(static r => r.StudyGroupId == null && r.GradeLevelId == null);
    }

    public static bool TryParsePeriod(string? period, out DateOnly start)
    {
        start = default;

        if (string.IsNullOrWhiteSpace(period)
            || !DateOnly.TryParseExact(period.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        start = parsed;
        return true;
    }

    public async Task<GenerateChargesResultDto> GenerateMonthlyAsync(
        Guid tenantId,
        DateOnly periodStart,
        Guid? studyGroupId,
        CancellationToken cancellationToken)
    {
        var periodKey = periodStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var dueDay = await settings.Query
            .Select(static s => (int?)s.FeeDueDayOfMonth)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false) ?? 5;

        var dueDate = new DateOnly(periodStart.Year, periodStart.Month, Math.Clamp(dueDay, 1, DateTime.DaysInMonth(periodStart.Year, periodStart.Month)));

        var groupQuery = groups.Query.Where(static g => g.IsActive);

        if (studyGroupId.HasValue)
        {
            groupQuery = groupQuery.Where(g => g.Id == studyGroupId.Value);
        }

        var groupRows = await groupQuery
            .Select(static g => new { g.Id, g.Name, g.GradeLevelId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groupIds = groupRows.Select(static g => g.Id).ToArray();

        var ruleRows = await rules.Query.ToListAsync(cancellationToken).ConfigureAwait(false);

        var active = await enrollments.Query
            .Where(e => groupIds.Contains(e.StudyGroupId) && e.Status == EnrollmentStatus.Active && e.EnrolledOn <= periodEnd)
            .Select(static e => new { e.StudyGroupId, e.StudentProfileId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Deleted charges count too: a charge removed on purpose is not regenerated.
        var existing = (await charges.Query
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.PeriodKey == periodKey && c.StudyGroupId != null && groupIds.Contains(c.StudyGroupId.Value))
                .Select(static c => new { c.StudentProfileId, c.StudyGroupId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .Select(static c => (c.StudentProfileId, c.StudyGroupId!.Value))
            .ToHashSet();

        int created = 0, skipped = 0, withoutRule = 0;

        foreach (var group in groupRows)
        {
            var rule = ResolveMonthlyRule(ruleRows, group.Id, group.GradeLevelId);

            foreach (var enrollment in active.Where(e => e.StudyGroupId == group.Id))
            {
                if (existing.Contains((enrollment.StudentProfileId, group.Id)))
                {
                    skipped++;
                    continue;
                }

                if (rule is null)
                {
                    withoutRule++;
                    continue;
                }

                await charges.AddAsync(
                        new Charge
                        {
                            TenantId = tenantId,
                            StudentProfileId = enrollment.StudentProfileId,
                            FeeRuleId = rule.Id,
                            StudyGroupId = group.Id,
                            Description = $"{rule.Name} - {group.Name} - {periodKey}",
                            Amount = rule.Amount,
                            DueDate = dueDate,
                            PeriodKey = periodKey,
                            Status = ChargeStatus.Open
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                created++;
            }
        }

        return new GenerateChargesResultDto(created, skipped, withoutRule);
    }
}

/// <summary>Applies and reverses payment allocations against charges.</summary>
public static class ChargeMath
{
    public static void Apply(Charge charge, decimal amount)
    {
        charge.AmountPaid += amount;
        charge.Status = StatusFor(charge);
    }

    public static void Reverse(Charge charge, decimal amount)
    {
        charge.AmountPaid = Math.Max(0m, charge.AmountPaid - amount);
        charge.Status = StatusFor(charge);
    }

    public static decimal Outstanding(Charge charge)
        => charge.Status == ChargeStatus.Cancelled ? 0m : Math.Max(0m, charge.Amount - charge.AmountPaid);

    private static ChargeStatus StatusFor(Charge charge)
        => charge.Status == ChargeStatus.Cancelled
            ? ChargeStatus.Cancelled
            : charge.AmountPaid <= 0m
                ? ChargeStatus.Open
                : charge.AmountPaid >= charge.Amount ? ChargeStatus.Paid : ChargeStatus.PartiallyPaid;

    /// <summary>
    /// FIFO plan: settle charges oldest-due first until the amount runs out. Returns the
    /// (charge, amount) pairs; whatever is left is credit.
    /// </summary>
    public static IReadOnlyList<(Charge Charge, decimal Amount)> PlanFifo(IEnumerable<Charge> openCharges, decimal amount)
    {
        var plan = new List<(Charge, decimal)>();
        var remaining = amount;

        foreach (var charge in openCharges.OrderBy(static c => c.DueDate).ThenBy(static c => c.CreatedAt))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(remaining, Outstanding(charge));

            if (take <= 0m)
            {
                continue;
            }

            plan.Add((charge, take));
            remaining -= take;
        }

        return plan;
    }
}

/// <summary>
/// Prices completed sessions for their teachers using the compensation precedence ladder
/// (snapshot amounts, one row per session).
/// </summary>
public sealed class CompensationEngine(
    IRepository<ClassSession> sessions,
    IRepository<StudyGroup> groups,
    IRepository<SessionCompensation> compensations,
    CompensationResolver resolver)
{
    public static decimal Price(decimal rate, CompensationRateBasis basis, TimeOnly start, TimeOnly end)
        => basis == CompensationRateBasis.PerHour
            ? Math.Round(rate * (decimal)(end - start).TotalHours, 2, MidpointRounding.AwayFromZero)
            : rate;

    public async Task<ComputeCompensationsResultDto> ComputeAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? teacherProfileId,
        CancellationToken cancellationToken)
    {
        var query = sessions.Query.Where(s =>
            s.Status == SessionStatus.Completed && s.TeacherProfileId != null && s.Date >= from && s.Date <= to);

        if (teacherProfileId.HasValue)
        {
            query = query.Where(s => s.TeacherProfileId == teacherProfileId);
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = rows.Select(static s => s.Id).ToArray();

        var priced = (await compensations.Query
                .Where(c => ids.Contains(c.ClassSessionId))
                .Select(static c => c.ClassSessionId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet();

        var groupIds = rows.Select(static s => s.StudyGroupId).Distinct().ToArray();
        var subjectByGroup = await groups.Query
            .Where(g => groupIds.Contains(g.Id))
            .ToDictionaryAsync(static g => g.Id, static g => g.SubjectId, cancellationToken)
            .ConfigureAwait(false);

        int created = 0, already = 0, withoutRule = 0;

        foreach (var session in rows)
        {
            if (priced.Contains(session.Id))
            {
                already++;
                continue;
            }

            var teacherId = session.TeacherProfileId!.Value;
            var resolved = await resolver
                .ResolveAsync(teacherId, subjectByGroup.GetValueOrDefault(session.StudyGroupId), session.StudyGroupId, cancellationToken)
                .ConfigureAwait(false);

            if (resolved.Rule is null)
            {
                withoutRule++;
                continue;
            }

            await compensations.AddAsync(
                    new SessionCompensation
                    {
                        TenantId = tenantId,
                        ClassSessionId = session.Id,
                        TeacherProfileId = teacherId,
                        CompensationRuleId = resolved.Rule.Id,
                        Amount = Price(resolved.Rule.Rate, resolved.Rule.RateBasis, session.StartTime, session.EndTime)
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            created++;
        }

        return new ComputeCompensationsResultDto(created, already, withoutRule);
    }
}

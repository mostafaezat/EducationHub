using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Academic;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Finance;

/// <summary>Display-name lookups shared by the finance handlers.</summary>
public sealed class FinanceReadModel(
    IRepository<StudentProfile> students,
    IRepository<StudyGroup> groups,
    IRepository<GradeLevel> grades,
    IRepository<Charge> charges,
    IRepository<PaymentAllocation> allocations)
{
    public async Task<Dictionary<Guid, string>> StudentNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToArray();
        return list.Length == 0
            ? new Dictionary<Guid, string>()
            : await students.Query.Where(s => list.Contains(s.Id)).ToDictionaryAsync(static s => s.Id, static s => s.FullName, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<Guid, string>> GroupNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(static i => i.HasValue).Select(static i => i!.Value).Distinct().ToArray();
        return list.Length == 0
            ? new Dictionary<Guid, string>()
            : await groups.Query.Where(g => list.Contains(g.Id)).ToDictionaryAsync(static g => g.Id, static g => g.Name, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FeeRuleDto>> RulesAsync(IReadOnlyList<FeeRule> rows, CancellationToken ct)
    {
        var groupNames = await GroupNamesAsync(rows.Select(static r => r.StudyGroupId), ct).ConfigureAwait(false);
        var gradeIds = rows.Where(static r => r.GradeLevelId.HasValue).Select(static r => r.GradeLevelId!.Value).Distinct().ToArray();
        var gradeNames = gradeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await grades.Query.Where(g => gradeIds.Contains(g.Id)).ToDictionaryAsync(static g => g.Id, static g => g.Name, ct).ConfigureAwait(false);

        return rows.Select(r => new FeeRuleDto(
                r.Id, r.Name, r.Amount, r.Frequency,
                r.StudyGroupId, r.StudyGroupId.HasValue ? groupNames.GetValueOrDefault(r.StudyGroupId.Value) : null,
                r.GradeLevelId, r.GradeLevelId.HasValue ? gradeNames.GetValueOrDefault(r.GradeLevelId.Value) : null,
                r.IsActive, r.Notes))
            .ToList();
    }

    public async Task<IReadOnlyList<ChargeDto>> ChargesAsync(IReadOnlyList<Charge> rows, CancellationToken ct)
    {
        var names = await StudentNamesAsync(rows.Select(static r => r.StudentProfileId), ct).ConfigureAwait(false);
        var groupNames = await GroupNamesAsync(rows.Select(static r => r.StudyGroupId), ct).ConfigureAwait(false);

        return rows.Select(r => new ChargeDto(
                r.Id, r.StudentProfileId, names.GetValueOrDefault(r.StudentProfileId), r.FeeRuleId,
                r.StudyGroupId, r.StudyGroupId.HasValue ? groupNames.GetValueOrDefault(r.StudyGroupId.Value) : null,
                r.Description, r.Amount, r.AmountPaid, ChargeMath.Outstanding(r), r.DueDate, r.PeriodKey, r.Status, r.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentDto>> PaymentsAsync(IReadOnlyList<Payment> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(static r => r.Id).ToArray();
        var names = await StudentNamesAsync(rows.Select(static r => r.StudentProfileId), ct).ConfigureAwait(false);

        var allocs = await allocations.Query
            .Where(a => ids.Contains(a.PaymentId))
            .Join(charges.Query.IgnoreQueryFilters(), a => a.ChargeId, c => c.Id, (a, c) => new { a.PaymentId, a.ChargeId, c.Description, a.Amount })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byPayment = allocs
            .GroupBy(static a => a.PaymentId)
            .ToDictionary(static g => g.Key, static g => (IReadOnlyList<AllocationDto>)g.Select(static a => new AllocationDto(a.ChargeId, a.Description, a.Amount)).ToList());

        return rows.Select(r =>
            {
                var list = byPayment.GetValueOrDefault(r.Id, []);
                var allocated = list.Sum(static a => a.Amount);
                return new PaymentDto(
                    r.Id, r.StudentProfileId, names.GetValueOrDefault(r.StudentProfileId), r.CashAccountId, r.Amount, allocated,
                    r.IsVoided ? 0m : r.Amount - allocated, r.PaidOn, r.Method, r.Reference, r.Notes, r.IsVoided, r.VoidReason,
                    list, r.CreatedAt);
            })
            .ToList();
    }
}

// ---- Fee rules ---------------------------------------------------------------

public sealed record GetFeeRulesQuery(bool ActiveOnly) : IRequest<Result<IReadOnlyList<FeeRuleDto>>>;

public sealed class GetFeeRulesQueryHandler(IRepository<FeeRule> rules, FinanceReadModel readModel)
    : IRequestHandler<GetFeeRulesQuery, Result<IReadOnlyList<FeeRuleDto>>>
{
    public async Task<Result<IReadOnlyList<FeeRuleDto>>> Handle(GetFeeRulesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = rules.Query;

        if (request.ActiveOnly)
        {
            query = query.Where(static r => r.IsActive);
        }

        var rows = await query.OrderBy(static r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<FeeRuleDto>>.Success(await readModel.RulesAsync(rows, cancellationToken).ConfigureAwait(false));
    }
}

public sealed record CreateFeeRuleCommand(CreateFeeRuleRequest Request) : IRequest<Result<FeeRuleDto>>;

public sealed class CreateFeeRuleCommandHandler(
    IRepository<FeeRule> rules,
    IRepository<StudyGroup> groups,
    IRepository<GradeLevel> grades,
    FinanceReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateFeeRuleCommand, Result<FeeRuleDto>>
{
    public async Task<Result<FeeRuleDto>> Handle(CreateFeeRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;

        if (r.StudyGroupId is { } g && !await groups.ExistsAsync(g, cancellationToken).ConfigureAwait(false))
        {
            return Result<FeeRuleDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        if (r.GradeLevelId is { } gl && !await grades.ExistsAsync(gl, cancellationToken).ConfigureAwait(false))
        {
            return Result<FeeRuleDto>.Failure("Grades.NotFound", "Grade level not found.");
        }

        var rule = new FeeRule
        {
            TenantId = tenantId,
            Name = Normalize.Required(r.Name, string.Empty),
            Amount = r.Amount,
            Frequency = r.Frequency,
            StudyGroupId = r.StudyGroupId,
            GradeLevelId = r.StudyGroupId.HasValue ? null : r.GradeLevelId,
            IsActive = true,
            Notes = Normalize.Optional(r.Notes)
        };

        await rules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<FeeRuleDto>.Success((await readModel.RulesAsync([rule], cancellationToken).ConfigureAwait(false))[0], "Fee rule created successfully.");
    }
}

/// <summary>Price changes never touch charges already issued (they hold a snapshot).</summary>
public sealed record UpdateFeeRuleCommand(Guid Id, UpdateFeeRuleRequest Request) : IRequest<Result<FeeRuleDto>>;

public sealed class UpdateFeeRuleCommandHandler(IRepository<FeeRule> rules, FinanceReadModel readModel, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateFeeRuleCommand, Result<FeeRuleDto>>
{
    public async Task<Result<FeeRuleDto>> Handle(UpdateFeeRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rule = await rules.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return Result<FeeRuleDto>.Failure("FeeRules.NotFound", "Fee rule not found.");
        }

        var r = command.Request;
        rule.Name = r.Name is null ? rule.Name : Normalize.Required(r.Name, rule.Name);
        rule.Amount = r.Amount ?? rule.Amount;
        rule.IsActive = r.IsActive ?? rule.IsActive;
        rule.Notes = r.Notes is null ? rule.Notes : Normalize.Optional(r.Notes);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<FeeRuleDto>.Success((await readModel.RulesAsync([rule], cancellationToken).ConfigureAwait(false))[0], "Fee rule updated successfully.");
    }
}

public sealed record DeleteFeeRuleCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteFeeRuleCommandHandler(IRepository<FeeRule> rules, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteFeeRuleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteFeeRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rule = await rules.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (rule is null)
        {
            return Result<bool>.Failure("FeeRules.NotFound", "Fee rule not found.");
        }

        rules.SoftRemove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(true, "Fee rule deleted successfully.");
    }
}

// ---- Charges -----------------------------------------------------------------

public sealed record GetChargesQuery(PaginationRequest Pagination, Guid? StudentProfileId, ChargeStatus? Status, string? PeriodKey, bool OutstandingOnly)
    : IRequest<Result<PaginatedList<ChargeDto>>>;

public sealed class GetChargesQueryHandler(IRepository<Charge> charges, FinanceReadModel readModel)
    : IRequestHandler<GetChargesQuery, Result<PaginatedList<ChargeDto>>>
{
    public async Task<Result<PaginatedList<ChargeDto>>> Handle(GetChargesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = charges.Query;

        if (request.StudentProfileId.HasValue)
        {
            query = query.Where(c => c.StudentProfileId == request.StudentProfileId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(c => c.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.PeriodKey))
        {
            query = query.Where(c => c.PeriodKey == request.PeriodKey);
        }

        if (request.OutstandingOnly)
        {
            query = query.Where(static c => c.Status == ChargeStatus.Open || c.Status == ChargeStatus.PartiallyPaid);
        }

        var page = await query
            .OrderByDescending(static c => c.DueDate)
            .ThenBy(static c => c.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.ChargesAsync(page.Items, cancellationToken).ConfigureAwait(false);
        return Result<PaginatedList<ChargeDto>>.Success(new PaginatedList<ChargeDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record CreateChargeCommand(CreateChargeRequest Request) : IRequest<Result<ChargeDto>>;

public sealed class CreateChargeCommandHandler(
    IRepository<Charge> charges,
    IRepository<StudentProfile> students,
    IRepository<StudyGroup> groups,
    FinanceReadModel readModel,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateChargeCommand, Result<ChargeDto>>
{
    public async Task<Result<ChargeDto>> Handle(CreateChargeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;

        if (!await students.ExistsAsync(r.StudentProfileId, cancellationToken).ConfigureAwait(false))
        {
            return Result<ChargeDto>.Failure("Students.NotFound", "Student not found.");
        }

        if (r.StudyGroupId is { } g && !await groups.ExistsAsync(g, cancellationToken).ConfigureAwait(false))
        {
            return Result<ChargeDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var charge = new Charge
        {
            TenantId = tenantId,
            StudentProfileId = r.StudentProfileId,
            StudyGroupId = r.StudyGroupId,
            Description = Normalize.Required(r.Description, "Charge"),
            Amount = r.Amount,
            DueDate = r.DueDate,
            Status = ChargeStatus.Open
        };

        await charges.AddAsync(charge, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ChargeDto>.Success((await readModel.ChargesAsync([charge], cancellationToken).ConfigureAwait(false))[0], "Charge created successfully.");
    }
}

/// <summary>Cancels an unpaid charge. A charge with payments allocated must have them voided first.</summary>
public sealed record CancelChargeCommand(Guid Id) : IRequest<Result<ChargeDto>>;

public sealed class CancelChargeCommandHandler(IRepository<Charge> charges, FinanceReadModel readModel, IUnitOfWork unitOfWork)
    : IRequestHandler<CancelChargeCommand, Result<ChargeDto>>
{
    public async Task<Result<ChargeDto>> Handle(CancelChargeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var charge = await charges.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (charge is null)
        {
            return Result<ChargeDto>.Failure("Charges.NotFound", "Charge not found.");
        }

        if (charge.AmountPaid > 0m)
        {
            return Result<ChargeDto>.Failure("Charges.InUse", "Void the payments allocated to this charge before cancelling it.");
        }

        charge.Status = ChargeStatus.Cancelled;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ChargeDto>.Success((await readModel.ChargesAsync([charge], cancellationToken).ConfigureAwait(false))[0], "Charge cancelled successfully.");
    }
}

public sealed record GenerateChargesCommand(GenerateChargesRequest Request) : IRequest<Result<GenerateChargesResultDto>>;

public sealed class GenerateChargesCommandHandler(
    FeeEngine engine,
    IRepository<StudyGroup> groups,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<GenerateChargesCommand, Result<GenerateChargesResultDto>>
{
    public async Task<Result<GenerateChargesResultDto>> Handle(GenerateChargesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;

        if (!FeeEngine.TryParsePeriod(r.Period, out var start))
        {
            return Result<GenerateChargesResultDto>.Failure("Charges.InvalidPeriod", "Period must be yyyy-MM.");
        }

        if (r.StudyGroupId is { } g && !await groups.ExistsAsync(g, cancellationToken).ConfigureAwait(false))
        {
            return Result<GenerateChargesResultDto>.Failure("Groups.NotFound", "Study group not found.");
        }

        var result = await engine.GenerateMonthlyAsync(tenantId, start, r.StudyGroupId, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<GenerateChargesResultDto>.Success(result, $"{result.Created} charge(s) created.");
    }
}

public sealed record GetStudentBalanceQuery(Guid StudentProfileId) : IRequest<Result<StudentBalanceDto>>;

public sealed class GetStudentBalanceQueryHandler(
    IRepository<StudentProfile> students,
    IRepository<Charge> charges,
    IRepository<Payment> payments,
    IRepository<PaymentAllocation> allocations)
    : IRequestHandler<GetStudentBalanceQuery, Result<StudentBalanceDto>>
{
    public async Task<Result<StudentBalanceDto>> Handle(GetStudentBalanceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = request.StudentProfileId;

        if (!await students.ExistsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            return Result<StudentBalanceDto>.Failure("Students.NotFound", "Student not found.");
        }

        var live = charges.Query.Where(c => c.StudentProfileId == id && c.Status != ChargeStatus.Cancelled);
        var charged = await live.SumAsync(static c => (decimal?)c.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
        var outstanding = await live.SumAsync(static c => (decimal?)(c.Amount - c.AmountPaid), cancellationToken).ConfigureAwait(false) ?? 0m;
        var open = await live.CountAsync(static c => c.Amount > c.AmountPaid, cancellationToken).ConfigureAwait(false);

        var livePayments = payments.Query.Where(p => p.StudentProfileId == id && !p.IsVoided);
        var paid = await livePayments.SumAsync(static p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
        var allocated = await allocations.Query
            .Where(a => livePayments.Select(static p => p.Id).Contains(a.PaymentId))
            .SumAsync(static a => (decimal?)a.Amount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        return Result<StudentBalanceDto>.Success(new StudentBalanceDto(id, charged, paid, outstanding, paid - allocated, open));
    }
}

// ---- Payments ----------------------------------------------------------------

public sealed record GetPaymentsQuery(PaginationRequest Pagination, Guid? StudentProfileId, DateOnly? From, DateOnly? To, bool IncludeVoided)
    : IRequest<Result<PaginatedList<PaymentDto>>>;

public sealed class GetPaymentsQueryHandler(IRepository<Payment> payments, FinanceReadModel readModel)
    : IRequestHandler<GetPaymentsQuery, Result<PaginatedList<PaymentDto>>>
{
    public async Task<Result<PaginatedList<PaymentDto>>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = payments.Query;

        if (request.StudentProfileId.HasValue)
        {
            query = query.Where(p => p.StudentProfileId == request.StudentProfileId.Value);
        }

        if (request.From.HasValue)
        {
            query = query.Where(p => p.PaidOn >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(p => p.PaidOn <= request.To.Value);
        }

        if (!request.IncludeVoided)
        {
            query = query.Where(static p => !p.IsVoided);
        }

        var page = await query
            .OrderByDescending(static p => p.PaidOn)
            .ThenByDescending(static p => p.CreatedAt)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var items = await readModel.PaymentsAsync(page.Items, cancellationToken).ConfigureAwait(false);
        return Result<PaginatedList<PaymentDto>>.Success(new PaginatedList<PaymentDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

public sealed record GetPaymentQuery(Guid Id) : IRequest<Result<PaymentDto>>;

public sealed class GetPaymentQueryHandler(IRepository<Payment> payments, FinanceReadModel readModel)
    : IRequestHandler<GetPaymentQuery, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payment = await payments.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);

        return payment is null
            ? Result<PaymentDto>.Failure("Payments.NotFound", "Payment not found.")
            : Result<PaymentDto>.Success((await readModel.PaymentsAsync([payment], cancellationToken).ConfigureAwait(false))[0]);
    }
}

/// <summary>
/// Records a payment, allocates it (explicitly or FIFO) and posts it to a cash account -
/// all in one transaction.
/// </summary>
public sealed record CreatePaymentCommand(CreatePaymentRequest Request) : IRequest<Result<PaymentDto>>;

public sealed class CreatePaymentCommandHandler(
    IRepository<Payment> payments,
    IRepository<PaymentAllocation> allocations,
    IRepository<Charge> charges,
    IRepository<StudentProfile> students,
    CashPostingService cash,
    FinanceReadModel readModel,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreatePaymentCommand, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await unitOfWork.ExecuteInTransactionAsync(ct => CreateAsync(command.Request, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<PaymentDto>> CreateAsync(CreatePaymentRequest r, CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        if (!await students.ExistsAsync(r.StudentProfileId, cancellationToken).ConfigureAwait(false))
        {
            return Result<PaymentDto>.Failure("Students.NotFound", "Student not found.");
        }

        var open = await charges.Query
            .Where(c => c.StudentProfileId == r.StudentProfileId && (c.Status == ChargeStatus.Open || c.Status == ChargeStatus.PartiallyPaid))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<(Charge Charge, decimal Amount)> plan;

        if (r.Allocations is { Count: > 0 } requested)
        {
            var byId = open.ToDictionary(static c => c.Id);
            var explicitPlan = new List<(Charge, decimal)>();

            foreach (var a in requested)
            {
                if (!byId.TryGetValue(a.ChargeId, out var charge))
                {
                    return Result<PaymentDto>.Failure("Charges.NotFound", "A charge to allocate to is not an open charge of this student.");
                }

                if (a.Amount > ChargeMath.Outstanding(charge))
                {
                    return Result<PaymentDto>.Failure("Payments.OverAllocated", "An allocation exceeds the charge's outstanding amount.");
                }

                explicitPlan.Add((charge, a.Amount));
            }

            if (explicitPlan.Sum(static p => p.Item2) > r.Amount)
            {
                return Result<PaymentDto>.Failure("Payments.OverAllocated", "Allocations exceed the payment amount.");
            }

            plan = explicitPlan;
        }
        else
        {
            plan = ChargeMath.PlanFifo(open, r.Amount);
        }

        if (await cash.IsUnknownAccountAsync(r.CashAccountId, cancellationToken).ConfigureAwait(false))
        {
            return Result<PaymentDto>.Failure("CashAccounts.NotFound", "Cash account not found.");
        }

        var accountId = await cash.ResolveAccountAsync(tenantId, r.CashAccountId, cancellationToken).ConfigureAwait(false);
        var paidOn = r.PaidOn ?? clock.UtcToday;

        var payment = new Payment
        {
            TenantId = tenantId,
            StudentProfileId = r.StudentProfileId,
            CashAccountId = accountId,
            Amount = r.Amount,
            PaidOn = paidOn,
            Method = r.Method,
            Reference = Normalize.Optional(r.Reference),
            Notes = Normalize.Optional(r.Notes)
        };

        await payments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        foreach (var (charge, amount) in plan)
        {
            ChargeMath.Apply(charge, amount);
            await allocations.AddAsync(
                    new PaymentAllocation { TenantId = tenantId, PaymentId = payment.Id, ChargeId = charge.Id, Amount = amount },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await cash.PostAsync(tenantId, accountId, CashDirection.In, r.Amount, paidOn, CashSourceType.Payment, payment.Id,
            $"Payment {payment.Reference ?? payment.Id.ToString()[..8]}", cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PaymentDto>.Success((await readModel.PaymentsAsync([payment], cancellationToken).ConfigureAwait(false))[0], "Payment recorded successfully.");
    }
}

/// <summary>Voids a payment: reverses its allocations and posts the reversal to cash.</summary>
public sealed record VoidPaymentCommand(Guid Id, VoidPaymentRequest Request) : IRequest<Result<PaymentDto>>;

public sealed class VoidPaymentCommandHandler(
    IRepository<Payment> payments,
    IRepository<PaymentAllocation> allocations,
    IRepository<Charge> charges,
    CashPostingService cash,
    FinanceReadModel readModel,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<VoidPaymentCommand, Result<PaymentDto>>
{
    public async Task<Result<PaymentDto>> Handle(VoidPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var payment = await payments.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            return Result<PaymentDto>.Failure("Payments.NotFound", "Payment not found.");
        }

        if (payment.IsVoided)
        {
            return Result<PaymentDto>.Failure("Payments.VoidConflict", "The payment is already voided.");
        }

        var rows = await allocations.Query.Where(a => a.PaymentId == payment.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var chargeIds = rows.Select(static a => a.ChargeId).ToArray();
        var chargeRows = await charges.Query.Where(c => chargeIds.Contains(c.Id)).ToDictionaryAsync(static c => c.Id, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            if (chargeRows.TryGetValue(row.ChargeId, out var charge))
            {
                ChargeMath.Reverse(charge, row.Amount);
            }

            allocations.Remove(row);
        }

        payment.IsVoided = true;
        payment.VoidedAt = clock.UtcNow;
        payment.VoidReason = Normalize.Optional(command.Request?.Reason);

        if (payment.CashAccountId is { } accountId)
        {
            await cash.PostAsync(tenantId, accountId, CashDirection.Out, payment.Amount, clock.UtcToday, CashSourceType.PaymentVoid,
                payment.Id, "Payment voided", cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<PaymentDto>.Success((await readModel.PaymentsAsync([payment], cancellationToken).ConfigureAwait(false))[0], "Payment voided successfully.");
    }
}

// ---- Validators --------------------------------------------------------------

public sealed class CreateFeeRuleCommandValidator : LocalizedValidator<CreateFeeRuleCommand>
{
    public CreateFeeRuleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThanOrEqualTo(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Frequency).IsInEnum().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(500).When(static x => x.Request is not null);
    }
}

public sealed class UpdateFeeRuleCommandValidator : LocalizedValidator<UpdateFeeRuleCommand>
{
    public UpdateFeeRuleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).MaximumLength(200).When(static x => x.Request?.Name is not null);
        RuleFor(x => x.Request.Amount).GreaterThanOrEqualTo(0m).When(static x => x.Request?.Amount is not null);
    }
}

public sealed class CreateChargeCommandValidator : LocalizedValidator<CreateChargeCommand>
{
    public CreateChargeCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudentProfileId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThan(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Description).MaximumLength(300).When(static x => x.Request is not null);
    }
}

public sealed class CreatePaymentCommandValidator : LocalizedValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.StudentProfileId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThan(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Method).IsInEnum().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Reference).MaximumLength(100).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(500).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Allocations)
            .Must(static list => list is null || list.Select(static a => a.ChargeId).Distinct().Count() == list.Count)
            .WithMessage("A charge can appear only once in the allocations.")
            .When(static x => x.Request is not null);
        RuleForEach(x => x.Request.Allocations).ChildRules(static a =>
        {
            a.RuleFor(i => i.ChargeId).NotEmpty();
            a.RuleFor(i => i.Amount).GreaterThan(0m);
        }).When(static x => x.Request?.Allocations is not null);
    }
}

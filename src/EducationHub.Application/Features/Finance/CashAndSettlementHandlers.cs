using EducationHub.Application.Common.Extensions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Scheduling;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Domain.Entities.Finance;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Finance;

// ---- Cash accounts & transactions ---------------------------------------------

public sealed record GetCashAccountsQuery : IRequest<Result<IReadOnlyList<CashAccountDto>>>;

public sealed class GetCashAccountsQueryHandler(CashPostingService cash)
    : IRequestHandler<GetCashAccountsQuery, Result<IReadOnlyList<CashAccountDto>>>
{
    public async Task<Result<IReadOnlyList<CashAccountDto>>> Handle(GetCashAccountsQuery request, CancellationToken cancellationToken)
        => Result<IReadOnlyList<CashAccountDto>>.Success(await cash.BalancesAsync(cancellationToken).ConfigureAwait(false));
}

internal static class CashAccountSupport
{
    /// <summary>Keeps exactly one default account when <paramref name="account"/> becomes the default.</summary>
    public static async Task MakeDefaultAsync(IRepository<CashAccount> accounts, CashAccount account, CancellationToken ct)
    {
        var others = await accounts.Query.Where(a => a.IsDefault && a.Id != account.Id).ToListAsync(ct).ConfigureAwait(false);

        foreach (var other in others)
        {
            other.IsDefault = false;
        }

        account.IsDefault = true;
    }
}

public sealed record CreateCashAccountCommand(CreateCashAccountRequest Request) : IRequest<Result<CashAccountDto>>;

public sealed class CreateCashAccountCommandHandler(
    IRepository<CashAccount> accounts,
    CashPostingService cash,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateCashAccountCommand, Result<CashAccountDto>>
{
    public async Task<Result<CashAccountDto>> Handle(CreateCashAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var name = Normalize.Required(r.Name, string.Empty);

        if (await accounts.Query.AnyAsync(a => a.Name == name, cancellationToken).ConfigureAwait(false))
        {
            return Result<CashAccountDto>.Failure("CashAccounts.NameTaken", $"A cash account named '{name}' already exists.");
        }

        var account = new CashAccount { TenantId = tenantId, Name = name, Type = r.Type, OpeningBalance = r.OpeningBalance, IsActive = true };

        if (r.IsDefault || !await accounts.Query.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await CashAccountSupport.MakeDefaultAsync(accounts, account, cancellationToken).ConfigureAwait(false);
        }

        await accounts.AddAsync(account, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = (await cash.BalancesAsync(cancellationToken).ConfigureAwait(false)).First(a => a.Id == account.Id);
        return Result<CashAccountDto>.Success(dto, "Cash account created successfully.");
    }
}

public sealed record UpdateCashAccountCommand(Guid Id, UpdateCashAccountRequest Request) : IRequest<Result<CashAccountDto>>;

public sealed class UpdateCashAccountCommandHandler(IRepository<CashAccount> accounts, CashPostingService cash, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCashAccountCommand, Result<CashAccountDto>>
{
    public async Task<Result<CashAccountDto>> Handle(UpdateCashAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var account = await accounts.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (account is null)
        {
            return Result<CashAccountDto>.Failure("CashAccounts.NotFound", "Cash account not found.");
        }

        var r = command.Request;

        if (r.Name is not null)
        {
            var name = Normalize.Required(r.Name, account.Name);

            if (await accounts.Query.AnyAsync(a => a.Name == name && a.Id != account.Id, cancellationToken).ConfigureAwait(false))
            {
                return Result<CashAccountDto>.Failure("CashAccounts.NameTaken", $"A cash account named '{name}' already exists.");
            }

            account.Name = name;
        }

        account.IsActive = r.IsActive ?? account.IsActive;

        if (r.IsDefault == true)
        {
            await CashAccountSupport.MakeDefaultAsync(accounts, account, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = (await cash.BalancesAsync(cancellationToken).ConfigureAwait(false)).First(a => a.Id == account.Id);
        return Result<CashAccountDto>.Success(dto, "Cash account updated successfully.");
    }
}

public sealed record GetCashTransactionsQuery(PaginationRequest Pagination, Guid? CashAccountId, DateOnly? From, DateOnly? To)
    : IRequest<Result<PaginatedList<CashTransactionDto>>>;

public sealed class GetCashTransactionsQueryHandler(IRepository<CashTransaction> transactions, IRepository<CashAccount> accounts)
    : IRequestHandler<GetCashTransactionsQuery, Result<PaginatedList<CashTransactionDto>>>
{
    public async Task<Result<PaginatedList<CashTransactionDto>>> Handle(GetCashTransactionsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = transactions.Query;

        if (request.CashAccountId.HasValue)
        {
            query = query.Where(t => t.CashAccountId == request.CashAccountId.Value);
        }

        if (request.From.HasValue)
        {
            query = query.Where(t => t.OccurredOn >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(t => t.OccurredOn <= request.To.Value);
        }

        var page = await query
            .OrderByDescending(static t => t.OccurredOn)
            .ThenByDescending(static t => t.CreatedAt)
            .ToPaginatedListAsync(request.Pagination, cancellationToken)
            .ConfigureAwait(false);

        var names = await accounts.Query.ToDictionaryAsync(static a => a.Id, static a => a.Name, cancellationToken).ConfigureAwait(false);
        var items = page.Items
            .Select(t => new CashTransactionDto(t.Id, t.CashAccountId, names.GetValueOrDefault(t.CashAccountId), t.Direction, t.Amount,
                t.OccurredOn, t.SourceType, t.SourceId, t.Description))
            .ToList();

        return Result<PaginatedList<CashTransactionDto>>.Success(new PaginatedList<CashTransactionDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>A manual cash movement (deposit, withdrawal, correction).</summary>
public sealed record CreateCashTransactionCommand(CreateCashTransactionRequest Request) : IRequest<Result<CashTransactionDto>>;

public sealed class CreateCashTransactionCommandHandler(
    CashPostingService cash,
    IRepository<CashTransaction> transactions,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateCashTransactionCommand, Result<CashTransactionDto>>
{
    public async Task<Result<CashTransactionDto>> Handle(CreateCashTransactionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        if (await cash.IsUnknownAccountAsync(r.CashAccountId, cancellationToken).ConfigureAwait(false))
        {
            return Result<CashTransactionDto>.Failure("CashAccounts.NotFound", "Cash account not found.");
        }

        var accountId = await cash.ResolveAccountAsync(tenantId, r.CashAccountId, cancellationToken).ConfigureAwait(false);
        var on = r.OccurredOn ?? clock.UtcToday;
        var sourceId = Guid.NewGuid();

        await cash.PostAsync(tenantId, accountId, r.Direction, r.Amount, on, CashSourceType.Manual, sourceId, Normalize.Optional(r.Description), cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var row = await transactions.Query.FirstAsync(t => t.SourceType == CashSourceType.Manual && t.SourceId == sourceId, cancellationToken).ConfigureAwait(false);
        return Result<CashTransactionDto>.Success(
            new CashTransactionDto(row.Id, row.CashAccountId, null, row.Direction, row.Amount, row.OccurredOn, row.SourceType, row.SourceId, row.Description),
            "Cash transaction recorded successfully.");
    }
}

// ---- Expenses ----------------------------------------------------------------

public sealed record GetExpenseCategoriesQuery : IRequest<Result<IReadOnlyList<ExpenseCategoryDto>>>;

public sealed class GetExpenseCategoriesQueryHandler(IRepository<ExpenseCategory> categories)
    : IRequestHandler<GetExpenseCategoriesQuery, Result<IReadOnlyList<ExpenseCategoryDto>>>
{
    public async Task<Result<IReadOnlyList<ExpenseCategoryDto>>> Handle(GetExpenseCategoriesQuery request, CancellationToken cancellationToken)
    {
        var rows = await categories.Query.OrderBy(static c => c.Name)
            .Select(static c => new ExpenseCategoryDto(c.Id, c.Name, c.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ExpenseCategoryDto>>.Success(rows);
    }
}

public sealed record CreateExpenseCategoryCommand(CreateExpenseCategoryRequest Request) : IRequest<Result<ExpenseCategoryDto>>;

public sealed class CreateExpenseCategoryCommandHandler(IRepository<ExpenseCategory> categories, ITenantContext tenantContext, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateExpenseCategoryCommand, Result<ExpenseCategoryDto>>
{
    public async Task<Result<ExpenseCategoryDto>> Handle(CreateExpenseCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var name = Normalize.Required(command.Request.Name, string.Empty);

        if (await categories.Query.AnyAsync(c => c.Name == name, cancellationToken).ConfigureAwait(false))
        {
            return Result<ExpenseCategoryDto>.Failure("ExpenseCategories.NameTaken", $"A category named '{name}' already exists.");
        }

        var category = new ExpenseCategory { TenantId = tenantId, Name = name, IsActive = true };
        await categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ExpenseCategoryDto>.Success(new ExpenseCategoryDto(category.Id, category.Name, true), "Expense category created successfully.");
    }
}

public sealed record GetExpensesQuery(PaginationRequest Pagination, Guid? ExpenseCategoryId, DateOnly? From, DateOnly? To)
    : IRequest<Result<PaginatedList<ExpenseDto>>>;

public sealed class GetExpensesQueryHandler(IRepository<Expense> expenses, IRepository<ExpenseCategory> categories)
    : IRequestHandler<GetExpensesQuery, Result<PaginatedList<ExpenseDto>>>
{
    public async Task<Result<PaginatedList<ExpenseDto>>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = expenses.Query;

        if (request.ExpenseCategoryId.HasValue)
        {
            query = query.Where(e => e.ExpenseCategoryId == request.ExpenseCategoryId.Value);
        }

        if (request.From.HasValue)
        {
            query = query.Where(e => e.SpentOn >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(e => e.SpentOn <= request.To.Value);
        }

        var page = await query.OrderByDescending(static e => e.SpentOn).ThenBy(static e => e.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken).ConfigureAwait(false);

        var names = await categories.Query
            .ToDictionaryAsync(static c => c.Id, static c => c.Name, cancellationToken).ConfigureAwait(false);

        var items = page.Items.Select(e => new ExpenseDto(e.Id, e.ExpenseCategoryId, names.GetValueOrDefault(e.ExpenseCategoryId), e.CashAccountId,
            e.Amount, e.SpentOn, e.Description, e.Reference)).ToList();

        return Result<PaginatedList<ExpenseDto>>.Success(new PaginatedList<ExpenseDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>Records an expense and posts it out of a cash account, in one transaction.</summary>
public sealed record CreateExpenseCommand(CreateExpenseRequest Request) : IRequest<Result<ExpenseDto>>;

public sealed class CreateExpenseCommandHandler(
    IRepository<Expense> expenses,
    IRepository<ExpenseCategory> categories,
    CashPostingService cash,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateExpenseCommand, Result<ExpenseDto>>
{
    public async Task<Result<ExpenseDto>> Handle(CreateExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var category = await categories.GetByIdAsync(r.ExpenseCategoryId, cancellationToken).ConfigureAwait(false);

        if (category is null)
        {
            return Result<ExpenseDto>.Failure("ExpenseCategories.NotFound", "Expense category not found.");
        }

        if (await cash.IsUnknownAccountAsync(r.CashAccountId, cancellationToken).ConfigureAwait(false))
        {
            return Result<ExpenseDto>.Failure("CashAccounts.NotFound", "Cash account not found.");
        }

        var accountId = await cash.ResolveAccountAsync(tenantId, r.CashAccountId, cancellationToken).ConfigureAwait(false);
        var expense = new Expense
        {
            TenantId = tenantId,
            ExpenseCategoryId = category.Id,
            CashAccountId = accountId,
            Amount = r.Amount,
            SpentOn = r.SpentOn ?? clock.UtcToday,
            Description = Normalize.Required(r.Description, category.Name),
            Reference = Normalize.Optional(r.Reference)
        };

        await expenses.AddAsync(expense, cancellationToken).ConfigureAwait(false);
        await cash.PostAsync(tenantId, accountId, CashDirection.Out, expense.Amount, expense.SpentOn, CashSourceType.Expense, expense.Id,
            expense.Description, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ExpenseDto>.Success(new ExpenseDto(expense.Id, category.Id, category.Name, accountId, expense.Amount, expense.SpentOn,
            expense.Description, expense.Reference), "Expense recorded successfully.");
    }
}

/// <summary>Soft-deletes an expense and its cash posting together.</summary>
public sealed record DeleteExpenseCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteExpenseCommandHandler(IRepository<Expense> expenses, CashPostingService cash, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteExpenseCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteExpenseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var expense = await expenses.GetByIdAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (expense is null)
        {
            return Result<bool>.Failure("Expenses.NotFound", "Expense not found.");
        }

        expenses.SoftRemove(expense);
        await cash.RemovePostingAsync(CashSourceType.Expense, expense.Id, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<bool>.Success(true, "Expense deleted successfully.");
    }
}

// ---- Teacher compensation & settlements ----------------------------------------

public sealed record ComputeCompensationsCommand(ComputeCompensationsRequest Request) : IRequest<Result<ComputeCompensationsResultDto>>;

public sealed class ComputeCompensationsCommandHandler(CompensationEngine engine, ITenantContext tenantContext, IUnitOfWork unitOfWork)
    : IRequestHandler<ComputeCompensationsCommand, Result<ComputeCompensationsResultDto>>
{
    public async Task<Result<ComputeCompensationsResultDto>> Handle(ComputeCompensationsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var r = command.Request;
        var result = await engine.ComputeAsync(tenantId, r.From, r.To, r.TeacherProfileId, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ComputeCompensationsResultDto>.Success(result, $"{result.Created} session(s) priced.");
    }
}

/// <summary>What a teacher is owed (unsettled compensations) and was paid in a period.</summary>
public sealed record GetTeacherSettlementQuery(Guid TeacherProfileId, DateOnly? From, DateOnly? To) : IRequest<Result<TeacherSettlementDto>>;

public sealed class GetTeacherSettlementQueryHandler(
    IRepository<TeacherProfile> teachers,
    IRepository<SessionCompensation> compensations,
    IRepository<ClassSession> sessions,
    IRepository<TeacherPayment> teacherPayments,
    SchedulingReadModel scheduling)
    : IRequestHandler<GetTeacherSettlementQuery, Result<TeacherSettlementDto>>
{
    public async Task<Result<TeacherSettlementDto>> Handle(GetTeacherSettlementQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var teacherId = request.TeacherProfileId;

        if (!await teachers.ExistsAsync(teacherId, cancellationToken).ConfigureAwait(false))
        {
            return Result<TeacherSettlementDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var from = request.From ?? DateOnly.MinValue;
        var to = request.To ?? DateOnly.MaxValue;

        var rows = await compensations.Query
            .Where(c => c.TeacherProfileId == teacherId && c.TeacherPaymentId == null)
            .Join(sessions.Query, c => c.ClassSessionId, s => s.Id, (c, s) => new { c, s.Date, s.StudyGroupId })
            .Where(x => x.Date >= from && x.Date <= to)
            .OrderBy(static x => x.Date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groupNames = await scheduling.GroupNamesAsync(rows.Select(static r => r.StudyGroupId), cancellationToken).ConfigureAwait(false);
        var names = await scheduling.TeacherNamesAsync([teacherId], cancellationToken).ConfigureAwait(false);

        var paid = await teacherPayments.Query
            .Where(p => p.TeacherProfileId == teacherId && p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(static p => (decimal?)p.Amount, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var items = rows.Select(x => new SessionCompensationDto(x.c.Id, x.c.ClassSessionId, x.Date, groupNames.GetValueOrDefault(x.StudyGroupId),
            teacherId, x.c.Amount, x.c.CompensationRuleId, x.c.TeacherPaymentId)).ToList();

        return Result<TeacherSettlementDto>.Success(new TeacherSettlementDto(
            teacherId, names.GetValueOrDefault(teacherId), items.Sum(static i => i.Amount), items.Count, paid, items));
    }
}

public sealed record GetTeacherPaymentsQuery(PaginationRequest Pagination, Guid? TeacherProfileId) : IRequest<Result<PaginatedList<TeacherPaymentDto>>>;

public sealed class GetTeacherPaymentsQueryHandler(
    IRepository<TeacherPayment> teacherPayments,
    IRepository<SessionCompensation> compensations,
    SchedulingReadModel scheduling)
    : IRequestHandler<GetTeacherPaymentsQuery, Result<PaginatedList<TeacherPaymentDto>>>
{
    public async Task<Result<PaginatedList<TeacherPaymentDto>>> Handle(GetTeacherPaymentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = teacherPayments.Query;

        if (request.TeacherProfileId.HasValue)
        {
            query = query.Where(p => p.TeacherProfileId == request.TeacherProfileId.Value);
        }

        var page = await query.OrderByDescending(static p => p.PaidOn).ThenBy(static p => p.Id)
            .ToPaginatedListAsync(request.Pagination, cancellationToken).ConfigureAwait(false);

        var ids = page.Items.Select(static p => (Guid?)p.Id).ToArray();
        var counts = await compensations.Query.Where(c => ids.Contains(c.TeacherPaymentId))
            .GroupBy(static c => c.TeacherPaymentId!.Value)
            .Select(static g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.Key, static x => x.Count, cancellationToken).ConfigureAwait(false);

        var names = await scheduling.TeacherNamesAsync(page.Items.Select(static p => (Guid?)p.TeacherProfileId), cancellationToken).ConfigureAwait(false);

        var items = page.Items.Select(p => new TeacherPaymentDto(p.Id, p.TeacherProfileId, names.GetValueOrDefault(p.TeacherProfileId), p.Amount,
            p.PaidOn, p.PeriodFrom, p.PeriodTo, p.CashAccountId, p.Notes, counts.GetValueOrDefault(p.Id))).ToList();

        return Result<PaginatedList<TeacherPaymentDto>>.Success(new PaginatedList<TeacherPaymentDto>(items, page.TotalItems, page.PageNumber, page.PageSize));
    }
}

/// <summary>Pays a teacher, settles their unsettled compensations in range and posts the cash out.</summary>
public sealed record CreateTeacherPaymentCommand(CreateTeacherPaymentRequest Request) : IRequest<Result<TeacherPaymentDto>>;

public sealed class CreateTeacherPaymentCommandHandler(
    IRepository<TeacherProfile> teachers,
    IRepository<SessionCompensation> compensations,
    IRepository<ClassSession> sessions,
    IRepository<TeacherPayment> teacherPayments,
    CashPostingService cash,
    SchedulingReadModel scheduling,
    IDateTimeService clock,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateTeacherPaymentCommand, Result<TeacherPaymentDto>>
{
    public async Task<Result<TeacherPaymentDto>> Handle(CreateTeacherPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await unitOfWork.ExecuteInTransactionAsync(ct => PayAsync(command.Request, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<TeacherPaymentDto>> PayAsync(CreateTeacherPaymentRequest r, CancellationToken cancellationToken)
    {
        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);

        if (!await teachers.ExistsAsync(r.TeacherProfileId, cancellationToken).ConfigureAwait(false))
        {
            return Result<TeacherPaymentDto>.Failure("Teachers.NotFound", "Teacher not found.");
        }

        var from = r.From ?? DateOnly.MinValue;
        var to = r.To ?? DateOnly.MaxValue;

        var unsettled = await compensations.Query
            .Where(c => c.TeacherProfileId == r.TeacherProfileId && c.TeacherPaymentId == null)
            .Join(sessions.Query, c => c.ClassSessionId, s => s.Id, (c, s) => new { c, s.Date })
            .Where(x => x.Date >= from && x.Date <= to)
            .Select(static x => x.c)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var due = unsettled.Sum(static c => c.Amount);

        if (unsettled.Count == 0 || due <= 0m)
        {
            return Result<TeacherPaymentDto>.Failure("TeacherPayments.NothingToPay", "There is nothing to pay for this teacher in the range.");
        }

        // A payment settles every unsettled session in the range, so it must match what those
        // sessions are worth: a smaller amount would mark unpaid sessions as settled, a larger
        // one would pay for sessions that were never held. There is no advance-payment rule.
        if (r.Amount is { } requested && requested != due)
        {
            return Result<TeacherPaymentDto>.Failure(
                "TeacherPayments.AmountMismatch",
                $"The amount must equal the unsettled compensation for the range ({due}).");
        }

        var amount = due;

        if (await cash.IsUnknownAccountAsync(r.CashAccountId, cancellationToken).ConfigureAwait(false))
        {
            return Result<TeacherPaymentDto>.Failure("CashAccounts.NotFound", "Cash account not found.");
        }

        var accountId = await cash.ResolveAccountAsync(tenantId, r.CashAccountId, cancellationToken).ConfigureAwait(false);
        var payment = new TeacherPayment
        {
            TenantId = tenantId,
            TeacherProfileId = r.TeacherProfileId,
            CashAccountId = accountId,
            Amount = amount,
            PaidOn = r.PaidOn ?? clock.UtcToday,
            PeriodFrom = r.From,
            PeriodTo = r.To,
            Notes = Normalize.Optional(r.Notes)
        };

        await teacherPayments.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        foreach (var c in unsettled)
        {
            c.TeacherPaymentId = payment.Id;
        }

        await cash.PostAsync(tenantId, accountId, CashDirection.Out, amount, payment.PaidOn, CashSourceType.TeacherPayment, payment.Id,
            "Teacher payment", cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var names = await scheduling.TeacherNamesAsync([r.TeacherProfileId], cancellationToken).ConfigureAwait(false);

        return Result<TeacherPaymentDto>.Success(new TeacherPaymentDto(payment.Id, payment.TeacherProfileId, names.GetValueOrDefault(payment.TeacherProfileId),
            payment.Amount, payment.PaidOn, payment.PeriodFrom, payment.PeriodTo, accountId, payment.Notes, unsettled.Count), "Teacher payment recorded successfully.");
    }
}

// ---- Reports & dashboard -----------------------------------------------------

public sealed record GetFinanceSummaryQuery(DateOnly From, DateOnly To) : IRequest<Result<FinanceSummaryDto>>;

public sealed class GetFinanceSummaryQueryHandler(
    IRepository<Payment> payments,
    IRepository<Expense> expenses,
    IRepository<ExpenseCategory> categories,
    IRepository<TeacherPayment> teacherPayments,
    IRepository<Charge> charges,
    CashPostingService cash)
    : IRequestHandler<GetFinanceSummaryQuery, Result<FinanceSummaryDto>>
{
    public async Task<Result<FinanceSummaryDto>> Handle(GetFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (from, to) = (request.From, request.To);

        var income = await payments.Query.Where(p => !p.IsVoided && p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(static p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var expenseRows = await expenses.Query.Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .GroupBy(static e => e.ExpenseCategoryId)
            .Select(static g => new { g.Key, Total = g.Sum(static e => e.Amount) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var categoryNames = await categories.Query.ToDictionaryAsync(static c => c.Id, static c => c.Name, cancellationToken).ConfigureAwait(false);

        var teacherPaid = await teacherPayments.Query.Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(static p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var issued = await charges.Query.Where(c => c.Status != ChargeStatus.Cancelled && c.DueDate >= from && c.DueDate <= to)
            .SumAsync(static c => (decimal?)c.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;

        var outstanding = await charges.Query.Where(static c => c.Status == ChargeStatus.Open || c.Status == ChargeStatus.PartiallyPaid)
            .SumAsync(static c => (decimal?)(c.Amount - c.AmountPaid), cancellationToken).ConfigureAwait(false) ?? 0m;

        var expenseTotal = expenseRows.Sum(static e => e.Total);
        var byCategory = expenseRows
            .Select(e => new CategoryTotalDto(e.Key, categoryNames.GetValueOrDefault(e.Key) ?? "?", e.Total))
            .OrderByDescending(static e => e.Total)
            .ToList();

        return Result<FinanceSummaryDto>.Success(new FinanceSummaryDto(from, to, income, expenseTotal, teacherPaid,
            income - expenseTotal - teacherPaid, outstanding, issued, byCategory, await cash.BalancesAsync(cancellationToken).ConfigureAwait(false)));
    }
}

public sealed record GetDashboardKpisQuery(bool IncludeFinance) : IRequest<Result<DashboardKpisDto>>;

public sealed class GetDashboardKpisQueryHandler(
    IRepository<StudentProfile> students,
    IRepository<StudyGroup> groups,
    IRepository<TeacherProfile> teachers,
    IRepository<ClassSession> sessions,
    IRepository<SessionAttendance> attendance,
    IRepository<Charge> charges,
    IRepository<Payment> payments,
    IRepository<Expense> expenses,
    IDateTimeService clock)
    : IRequestHandler<GetDashboardKpisQuery, Result<DashboardKpisDto>>
{
    public async Task<Result<DashboardKpisDto>> Handle(GetDashboardKpisQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var today = clock.GetTenantToday();
        var since = today.AddDays(-30);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var activeStudents = await students.Query.CountAsync(static s => s.Status == StudentStatus.Active, cancellationToken).ConfigureAwait(false);
        var activeGroups = await groups.Query.CountAsync(static g => g.IsActive, cancellationToken).ConfigureAwait(false);
        var activeTeachers = await teachers.Query.CountAsync(static t => t.IsActive, cancellationToken).ConfigureAwait(false);
        var sessionsToday = await sessions.Query.CountAsync(s => s.Date == today && s.Status != SessionStatus.Cancelled, cancellationToken).ConfigureAwait(false);

        var recent = sessions.Query.Where(s => s.Date >= since && s.Date <= today).Select(static s => s.Id);
        var marks = attendance.Query.Where(a => recent.Contains(a.ClassSessionId));
        var total = await marks.CountAsync(cancellationToken).ConfigureAwait(false);
        var present = await marks.CountAsync(static a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late, cancellationToken).ConfigureAwait(false);
        decimal? rate = total == 0 ? null : Math.Round(100m * present / total, 1);

        decimal? outstanding = null, income = null, spent = null;

        if (request.IncludeFinance)
        {
            outstanding = await charges.Query.Where(static c => c.Status == ChargeStatus.Open || c.Status == ChargeStatus.PartiallyPaid)
                .SumAsync(static c => (decimal?)(c.Amount - c.AmountPaid), cancellationToken).ConfigureAwait(false) ?? 0m;
            income = await payments.Query.Where(p => !p.IsVoided && p.PaidOn >= monthStart && p.PaidOn <= today)
                .SumAsync(static p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
            spent = await expenses.Query.Where(e => e.SpentOn >= monthStart && e.SpentOn <= today)
                .SumAsync(static e => (decimal?)e.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
        }

        return Result<DashboardKpisDto>.Success(new DashboardKpisDto(activeStudents, activeGroups, activeTeachers, sessionsToday, rate, outstanding, income, spent));
    }
}

// ---- Validators --------------------------------------------------------------

public sealed class CreateCashAccountCommandValidator : LocalizedValidator<CreateCashAccountCommand>
{
    public CreateCashAccountCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Type).IsInEnum().When(static x => x.Request is not null);
        RuleFor(x => x.Request.OpeningBalance).GreaterThanOrEqualTo(0m).When(static x => x.Request is not null);
    }
}

public sealed class CreateCashTransactionCommandValidator : LocalizedValidator<CreateCashTransactionCommand>
{
    public CreateCashTransactionCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.CashAccountId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThan(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Direction).IsInEnum().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Description).MaximumLength(300).When(static x => x.Request is not null);
    }
}

public sealed class CreateExpenseCategoryCommandValidator : LocalizedValidator<CreateExpenseCategoryCommand>
{
    public CreateExpenseCategoryCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(100).When(static x => x.Request is not null);
    }
}

public sealed class CreateExpenseCommandValidator : LocalizedValidator<CreateExpenseCommand>
{
    public CreateExpenseCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.ExpenseCategoryId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThan(0m).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Description).MaximumLength(300).When(static x => x.Request is not null);
        RuleFor(x => x.Request.Reference).MaximumLength(100).When(static x => x.Request is not null);
    }
}

public sealed class ComputeCompensationsCommandValidator : LocalizedValidator<ComputeCompensationsCommand>
{
    public ComputeCompensationsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).Must(static r => r.To >= r.From && r.To.DayNumber - r.From.DayNumber <= 366)
            .WithMessage("The range must end on or after its start and span at most a year.")
            .When(static x => x.Request is not null);
    }
}

public sealed class CreateTeacherPaymentCommandValidator : LocalizedValidator<CreateTeacherPaymentCommand>
{
    public CreateTeacherPaymentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request.TeacherProfileId).NotEmpty().When(static x => x.Request is not null);
        RuleFor(x => x.Request.Amount).GreaterThan(0m).When(static x => x.Request?.Amount is not null);
        RuleFor(x => x.Request.Notes).MaximumLength(500).When(static x => x.Request is not null);
        RuleFor(x => x.Request).Must(static r => r.From is null || r.To is null || r.To >= r.From)
            .WithMessage("The period must end on or after its start.")
            .When(static x => x.Request is not null);
    }
}

public sealed class GetFinanceSummaryQueryValidator : LocalizedValidator<GetFinanceSummaryQuery>
{
    public GetFinanceSummaryQueryValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x).Must(static q => q.To >= q.From && q.To.DayNumber - q.From.DayNumber <= 366)
            .WithMessage("The range must end on or after its start and span at most a year.");
    }
}

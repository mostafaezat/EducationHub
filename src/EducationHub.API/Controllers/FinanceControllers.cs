using EducationHub.Application.Features.Finance;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EducationHub.API.Controllers;

/// <summary>Fee rules (price list) and charges (Mission Phase 4).</summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public sealed class FeesController(IMediator mediator) : ControllerBase
{
    [HttpGet("fee-rules")]
    [Authorize(Policy = PermissionConstants.FeesView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FeeRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<FeeRuleDto>>>> GetRules([FromQuery] bool activeOnly = false, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetFeeRulesQuery(activeOnly), cancellationToken).ConfigureAwait(false));

    [HttpPost("fee-rules")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<FeeRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<FeeRuleDto>>> CreateRule([FromBody] CreateFeeRuleRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateFeeRuleCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("fee-rules/{id:guid}")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<FeeRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<FeeRuleDto>>> UpdateRule(Guid id, [FromBody] UpdateFeeRuleRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateFeeRuleCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("fee-rules/{id:guid}")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> DeleteRule(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteFeeRuleCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpGet("charges")]
    [Authorize(Policy = PermissionConstants.FeesView)]
    [ProducesResponseType(typeof(Result<PaginatedList<ChargeDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<ChargeDto>>>> GetCharges(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? studentProfileId = null,
        [FromQuery] ChargeStatus? status = null,
        [FromQuery] string? periodKey = null,
        [FromQuery] bool outstandingOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetChargesQuery(pagination, studentProfileId, status, periodKey, outstandingOnly), cancellationToken).ConfigureAwait(false));

    [HttpPost("charges")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<ChargeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ChargeDto>>> CreateCharge([FromBody] CreateChargeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateChargeCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPost("charges/{id:guid}/cancel")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<ChargeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ChargeDto>>> CancelCharge(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CancelChargeCommand(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Generates monthly charges for every active enrollment (idempotent per period).</summary>
    [HttpPost("charges/generate")]
    [Authorize(Policy = PermissionConstants.FeesManage)]
    [ProducesResponseType(typeof(Result<GenerateChargesResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<GenerateChargesResultDto>>> Generate([FromBody] GenerateChargesRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GenerateChargesCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpGet("students/{studentId:guid}/balance")]
    [Authorize(Policy = PermissionConstants.FeesView)]
    [ProducesResponseType(typeof(Result<StudentBalanceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<StudentBalanceDto>>> Balance(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentBalanceQuery(studentId), cancellationToken).ConfigureAwait(false));
}

/// <summary>Student payments (Mission Phase 4).</summary>
[ApiController]
[Route("api/payments")]
[Authorize]
[Produces("application/json")]
public sealed class PaymentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.PaymentsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<PaymentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<PaymentDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? studentProfileId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetPaymentsQuery(pagination, studentProfileId, from, to, includeVoided), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.PaymentsView)]
    [ProducesResponseType(typeof(Result<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaymentDto>>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPaymentQuery(id), cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = PermissionConstants.PaymentsCreate)]
    [ProducesResponseType(typeof(Result<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaymentDto>>> Create([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreatePaymentCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionConstants.PaymentsVoid)]
    [ProducesResponseType(typeof(Result<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaymentDto>>> Void(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] VoidPaymentRequest? request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new VoidPaymentCommand(id, request ?? new VoidPaymentRequest(null)), cancellationToken).ConfigureAwait(false));
}

/// <summary>Cash accounts, transactions, expenses, teacher settlements and reports (Mission Phase 5).</summary>
[ApiController]
[Route("api/finance")]
[Authorize]
[Produces("application/json")]
public sealed class FinanceController(IMediator mediator) : ControllerBase
{
    [HttpGet("cash-accounts")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<CashAccountDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<CashAccountDto>>>> GetAccounts(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCashAccountsQuery(), cancellationToken).ConfigureAwait(false));

    [HttpPost("cash-accounts")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<CashAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<CashAccountDto>>> CreateAccount([FromBody] CreateCashAccountRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateCashAccountCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("cash-accounts/{id:guid}")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<CashAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<CashAccountDto>>> UpdateAccount(Guid id, [FromBody] UpdateCashAccountRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateCashAccountCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpGet("transactions")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<PaginatedList<CashTransactionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<CashTransactionDto>>>> GetTransactions(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? cashAccountId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetCashTransactionsQuery(pagination, cashAccountId, from, to), cancellationToken).ConfigureAwait(false));

    [HttpPost("transactions")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<CashTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<CashTransactionDto>>> CreateTransaction([FromBody] CreateCashTransactionRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateCashTransactionCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpGet("expense-categories")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ExpenseCategoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<ExpenseCategoryDto>>>> GetCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExpenseCategoriesQuery(), cancellationToken).ConfigureAwait(false));

    [HttpPost("expense-categories")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<ExpenseCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ExpenseCategoryDto>>> CreateCategory([FromBody] CreateExpenseCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateExpenseCategoryCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpGet("expenses")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<PaginatedList<ExpenseDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<ExpenseDto>>>> GetExpenses(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? expenseCategoryId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetExpensesQuery(pagination, expenseCategoryId, from, to), cancellationToken).ConfigureAwait(false));

    [HttpPost("expenses")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<ExpenseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ExpenseDto>>> CreateExpense([FromBody] CreateExpenseRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateExpenseCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("expenses/{id:guid}")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> DeleteExpense(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteExpenseCommand(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Prices completed sessions for their teachers (idempotent per session).</summary>
    [HttpPost("compensations/compute")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<ComputeCompensationsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ComputeCompensationsResultDto>>> Compute([FromBody] ComputeCompensationsRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ComputeCompensationsCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpGet("teachers/{teacherId:guid}/settlement")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<TeacherSettlementDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<TeacherSettlementDto>>> Settlement(
        Guid teacherId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetTeacherSettlementQuery(teacherId, from, to), cancellationToken).ConfigureAwait(false));

    [HttpGet("teacher-payments")]
    [Authorize(Policy = PermissionConstants.FinanceView)]
    [ProducesResponseType(typeof(Result<PaginatedList<TeacherPaymentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<TeacherPaymentDto>>>> GetTeacherPayments(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? teacherProfileId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetTeacherPaymentsQuery(pagination, teacherProfileId), cancellationToken).ConfigureAwait(false));

    [HttpPost("teacher-payments")]
    [Authorize(Policy = PermissionConstants.FinanceManage)]
    [ProducesResponseType(typeof(Result<TeacherPaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<TeacherPaymentDto>>> PayTeacher([FromBody] CreateTeacherPaymentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateTeacherPaymentCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpGet("summary")]
    [Authorize(Policy = PermissionConstants.ReportsView)]
    [ProducesResponseType(typeof(Result<FinanceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<FinanceSummaryDto>>> Summary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFinanceSummaryQuery(from, to), cancellationToken).ConfigureAwait(false));
}

/// <summary>Dashboard KPIs (closes the Dashboard.View permission gap, BL-007).</summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
[Produces("application/json")]
public sealed class DashboardController(IMediator mediator) : ControllerBase
{
    [HttpGet("kpis")]
    [Authorize(Policy = PermissionConstants.DashboardView)]
    [ProducesResponseType(typeof(Result<DashboardKpisDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<DashboardKpisDto>>> Kpis(CancellationToken cancellationToken)
    {
        var includeFinance = User.HasClaim(EducationHubClaimTypes.PermissionClaimType, PermissionConstants.ReportsView);
        return Ok(await mediator.Send(new GetDashboardKpisQuery(includeFinance), cancellationToken).ConfigureAwait(false));
    }
}

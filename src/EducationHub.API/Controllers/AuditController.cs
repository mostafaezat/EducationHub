using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Audit.DTOs;
using EducationHub.Application.Features.Audit.Queries;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Read-only audit history.</summary>
[ApiController]
[Route("api/audit")]
[Authorize]
[Produces("application/json")]
public sealed class AuditController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages audit entries.</summary>
    /// <remarks>
    /// Read-only by design: no endpoint here mutates an audit row. An audit trail that can
    /// be edited is not evidence of anything, so corrections are appended, never applied.
    /// </remarks>
    [HttpGet("logs")]
    [Authorize(Policy = PermissionConstants.AuditLogsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<AuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<AuditLogDto>>>> Logs(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] AuditAction? action = null,
        [FromQuery] string? entityName = null,
        [FromQuery] string? entityId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetAuditLogsQuery(pagination, from, to, userId, action, entityName, entityId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one audit entry.</summary>
    [HttpGet("logs/{id:guid}")]
    [Authorize(Policy = PermissionConstants.AuditLogsView)]
    [ProducesResponseType(typeof(Result<AuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<AuditLogDto>>> Log(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAuditLogQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Grouped activity counts for the dashboard.
    /// </summary>
    /// <remarks>
    /// Aggregated in SQL rather than by counting paged rows in memory: a busy tenant's range
    /// can hold hundreds of thousands of entries, which would be unreadable as a page and
    /// absurd to count client-side.
    /// </remarks>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionConstants.AuditLogsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<AuditSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<AuditSummaryDto>>>> Summary(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetAuditSummaryQuery(from, to, top), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Pages authentication attempts.</summary>
    /// <remarks>
    /// Where an administrator looks when investigating a lockout: the failure reasons
    /// distinguish a wrong password from a disabled account.
    /// </remarks>
    [HttpGet("logins")]
    [Authorize(Policy = PermissionConstants.AuditLogsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<LoginHistoryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<LoginHistoryDto>>>> Logins(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? userId = null,
        [FromQuery] bool succeededOnly = false,
        [FromQuery] bool failedOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetLoginHistoryQuery(pagination, userId, succeededOnly, failedOnly), cancellationToken)
            .ConfigureAwait(false));
}
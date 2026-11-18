using EducationHub.API.Auth;
using EducationHub.Application.Features.Registration;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EducationHub.API.Controllers;

/// <summary>
/// The centre's public pre-registration page. Anonymous: the tenant comes from the slug in the
/// URL, never from a token.
/// </summary>
[ApiController]
[Route("api/public/registration/{tenantSlug}")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class PublicRegistrationController(IMediator mediator) : ControllerBase
{
    /// <summary>The centre's name and the grade levels and subjects to choose from.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Result<PublicRegistrationFormDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<PublicRegistrationFormDto>>> GetForm(
        string tenantSlug,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPublicRegistrationFormQuery(tenantSlug), cancellationToken).ConfigureAwait(false));

    /// <summary>Sends a pre-registration request; the centre's staff review it.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.PublicRegistration)]
    [ProducesResponseType(typeof(Result<RegistrationSubmittedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result<RegistrationSubmittedDto>>> Submit(
        string tenantSlug,
        [FromBody] SubmitRegistrationRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SubmitRegistrationCommand(tenantSlug, request), cancellationToken).ConfigureAwait(false));
}

/// <summary>Staff review of pre-registration requests.</summary>
[ApiController]
[Route("api/registration-requests")]
[Authorize]
[Produces("application/json")]
public sealed class RegistrationRequestsController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the centre's requests, newest first; filter by status, search by name or mobile.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.RegistrationRequestsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<RegistrationRequestDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<RegistrationRequestDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] RegistrationRequestStatus? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetRegistrationRequestsQuery(pagination, status, search), cancellationToken).ConfigureAwait(false));

    /// <summary>The current centre's slug for the shareable link <c>/register/{slug}</c>.</summary>
    [HttpGet("link")]
    [Authorize(Policy = PermissionConstants.RegistrationRequestsView)]
    [ProducesResponseType(typeof(Result<RegistrationLinkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<RegistrationLinkDto>>> GetLink(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRegistrationLinkQuery(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Approves a pending request: creates the student and guardian (reusing a parent with the same
    /// e-mail or mobile number), optionally enrolls the student in a group.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = PermissionConstants.RegistrationRequestsManage)]
    [ProducesResponseType(typeof(Result<ApproveRegistrationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Result<ApproveRegistrationResultDto>>> Approve(
        Guid id,
        [FromBody] ApproveRegistrationRequest? request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ApproveRegistrationCommand(id, request ?? new ApproveRegistrationRequest()), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Rejects a pending request with a reason.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermissionConstants.RegistrationRequestsManage)]
    [ProducesResponseType(typeof(Result<RegistrationRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Result<RegistrationRequestDto>>> Reject(
        Guid id,
        [FromBody] RejectRegistrationRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RejectRegistrationCommand(id, request), cancellationToken).ConfigureAwait(false));
}

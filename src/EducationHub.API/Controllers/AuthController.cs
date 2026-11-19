using EducationHub.API.Auth;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.Commands;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Application.Features.Auth.Queries;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EducationHub.API.Controllers;

/// <summary>Authentication endpoints.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Signs in with email and password.
    /// </summary>
    /// <remarks>
    /// The tenant is not part of the request. Email is unique across the whole platform, so
    /// the account itself identifies the tenant and the caller never chooses which one they
    /// enter - which is what stops someone from selecting a tenant they do not belong to.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Credential)]
    [CredentialRateLimit("login")]
    [ProducesResponseType(typeof(Result<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result<AuthResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new LoginCommand(request), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Exchanges a refresh token for a new token pair.
    /// </summary>
    /// <remarks>
    /// Rotates: the presented token is burned and its replacement returned. Replaying an
    /// already-used token revokes the entire chain, on the assumption that two parties now
    /// hold tokens from one login.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType(typeof(Result<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result<AuthResponse>>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RefreshTokenCommand(request), cancellationToken).ConfigureAwait(false));

    /// <summary>Signs out, revoking the presented refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result>> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new LogoutCommand(request ?? new LogoutRequest(null)), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Changes the signed-in user's own password.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Result>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ChangePasswordCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Emails a password reset link. Always succeeds, so addresses cannot be probed.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Credential)]
    [CredentialRateLimit("forgot-password")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ForgotPasswordCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Completes a password reset with the token from the emailed link.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ResetPasswordCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Sends a 6-digit password-reset code by WhatsApp to a registered mobile number. Always
    /// answers the same success, so numbers cannot be probed.
    /// </summary>
    [HttpPost("forgot-password/phone")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PhoneReset)]
    [PhoneRateLimit("forgot")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result>> ForgotPasswordByPhone(
        [FromBody] ForgotPasswordByPhoneRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ForgotPasswordByPhoneCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Completes a password reset with the code sent by WhatsApp.</summary>
    [HttpPost("reset-password/phone")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PhoneReset)]
    [PhoneRateLimit("reset")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result>> ResetPasswordByPhone(
        [FromBody] ResetPasswordByPhoneRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ResetPasswordByPhoneCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Confirms an email with the token from the confirmation email.</summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ConfirmEmailCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Re-sends the confirmation link for an unconfirmed account. Always succeeds.</summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Credential)]
    [CredentialRateLimit("resend-confirmation")]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<Result>> ResendConfirmation(
        [FromBody] ResendConfirmationRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ResendConfirmationCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>The signed-in user's profile, roles and permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(Result<UserProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<Result<UserProfileDto>>> Me(CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetCurrentUserQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>The signed-in user's effective permissions, grouped by area.</summary>
    /// <remarks>
    /// Separate from <c>me</c> because the SPA re-checks permissions after any change that
    /// could affect them, without refetching the whole profile.
    /// </remarks>
    [HttpGet("permissions")]
    [Authorize]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PermissionAreaDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<PermissionAreaDto>>>> Permissions(
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetPermissionsQuery(), cancellationToken)
            .ConfigureAwait(false));
}
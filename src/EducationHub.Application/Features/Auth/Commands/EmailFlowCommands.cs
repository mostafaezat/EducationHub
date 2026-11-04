using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EducationHub.Application.Features.Auth.Commands;

/// <summary>
/// Starts a password reset by emailing a reset link when the address is known.
/// </summary>
public sealed record ForgotPasswordCommand(ForgotPasswordRequest Request) : IRequest<Result>;

public sealed class ForgotPasswordCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IWebLinkBuilder links,
    ILogger<ForgotPasswordCommandHandler> logger)
    : IRequestHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var snapshot = await authentication
            .GetByEmailAsync(command.Request.Email.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            // Identical response to a known address: revealing which emails are registered
            // would turn this endpoint into a live account-enumeration oracle.
            logger.LogInformation(
                "Password reset requested for an unknown email; nothing was sent.");
            return Result.Success(
                "If that email is registered, a password reset link has been sent.");
        }

        var user = snapshot.User;

        var token = await userManager
            .GeneratePasswordResetTokenAsync(user)
            .ConfigureAwait(false);

        var link = links.PasswordResetLink(user.Email ?? string.Empty, token);

        await emailSender
            .SendAsync(
                new EmailMessage(
                    user.Email ?? string.Empty,
                    "Reset your EducationHub password",
                    $"You asked to reset your password. Open this link to choose a new one:\n\n{link}\n\n"
                    + "If you did not ask for this, ignore the message and your password will stay unchanged."),
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success("If that email is registered, a password reset link has been sent.");
    }
}

/// <summary>
/// Completes a password reset using the token from the emailed link.
/// </summary>
public sealed record ResetPasswordCommand(ResetPasswordRequest Request) : IRequest<Result>;

public sealed class ResetPasswordCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IDateTimeService dateTime,
    IUnitOfWork unitOfWork,
    ILogger<ResetPasswordCommandHandler> logger)
    : IRequestHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var snapshot = await authentication
            .GetByEmailAsync(command.Request.Email.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            // No account, nothing to reset. Same success response as a reset that worked, so
            // this endpoint cannot be used to learn which emails exist.
            logger.LogInformation(
                "Password reset submitted for an unknown email; nothing was changed.");
            return Result.Success("Your password has been reset. Please sign in.");
        }

        var user = snapshot.User;

        var tokenValid = await userManager
            .VerifyUserTokenAsync(
                user,
                userManager.Options.Tokens.PasswordResetTokenProvider,
                "ResetPassword",
                command.Request.ResetToken)
            .ConfigureAwait(false);

        if (!tokenValid)
        {
            throw new BusinessRuleException(
                "Auth.InvalidResetToken",
                "The reset link is invalid or has expired. Request a new one.");
        }

        // Validation succeeded (the token is bound to the account's security stamp), so set
        // the hash directly. One unit of work then writes the new hash, the cleared lockout
        // and the revocation of every existing session together - a crash between them would
        // leave pre-reset sessions alive against the new password. The user-row change is
        // audited by the interceptor (PasswordHash redacted).
        user.PasswordHash = passwordHasher.HashPassword(user, command.Request.NewPassword);
        user.PasswordChangedAt = dateTime.UtcNow;
        user.MustChangePassword = false;
        user.AccessFailedCount = 0;
        user.LockedOutUntil = null;

        await authentication
            .RevokeAllForUserAsync(user.Id, "PasswordReset", cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success("Your password has been reset. Please sign in.");
    }
}

/// <summary>Confirms an email address using the token from the confirmation email.</summary>
public sealed record ConfirmEmailCommand(ConfirmEmailRequest Request) : IRequest<Result>;

public sealed class ConfirmEmailCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<ConfirmEmailCommand, Result>
{
    public async Task<Result> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var snapshot = await authentication
            .GetByEmailAsync(command.Request.Email.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            // Mirror the generic success: neither confirming nor resetting may reveal
            // whether an address is registered.
            return Result.Success("Your email has been confirmed. You can now sign in.");
        }

        var user = snapshot.User;

        if (user.EmailConfirmed)
        {
            return Result.Success("Your email has been confirmed. You can now sign in.");
        }

        var result = await userManager
            .ConfirmEmailAsync(user, command.Request.Token)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Our stable code first: the filter renders Errors[0].Code as the response title,
            // and Identity's raw "InvalidToken" is an implementation detail, not a contract.
            throw new BusinessRuleException(
                "Auth.InvalidEmailToken",
                "The confirmation link is invalid or has expired. Request a new one.",
                [
                    new Error(
                        "Auth.InvalidEmailToken",
                        "The confirmation link is invalid or has expired. Request a new one."),
                    .. result.Errors.Select(
                        static e => new Error(e.Code, e.Description))
                ]);
        }

        return Result.Success("Your email has been confirmed. You can now sign in.");
    }
}

/// <summary>Re-sends the email confirmation link for an unconfirmed account.</summary>
public sealed record ResendConfirmationCommand(ResendConfirmationRequest Request) : IRequest<Result>;

public sealed class ResendConfirmationCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IWebLinkBuilder links,
    ILogger<ResendConfirmationCommandHandler> logger)
    : IRequestHandler<ResendConfirmationCommand, Result>
{
    public async Task<Result> Handle(ResendConfirmationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var snapshot = await authentication
            .GetByEmailAsync(command.Request.Email.Trim().ToUpperInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null || snapshot.User.EmailConfirmed)
        {
            // No account, or nothing left to confirm: one indistinguishable response, so the
            // endpoint cannot be used to enumerate registered (or unconfirmed) addresses.
            logger.LogInformation(
                "Confirmation link requested for an account that needs none; nothing was sent.");
            return Result.Success("If that email needs confirmation, a new link has been sent.");
        }

        var user = snapshot.User;

        var token = await userManager
            .GenerateEmailConfirmationTokenAsync(user)
            .ConfigureAwait(false);

        await emailSender
            .SendAsync(
                new EmailMessage(
                    user.Email ?? string.Empty,
                    "Confirm your EducationHub email",
                    $"Please confirm this email address to activate the account:\n\n"
                    + $"{links.EmailConfirmationLink(user.Email ?? string.Empty, token)}\n\n"
                    + "The link is valid for a short time and can only be used once."),
                cancellationToken)
            .ConfigureAwait(false);

        return Result.Success("If that email needs confirmation, a new link has been sent.");
    }
}
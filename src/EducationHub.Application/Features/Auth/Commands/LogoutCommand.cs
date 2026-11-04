using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Auth.Commands;

/// <summary>Ends the current session by revoking the supplied refresh token.</summary>
public sealed record LogoutCommand(LogoutRequest Request) : IRequest<Result>;

public sealed class LogoutCommandHandler(
    IAuthenticationRepository authentication,
    ITokenService tokenService)
    : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Request.RefreshToken))
        {
            // Nothing to revoke; a logout with no session is still a success, because the
            // caller's desired end state (not being logged in) already holds.
            return Result.Success("Signed out.");
        }

        var stored = await authentication
            .FindRefreshTokenAsync(tokenService.HashRefreshToken(command.Request.RefreshToken), cancellationToken)
            .ConfigureAwait(false);

        if (stored is not null && stored.RevokedAt is null)
        {
            await authentication
                .RevokeRefreshTokenAsync(stored, "Logout", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return Result.Success("Signed out.");
    }
}

/// <summary>Changes the caller's own password after re-verifying the current one.</summary>
public sealed record ChangePasswordCommand(ChangePasswordRequest Request) : IRequest<Result>;

public sealed class ChangePasswordCommandHandler(
    IAuthenticationRepository authentication,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ICurrentUser currentUser,
    IDateTimeService dateTime,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.UserId
            ?? throw new BusinessRuleException("Auth.Unauthenticated", "Authentication is required.");

        var snapshot = await authentication.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (snapshot is null)
        {
            throw new BusinessRuleException("Account.NotFound", "The account no longer exists.");
        }

        var user = snapshot.User;

        // Re-verify the current password so a stolen access token alone cannot lock the real
        // owner out of their own account.
        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash ?? string.Empty,
            command.Request.CurrentPassword);

        if (verification == PasswordVerificationResult.Failed)
        {
            throw new BusinessRuleException(
                "Auth.CurrentPasswordIncorrect",
                "The current password is incorrect.");
        }

        // ASP.NET Core Identity performs the strength check through its own validators; the
        // FluentValidation rules on the request mirror the configured policy for a clear
        // 400 response before any hashing work happens.
        user.PasswordHash = passwordHasher.HashPassword(user, command.Request.NewPassword);
        user.PasswordChangedAt = dateTime.UtcNow;
        user.MustChangePassword = false;

        // A changed password leaves every existing session invalid: all of the user's live
        // refresh-token families are revoked, so no device can keep going on the old
        // credential. This runs (unstaged) in the same unit of work as the hash update;
        // one commit makes the new password effective and the old sessions dead together,
        // and a failure rolls both back. The user-row change is audited by the
        // AuditSaveChangesInterceptor in that same save (PasswordHash is redacted).
        await authentication
            .RevokeAllForUserAsync(userId, "PasswordChanged", cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success("Password changed successfully.");
    }
}

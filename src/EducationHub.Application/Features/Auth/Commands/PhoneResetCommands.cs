using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Common.Messaging;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Localization;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace EducationHub.Application.Features.Auth.Commands;

/// <summary>
/// Password reset by mobile number, for accounts that have no real e-mail (phone-only parents).
/// The code is a 6-digit one-time code from Identity's phone token provider: it is bound to the
/// account's security stamp and expires within minutes, so no table is needed to store it.
/// </summary>
public static class PhoneReset
{
    /// <summary>Token purpose; a code generated for one purpose never validates for another.</summary>
    public const string Purpose = "ResetPasswordByPhone";

    internal static string Localize(IStringLocalizer<LocalizationResource> localizer, string key, string fallback)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.IsNullOrWhiteSpace(value.Value) ? fallback : value.Value;
    }
}

/// <summary>Sends a password-reset code by WhatsApp when the number belongs to exactly one account.</summary>
public sealed record ForgotPasswordByPhoneCommand(ForgotPasswordByPhoneRequest Request) : IRequest<Result>;

public sealed class ForgotPasswordByPhoneCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager,
    WhatsAppMessenger whatsApp,
    IStringLocalizer<LocalizationResource> localizer,
    ILogger<ForgotPasswordByPhoneCommandHandler> logger)
    : IRequestHandler<ForgotPasswordByPhoneCommand, Result>
{
    public async Task<Result> Handle(ForgotPasswordByPhoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One answer whatever happens below, so the endpoint cannot tell which numbers exist.
        var generic = Result.Success(PhoneReset.Localize(
            localizer,
            LocalizationKeys.AuthPhoneResetCodeSent,
            "If that mobile number is registered, a reset code has been sent by WhatsApp."));

        var phone = PhoneNumberFormat.ToCanonical(command.Request.Phone);

        var snapshot = await authentication
            .GetByPhoneAsync(PhoneNumberFormat.LookupVariants(phone), cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null || !snapshot.User.IsActive)
        {
            logger.LogInformation("Phone password reset requested for an unknown or ambiguous number; nothing was sent.");
            return generic;
        }

        var code = await userManager
            .GenerateUserTokenAsync(snapshot.User, TokenOptions.DefaultPhoneProvider, PhoneReset.Purpose)
            .ConfigureAwait(false);

        // Best-effort: a WhatsApp failure is logged by the messenger and the answer stays the same.
        await whatsApp.SendPasswordResetCodeAsync(phone, code, cancellationToken).ConfigureAwait(false);

        return generic;
    }
}

/// <summary>Completes a phone password reset with the WhatsApp code.</summary>
public sealed record ResetPasswordByPhoneCommand(ResetPasswordByPhoneRequest Request) : IRequest<Result>;

public sealed class ResetPasswordByPhoneCommandHandler(
    IAuthenticationRepository authentication,
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IDateTimeService dateTime,
    IUnitOfWork unitOfWork,
    IStringLocalizer<LocalizationResource> localizer)
    : IRequestHandler<ResetPasswordByPhoneCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordByPhoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;

        var snapshot = await authentication
            .GetByPhoneAsync(PhoneNumberFormat.LookupVariants(request.Phone), cancellationToken)
            .ConfigureAwait(false);

        // An unknown number and a wrong code answer identically, so neither reveals an account.
        var invalid = Result.Failure(
            LocalizationKeys.AuthInvalidResetCode,
            PhoneReset.Localize(localizer, LocalizationKeys.AuthInvalidResetCode, "The code is invalid or has expired. Request a new one."));

        if (snapshot is null || !snapshot.User.IsActive)
        {
            return invalid;
        }

        var user = snapshot.User;

        var valid = await userManager
            .VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, PhoneReset.Purpose, request.Code.Trim())
            .ConfigureAwait(false);

        if (!valid)
        {
            return invalid;
        }

        // Same unit of work as the e-mail reset: new hash, cleared lockout and every session
        // revoked together. The security stamp is rotated as well, which invalidates the code
        // (and any other outstanding token) the moment it has been used once.
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordChangedAt = dateTime.UtcNow;
        user.MustChangePassword = false;
        user.AccessFailedCount = 0;
        user.LockedOutUntil = null;

        await authentication.RevokeAllForUserAsync(user.Id, "PasswordReset", cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(PhoneReset.Localize(
            localizer, LocalizationKeys.AuthPasswordResetDone, "Your password has been reset. Please sign in."));
    }
}

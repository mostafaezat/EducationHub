using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Auth.Commands;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Auth.Validators;

public sealed class LoginRequestValidator : LocalizedValidator<DTOs.LoginRequest>
{
    public LoginRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."));

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.PasswordRequired, "Password is required."))
            .MaximumLength(128).WithMessage(_ => Message(LocalizationKeys.PasswordMaxLength, "Password must not exceed 128 characters."));
    }
}

public sealed class RefreshTokenRequestValidator : LocalizedValidator<DTOs.RefreshTokenRequest>
{
    public RefreshTokenRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.RefreshTokenRequired, "A refresh token is required."));
    }
}

public sealed class ChangePasswordRequestValidator : LocalizedValidator<DTOs.ChangePasswordRequest>
{
    /// <summary>Kept in step with the Identity password options configured at startup.</summary>
    private const int MinimumLength = 8;

    public ChangePasswordRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.CurrentPasswordRequired, "The current password is required."));

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NewPasswordRequired, "The new password is required."))
            .MinimumLength(MinimumLength)
            .WithMessage(_ => Message(LocalizationKeys.NewPasswordMinLength, $"The new password must be at least {MinimumLength} characters."))
            .Matches("[A-Za-z]")
            .WithMessage(_ => Message(LocalizationKeys.NewPasswordLetter, "The new password must contain at least one letter."))
            .Matches("[0-9]")
            .WithMessage(_ => Message(LocalizationKeys.NewPasswordDigit, "The new password must contain at least one digit."));

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword)
            .WithMessage(_ => Message(LocalizationKeys.PasswordMismatch, "The new password and its confirmation do not match."));
    }
}

public sealed class LogoutRequestValidator : LocalizedValidator<DTOs.LogoutRequest>
{
    public LogoutRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        // RefreshToken is optional: logging out with no active session is a valid no-op.
    }
}

// The validators above target the request DTOs. The pipeline validates the command that
// MediatR receives, so each command needs a validator that composes the matching DTO
// validator. Without these, IValidator<TCommand> resolves to nothing and validation is
// skipped entirely (the defect recorded as P0-1 in the audit).
//
// RuleFor(x => x.Request).SetValidator(...) is null-safe: FluentValidation's child adaptor
// skips a null value, and the separate NotNull rule reports the missing body.

public sealed class LoginCommandValidator : LocalizedValidator<LoginCommand>
{
    public LoginCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new LoginRequestValidator(localizer));
    }
}

public sealed class RefreshTokenCommandValidator : LocalizedValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new RefreshTokenRequestValidator(localizer));
    }
}

public sealed class ChangePasswordCommandValidator : LocalizedValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new ChangePasswordRequestValidator(localizer));
    }
}

public sealed class LogoutCommandValidator : LocalizedValidator<LogoutCommand>
{
    public LogoutCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new LogoutRequestValidator(localizer));
    }
}

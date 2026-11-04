using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Auth.Commands;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Auth.Validators;

public sealed class ForgotPasswordRequestValidator : LocalizedValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."));
    }
}

public sealed class ResetPasswordRequestValidator : LocalizedValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."));

        RuleFor(x => x.ResetToken)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.ResetTokenRequired, "The reset token is required."));

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NewPasswordRequiredSimple, "A new password is required."))
            .MinimumLength(8).WithMessage(_ => Message(LocalizationKeys.PasswordMinLengthSimple, "The password must be at least 8 characters."))
            .Must(p => p.Any(char.IsDigit)).WithMessage(_ => Message(LocalizationKeys.PasswordDigitSimple, "The password must contain a digit."))
            .Must(p => p.Any(char.IsLower)).WithMessage(_ => Message(LocalizationKeys.PasswordLowerSimple, "The password must contain a lowercase letter."));

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage(_ => Message(LocalizationKeys.PasswordMismatchSimple, "The passwords do not match."));
    }
}

public sealed class ConfirmEmailRequestValidator : LocalizedValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."));

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.ConfirmationTokenRequired, "The confirmation token is required."));
    }
}

public sealed class ResendConfirmationRequestValidator : LocalizedValidator<ResendConfirmationRequest>
{
    public ResendConfirmationRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."));
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class ForgotPasswordCommandValidator : LocalizedValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new ForgotPasswordRequestValidator(localizer));
    }
}

public sealed class ResetPasswordCommandValidator : LocalizedValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new ResetPasswordRequestValidator(localizer));
    }
}

public sealed class ConfirmEmailCommandValidator : LocalizedValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new ConfirmEmailRequestValidator(localizer));
    }
}

public sealed class ResendConfirmationCommandValidator : LocalizedValidator<ResendConfirmationCommand>
{
    public ResendConfirmationCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new ResendConfirmationRequestValidator(localizer));
    }
}
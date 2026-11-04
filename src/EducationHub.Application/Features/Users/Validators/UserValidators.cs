using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Application.Features.Users.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Users.Validators;

public sealed class CreateUserRequestValidator : LocalizedValidator<CreateUserRequest>
{
    public CreateUserRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.FullNameRequired, "Full name is required."))
            .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."));

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
            .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."));

        // Mirrors the Identity password options. ASP.NET Core Identity re-checks this on
        // creation, so a mismatch here would surface as an opaque server-side failure
        // instead of a clear field error.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.PasswordRequired, "Password is required."))
            .MinimumLength(8).WithMessage(_ => Message(LocalizationKeys.PasswordMinLength8, "Password must be at least 8 characters."))
            .MaximumLength(128).WithMessage(_ => Message(LocalizationKeys.PasswordMaxLength, "Password must not exceed 128 characters."))
            .Matches("[A-Za-z]").WithMessage(_ => Message(LocalizationKeys.PasswordLetter, "Password must contain at least one letter."))
            .Matches("[0-9]").WithMessage(_ => Message(LocalizationKeys.PasswordDigit, "Password must contain at least one digit."));

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30);

        RuleFor(x => x.PreferredLanguage)
            .Must(l => string.IsNullOrWhiteSpace(l) || Shared.Common.Localization.SupportedLanguages.IsSupported(l))
            .WithMessage(_ => Message(LocalizationKeys.PreferredLanguage, "Preferred language must be 'ar' or 'en'."));

        RuleFor(x => x.Roles)
            .NotNull().WithMessage(_ => Message(LocalizationKeys.RoleRequired, "At least one role must be supplied."))
            .Must(r => r.Count > 0).WithMessage(_ => Message(LocalizationKeys.RoleRequired, "At least one role must be supplied."))
            .Must(r => r.Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Count)
            .WithMessage(_ => Message(LocalizationKeys.RoleDuplicate, "The role list must not contain duplicates."));
    }
}

public sealed class UpdateUserRequestValidator : LocalizedValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.FullNameBlank, "Full name cannot be blank."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."))
            .When(x => x.FullName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(x => x.PhoneNumber is not null);

        RuleFor(x => x.PreferredLanguage)
            .Must(l => string.IsNullOrWhiteSpace(l) || Shared.Common.Localization.SupportedLanguages.IsSupported(l))
            .WithMessage(_ => Message(LocalizationKeys.PreferredLanguage, "Preferred language must be 'ar' or 'en'."))
            .When(x => x.PreferredLanguage is not null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(x => x.Notes is not null);
    }
}

public sealed class AdminResetPasswordRequestValidator : LocalizedValidator<AdminResetPasswordRequest>
{
    public AdminResetPasswordRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NewPasswordRequired, "The new password is required."))
            .MinimumLength(8).WithMessage(_ => Message(LocalizationKeys.PasswordMinLength8, "Password must be at least 8 characters."))
            .MaximumLength(128).WithMessage(_ => Message(LocalizationKeys.PasswordMaxLength, "Password must not exceed 128 characters."))
            .Matches("[A-Za-z]").WithMessage(_ => Message(LocalizationKeys.PasswordLetter, "Password must contain at least one letter."))
            .Matches("[0-9]").WithMessage(_ => Message(LocalizationKeys.PasswordDigit, "Password must contain at least one digit."));
    }
}

public sealed class AssignRolesRequestValidator : LocalizedValidator<AssignRolesRequest>
{
    public AssignRolesRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Roles)
            .NotNull().WithMessage(_ => Message(LocalizationKeys.RoleListRequired, "The role list is required."))
            .Must(r => r.Count > 0).WithMessage(_ => Message(LocalizationKeys.RoleAssigned, "At least one role must be assigned."))
            .Must(r => r.Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Count)
            .WithMessage(_ => Message(LocalizationKeys.RoleDuplicate, "The role list must not contain duplicates."));

        RuleForEach(x => x.Roles)
            .MaximumLength(100)
            .Must(r => !string.IsNullOrWhiteSpace(r))
            .WithMessage(_ => Message(LocalizationKeys.RoleNameBlank, "A role name cannot be blank."));
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class CreateUserCommandValidator : LocalizedValidator<CreateUserCommand>
{
    public CreateUserCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateUserRequestValidator(localizer));
    }
}

public sealed class UpdateUserCommandValidator : LocalizedValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateUserRequestValidator(localizer));
    }
}

public sealed class ResetPasswordCommandValidator : LocalizedValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new AdminResetPasswordRequestValidator(localizer));
    }
}

public sealed class AssignRolesCommandValidator : LocalizedValidator<AssignRolesCommand>
{
    public AssignRolesCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new AssignRolesRequestValidator(localizer));
    }
}
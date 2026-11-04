using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Teachers.Commands;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Teachers.Validators;

/// <summary>
/// Enforces the create-or-link split: a brand-new account needs e-mail, password and name;
/// linking an existing one takes <c>UserId</c> and nothing that belongs to a new account.
/// </summary>
public sealed class CreateTeacherRequestValidator : LocalizedValidator<CreateTeacherRequest>
{
    public CreateTeacherRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.EmployeeNumber)
            .MaximumLength(50)
            .When(static x => x.EmployeeNumber is not null);

        RuleFor(x => x.SubjectIds)
            .Must(static ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage(_ => Message(LocalizationKeys.SubjectDuplicate, "The subject list must not contain duplicates."));

        When(
            static x => x.UserId is null,
            () =>
            {
                // Mirrors the Identity password options, exactly as CreateUserRequestValidator
                // does: Identity re-checks on creation, and a mismatch here would surface as
                // an opaque server-side failure instead of a clear field error.
                RuleFor(x => x.Email)
                    .NotEmpty().WithMessage(_ => Message(LocalizationKeys.EmailRequired, "Email is required."))
                    .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
                    .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."));

                RuleFor(x => x.Password)
                    .NotEmpty().WithMessage(_ => Message(LocalizationKeys.PasswordRequired, "Password is required."))
                    .MinimumLength(8).WithMessage(_ => Message(LocalizationKeys.PasswordMinLength8, "Password must be at least 8 characters."))
                    .MaximumLength(128).WithMessage(_ => Message(LocalizationKeys.PasswordMaxLength, "Password must not exceed 128 characters."))
                    .Matches("[A-Za-z]").WithMessage(_ => Message(LocalizationKeys.PasswordLetter, "Password must contain at least one letter."))
                    .Matches("[0-9]").WithMessage(_ => Message(LocalizationKeys.PasswordDigit, "Password must contain at least one digit."));

                RuleFor(x => x.FullName)
                    .NotEmpty().WithMessage(_ => Message(LocalizationKeys.FullNameRequired, "Full name is required."))
                    .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
                    .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."));
            });

        Unless(
            static x => x.UserId is null,
            () =>
            {
                RuleFor(x => x.Email)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.TeacherLinkConflicts,
                        "Email, password and full name cannot be supplied when linking an existing user account."));

                RuleFor(x => x.Password)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.TeacherLinkConflicts,
                        "Email, password and full name cannot be supplied when linking an existing user account."));

                RuleFor(x => x.FullName)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.TeacherLinkConflicts,
                        "Email, password and full name cannot be supplied when linking an existing user account."));
            });

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(static x => x.PhoneNumber is not null);
    }
}

public sealed class UpdateTeacherRequestValidator : LocalizedValidator<UpdateTeacherRequest>
{
    public UpdateTeacherRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.EmployeeNumber)
            .MaximumLength(50)
            .When(static x => x.EmployeeNumber is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(static x => x.PhoneNumber is not null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(static x => x.Notes is not null);
    }
}

public sealed class AssignTeacherSubjectsRequestValidator : LocalizedValidator<AssignTeacherSubjectsRequest>
{
    public AssignTeacherSubjectsRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.SubjectIds)
            .NotNull().WithMessage(_ => Message(LocalizationKeys.SubjectListRequired, "The subject list is required."))
            .Must(static ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage(_ => Message(LocalizationKeys.SubjectDuplicate, "The subject list must not contain duplicates."));
    }
}

public sealed class CreateCompensationRuleRequestValidator : LocalizedValidator<CreateCompensationRuleRequest>
{
    public CreateCompensationRuleRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        // All-null scope is valid: it creates the tenant default rule (precedence level 6).
        // The handler rejects a second rule with the same scope.
        RuleFor(x => x.Rate)
            .GreaterThanOrEqualTo(0).WithMessage(_ => Message(LocalizationKeys.RateNonNegative, "Rate cannot be negative."));
    }
}

public sealed class UpdateCompensationRuleRequestValidator : LocalizedValidator<UpdateCompensationRuleRequest>
{
    public UpdateCompensationRuleRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Rate)
            .GreaterThanOrEqualTo(0).WithMessage(_ => Message(LocalizationKeys.RateNonNegative, "Rate cannot be negative."))
            .When(static x => x.Rate.HasValue);
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class CreateTeacherCommandValidator : LocalizedValidator<CreateTeacherCommand>
{
    public CreateTeacherCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateTeacherRequestValidator(localizer));
    }
}

public sealed class UpdateTeacherCommandValidator : LocalizedValidator<UpdateTeacherCommand>
{
    public UpdateTeacherCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateTeacherRequestValidator(localizer));
    }
}

public sealed class AssignTeacherSubjectsCommandValidator : LocalizedValidator<AssignTeacherSubjectsCommand>
{
    public AssignTeacherSubjectsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new AssignTeacherSubjectsRequestValidator(localizer));
    }
}

public sealed class CreateCompensationRuleCommandValidator : LocalizedValidator<CreateCompensationRuleCommand>
{
    public CreateCompensationRuleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateCompensationRuleRequestValidator(localizer));
    }
}

public sealed class UpdateCompensationRuleCommandValidator : LocalizedValidator<UpdateCompensationRuleCommand>
{
    public UpdateCompensationRuleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateCompensationRuleRequestValidator(localizer));
    }
}

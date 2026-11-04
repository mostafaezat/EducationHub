using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Students.Commands;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Students.Validators;

/// <summary>Password rules shared by the student and parent account modes.</summary>
internal static class AccountPasswordRules
{
    // Mirrors the Identity password options exactly as the teacher and user validators do:
    // Identity re-checks on creation, and a mismatch would surface as an opaque failure.
    public static void Apply<T>(
        IRuleBuilderInitial<T, string?> rule,
        Func<string, string, string> message)
        => rule
            .NotEmpty().WithMessage(_ => message(LocalizationKeys.PasswordRequired, "Password is required."))
            .MinimumLength(8).WithMessage(_ => message(LocalizationKeys.PasswordMinLength8, "Password must be at least 8 characters."))
            .MaximumLength(128).WithMessage(_ => message(LocalizationKeys.PasswordMaxLength, "Password must not exceed 128 characters."))
            .Matches("[A-Za-z]").WithMessage(_ => message(LocalizationKeys.PasswordLetter, "Password must contain at least one letter."))
            .Matches("[0-9]").WithMessage(_ => message(LocalizationKeys.PasswordDigit, "Password must contain at least one digit."));
}

/// <summary>
/// Name rules plus the three account modes: none, link (<c>UserId</c>) or create
/// (<c>AccountEmail</c> + <c>AccountPassword</c>) - never link and create together.
/// </summary>
public sealed class CreateStudentRequestValidator : LocalizedValidator<CreateStudentRequest>
{
    public CreateStudentRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.FullNameRequired, "Full name is required."))
            .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."));

        RuleFor(x => x.StudentNumber)
            .MaximumLength(50)
            .When(static x => x.StudentNumber is not null);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(static x => x.Notes is not null);

        When(
            static x => x.UserId is not null,
            () =>
            {
                RuleFor(x => x.AccountEmail)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.ProfileLinkConflicts,
                        "Account e-mail and password cannot be supplied when linking an existing user account."));

                RuleFor(x => x.AccountPassword)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.ProfileLinkConflicts,
                        "Account e-mail and password cannot be supplied when linking an existing user account."));
            });

        When(
            static x => x.UserId is null && !string.IsNullOrWhiteSpace(x.AccountEmail),
            () =>
            {
                RuleFor(x => x.AccountEmail)
                    .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
                    .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."));

                AccountPasswordRules.Apply(RuleFor(x => x.AccountPassword), Message);
            });

        When(
            static x => x.UserId is null
                && string.IsNullOrWhiteSpace(x.AccountEmail)
                && !string.IsNullOrWhiteSpace(x.AccountPassword),
            () =>
            {
                RuleFor(x => x.AccountEmail)
                    .NotEmpty().WithMessage(_ => Message(LocalizationKeys.AccountEmailRequired,
                        "An e-mail address is required to create a sign-in account."));
            });
    }
}

public sealed class UpdateStudentRequestValidator : LocalizedValidator<UpdateStudentRequest>
{
    public UpdateStudentRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."))
            .When(static x => x.FullName is not null);

        RuleFor(x => x.StudentNumber)
            .MaximumLength(50)
            .When(static x => x.StudentNumber is not null);

        RuleFor(x => x.Status)
            .IsInEnum()
            .When(static x => x.Status.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(static x => x.Notes is not null);
    }
}

public sealed class SetStudentGuardiansRequestValidator : LocalizedValidator<SetStudentGuardiansRequest>
{
    public SetStudentGuardiansRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Guardians)
            .NotNull().WithMessage(_ => Message(LocalizationKeys.GuardianListRequired, "The guardian list is required."))
            .Must(static list => list is null
                || list.Select(static g => g.ParentProfileId).Distinct().Count() == list.Count)
            .WithMessage(_ => Message(LocalizationKeys.GuardianDuplicate, "A parent can appear only once in the guardian list."))
            .Must(static list => list is null || list.Count(static g => g.IsPrimary) <= 1)
            .WithMessage(_ => Message(LocalizationKeys.GuardianPrimaryMultiple, "Only one guardian can be the primary contact."));

        RuleForEach(x => x.Guardians).ChildRules(static guardian =>
        {
            guardian.RuleFor(g => g.ParentProfileId).NotEmpty();
            guardian.RuleFor(g => g.Relationship).IsInEnum();
        });
    }
}

/// <summary>
/// Name/contact rules plus the account modes. A new parent account signs in with the
/// parent's <c>Email</c>, so that e-mail becomes required once a password is supplied.
/// </summary>
public sealed class CreateParentRequestValidator : LocalizedValidator<CreateParentRequest>
{
    public CreateParentRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.FullNameRequired, "Full name is required."))
            .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."));

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(static x => x.PhoneNumber is not null);

        RuleFor(x => x.Email)
            .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(static x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(static x => x.Notes is not null);

        When(
            static x => x.UserId is not null,
            () =>
            {
                RuleFor(x => x.AccountPassword)
                    .Empty().WithMessage(_ => Message(LocalizationKeys.ProfileLinkConflicts,
                        "Account e-mail and password cannot be supplied when linking an existing user account."));
            });

        When(
            static x => x.UserId is null && !string.IsNullOrWhiteSpace(x.AccountPassword),
            () =>
            {
                RuleFor(x => x.Email)
                    .NotEmpty().WithMessage(_ => Message(LocalizationKeys.AccountEmailRequired,
                        "An e-mail address is required to create a sign-in account."));

                AccountPasswordRules.Apply(RuleFor(x => x.AccountPassword), Message);
            });
    }
}

public sealed class UpdateParentRequestValidator : LocalizedValidator<UpdateParentRequest>
{
    public UpdateParentRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.FullName)
            .MinimumLength(3).WithMessage(_ => Message(LocalizationKeys.FullNameMinLength3, "Full name must be at least 3 characters."))
            .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.FullNameMaxLength200, "Full name must not exceed 200 characters."))
            .When(static x => x.FullName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30)
            .When(static x => x.PhoneNumber is not null);

        RuleFor(x => x.Email)
            .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailMaxLength, "Email must not exceed 256 characters."))
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(static x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Notes)
            .MaximumLength(1000)
            .When(static x => x.Notes is not null);
    }
}

// Command-level validators so the DTO rules run through the MediatR pipeline (audit P0-1).

public sealed class CreateStudentCommandValidator : LocalizedValidator<CreateStudentCommand>
{
    public CreateStudentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateStudentRequestValidator(localizer));
    }
}

public sealed class UpdateStudentCommandValidator : LocalizedValidator<UpdateStudentCommand>
{
    public UpdateStudentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateStudentRequestValidator(localizer));
    }
}

public sealed class SetStudentGuardiansCommandValidator : LocalizedValidator<SetStudentGuardiansCommand>
{
    public SetStudentGuardiansCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new SetStudentGuardiansRequestValidator(localizer));
    }
}

public sealed class CreateParentCommandValidator : LocalizedValidator<CreateParentCommand>
{
    public CreateParentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateParentRequestValidator(localizer));
    }
}

public sealed class UpdateParentCommandValidator : LocalizedValidator<UpdateParentCommand>
{
    public UpdateParentCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateParentRequestValidator(localizer));
    }
}

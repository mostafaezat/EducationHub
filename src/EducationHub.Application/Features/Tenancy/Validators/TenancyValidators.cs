using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Tenancy.Validators;

/// <summary>Field limits shared by create and update, so the two cannot drift apart.</summary>
internal static class TenancyValidationRules
{
    public const int MaxNameLength = 200;

    public const int MaxEmailLength = 256;

    /// <summary>Letters, numbers, spaces and a few name punctuation marks, in any script.</summary>
    public const string NamePattern = @"^[\p{L}\p{N}\s\.\-&']+$";

    /// <summary>Unicode letters and digits, so Arabic and English names both validate.</summary>
    public static bool HasValidNameCharacters(string? value)
        => string.IsNullOrWhiteSpace(value)
           || System.Text.RegularExpressions.Regex.IsMatch(value, NamePattern);
}

public sealed class CreateTenantRequestValidator : LocalizedValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.TenantNameRequired, "Tenant name is required."))
            .MaximumLength(TenancyValidationRules.MaxNameLength)
            .WithMessage(_ => Message(LocalizationKeys.TenantNameMaxLength200, $"Tenant name must not exceed {TenancyValidationRules.MaxNameLength} characters."))
            .Must(TenancyValidationRules.HasValidNameCharacters)
            .WithMessage(_ => Message(LocalizationKeys.TenantNameChars, "Tenant name may contain letters, numbers, spaces and . - & ' only."));

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.SlugRequired, "Slug is required."))
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.SlugMaxLength100, "Slug must not exceed 100 characters."))
            .Matches("^[a-zA-Z0-9\\-_]+$")
            .WithMessage(_ => Message(LocalizationKeys.SlugChars, "Slug may contain letters, digits, hyphens and underscores only."));

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Currency)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage(_ => Message(LocalizationKeys.Currency, "Currency must be a 3-letter ISO 4217 code, for example EGP."));

        RuleFor(x => x.TimeZone)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.TimeZoneRequired, "Time zone is required."))
            .MaximumLength(100);

        RuleFor(x => x.DefaultLanguage)
            .Must(SupportedLanguages.IsSupported)
            .WithMessage(_ => Message(LocalizationKeys.DefaultLanguage, "Default language must be 'ar' or 'en'."));

        RuleFor(x => x.Phone)
            .MaximumLength(30);

        RuleFor(x => x.AdminFullName)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.AdminFullNameRequired, "Admin full name is required."))
            .MaximumLength(TenancyValidationRules.MaxNameLength)
            .Must(TenancyValidationRules.HasValidNameCharacters)
            .WithMessage(_ => Message(LocalizationKeys.AdminFullNameChars, "Admin full name may contain letters, numbers, spaces and . - & ' only."));

        // Mirrors the Identity password options and the user-creation validator.
        // ASP.NET Core Identity re-checks this when the account is created, so these rules
        // are convenience, but a mismatch would surface as a confusing Identity error at
        // provisioning time rather than a clean 400 here.
        RuleFor(x => x.AdminEmail)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.AdminEmailRequired, "Admin email is required."))
            .MaximumLength(TenancyValidationRules.MaxEmailLength)
            .WithMessage(_ => Message(LocalizationKeys.AdminEmailMaxLength256, $"Admin email must not exceed {TenancyValidationRules.MaxEmailLength} characters."))
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.AdminEmailInvalid, "Admin email is not a valid address."));

        RuleFor(x => x.AdminPassword)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.AdminPasswordRequired, "Admin password is required."))
            .MinimumLength(8).WithMessage(_ => Message(LocalizationKeys.AdminPasswordMinLength8, "Admin password must be at least 8 characters."))
            .MaximumLength(128).WithMessage(_ => Message(LocalizationKeys.AdminPasswordMaxLength128, "Admin password must not exceed 128 characters."))
            .Matches("[A-Za-z]").WithMessage(_ => Message(LocalizationKeys.AdminPasswordLetter, "Admin password must contain at least one letter."))
            .Matches("[0-9]").WithMessage(_ => Message(LocalizationKeys.AdminPasswordDigit, "Admin password must contain at least one digit."));
    }
}

public sealed class UpdateTenantRequestValidator : LocalizedValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.TenantNameBlank, "Tenant name cannot be blank."))
            .MaximumLength(TenancyValidationRules.MaxNameLength)
            .Must(TenancyValidationRules.HasValidNameCharacters)
            .WithMessage(_ => Message(LocalizationKeys.TenantNameChars, "Tenant name may contain letters, numbers, spaces and . - & ' only."))
            .When(x => x.Name is not null);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(x => x.Email is not null);

        RuleFor(x => x.Currency)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage(_ => Message(LocalizationKeys.Currency, "Currency must be a 3-letter ISO 4217 code, for example EGP."))
            .When(x => x.Currency is not null);

        RuleFor(x => x.DefaultLanguage)
            .Must(SupportedLanguages.IsSupported)
            .WithMessage(_ => Message(LocalizationKeys.DefaultLanguage, "Default language must be 'ar' or 'en'."))
            .When(x => x.DefaultLanguage is not null);

        // Cross-field rule: an inverted subscription range is a client error, caught here
        // rather than surfacing as a thrown exception from the handler.
        RuleFor(x => x)
            .Must((request, _, context) =>
            {
                if (request.SubscriptionStartDate.HasValue
                    && request.SubscriptionExpiryDate.HasValue
                    && request.SubscriptionExpiryDate < request.SubscriptionStartDate)
                {
                    context.AddFailure(Message(LocalizationKeys.SubscriptionRange, "The subscription expiry date cannot be earlier than its start date."));
                }

                return true;
            });
    }
}

public sealed class CreateBranchRequestValidator : LocalizedValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.BranchNameRequired, "Branch name is required."))
            .MaximumLength(TenancyValidationRules.MaxNameLength)
            .WithMessage(_ => Message(LocalizationKeys.BranchNameMaxLength200, $"Branch name must not exceed {TenancyValidationRules.MaxNameLength} characters."))
            .Must(TenancyValidationRules.HasValidNameCharacters)
            .WithMessage(_ => Message(LocalizationKeys.BranchNameChars, "Branch name may contain letters, numbers, spaces and . - & ' only."));

        RuleFor(x => x.Code)
            .MaximumLength(50)
            .When(x => x.Code is not null);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class UpdateBranchRequestValidator : LocalizedValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.BranchNameBlank, "Branch name cannot be blank."))
            .MaximumLength(TenancyValidationRules.MaxNameLength)
            .Must(TenancyValidationRules.HasValidNameCharacters)
            .WithMessage(_ => Message(LocalizationKeys.BranchNameChars, "Branch name may contain letters, numbers, spaces and . - & ' only."))
            .When(x => x.Name is not null);

        RuleFor(x => x.Code)
            .MaximumLength(50)
            .When(x => x.Code is not null);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailInvalid, "Email is not a valid address."))
            .When(x => x.Email is not null);
    }
}

public sealed class UpdateTenantSettingsRequestValidator : LocalizedValidator<UpdateTenantSettingsRequest>
{
    public UpdateTenantSettingsRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        // These values drive money and attendance calculations, so an out-of-range value is
        // rejected up front instead of producing quietly nonsensical business results later.
        RuleFor(x => x.LateThresholdMinutes)
            .InclusiveBetween(0, 240)
            .When(x => x.LateThresholdMinutes.HasValue)
            .WithMessage(_ => Message(LocalizationKeys.LateThreshold, "Late threshold must be between 0 and 240 minutes."));

        RuleFor(x => x.FeeDueDayOfMonth)
            .InclusiveBetween(1, 28)
            .When(x => x.FeeDueDayOfMonth.HasValue)
            .WithMessage(_ => Message(LocalizationKeys.FeeDueDay, "Fee due day must be between 1 and 28."));

        RuleFor(x => x.GracePeriodDays)
            .InclusiveBetween(0, 90)
            .When(x => x.GracePeriodDays.HasValue)
            .WithMessage(_ => Message(LocalizationKeys.GracePeriod, "Grace period must be between 0 and 90 days."));

        RuleFor(x => x.SiblingDiscountPercentage)
            .InclusiveBetween(0, 100)
            .When(x => x.SiblingDiscountPercentage.HasValue)
            .WithMessage(_ => Message(LocalizationKeys.SiblingDiscount, "Sibling discount must be between 0 and 100 percent."));

        RuleFor(x => x.DefaultPassingPercentage)
            .InclusiveBetween(0, 100)
            .When(x => x.DefaultPassingPercentage.HasValue)
            .WithMessage(_ => Message(LocalizationKeys.PassingPercentage, "Passing percentage must be between 0 and 100."));
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class CreateTenantCommandValidator : LocalizedValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateTenantRequestValidator(localizer));
    }
}

public sealed class UpdateTenantCommandValidator : LocalizedValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateTenantRequestValidator(localizer));
    }
}

public sealed class CreateBranchCommandValidator : LocalizedValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateBranchRequestValidator(localizer));
    }
}

public sealed class UpdateBranchCommandValidator : LocalizedValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateBranchRequestValidator(localizer));
    }
}

public sealed class UpdateTenantSettingsCommandValidator : LocalizedValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateTenantSettingsRequestValidator(localizer));
    }
}
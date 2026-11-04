using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Academic.Commands;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Academic.Validators;

public sealed class CreateAcademicYearRequestValidator : LocalizedValidator<CreateAcademicYearRequest>
{
    public CreateAcademicYearRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NameRequired, "Name is required."))
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."));

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."));

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage(_ => Message(LocalizationKeys.DateRange, "The end date cannot be earlier than the start date."));
    }
}

public sealed class UpdateAcademicYearRequestValidator : LocalizedValidator<UpdateAcademicYearRequest>
{
    public UpdateAcademicYearRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."))
            .When(static x => x.Name is not null);

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."))
            .When(static x => x.NameAr is not null);

        // Only checked when both dates are present. A partial update leaves the other date
        // to come from the stored row, and the handler re-validates the merged pair.
        RuleFor(x => x)
            .Must(x => x.StartDate is null || x.EndDate is null || x.EndDate >= x.StartDate)
            .WithMessage(_ => Message(LocalizationKeys.DateRange, "The end date cannot be earlier than the start date."));
    }
}

public sealed class CreateGradeLevelRequestValidator : LocalizedValidator<CreateGradeLevelRequest>
{
    public CreateGradeLevelRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.CodeRequired, "Code is required."))
            .MaximumLength(20).WithMessage(_ => Message(LocalizationKeys.CodeMaxLength20, "Code must not exceed 20 characters."));

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NameRequired, "Name is required."))
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."));

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."));

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."));

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."));

        RuleFor(x => x.Order)
            .GreaterThanOrEqualTo(0).WithMessage(_ => Message(LocalizationKeys.OrderNonNegative, "Order cannot be negative."));
    }
}

public sealed class UpdateGradeLevelRequestValidator : LocalizedValidator<UpdateGradeLevelRequest>
{
    public UpdateGradeLevelRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."))
            .When(static x => x.Name is not null);

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."))
            .When(static x => x.NameAr is not null);

        RuleFor(x => x.Order)
            .GreaterThanOrEqualTo(0).WithMessage(_ => Message(LocalizationKeys.OrderNonNegative, "Order cannot be negative."))
            .When(static x => x.Order.HasValue);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."))
            .When(static x => x.Description is not null);

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."))
            .When(static x => x.DescriptionAr is not null);
    }
}

public sealed class CreateSubjectRequestValidator : LocalizedValidator<CreateSubjectRequest>
{
    public CreateSubjectRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.CodeRequired, "Code is required."))
            .MaximumLength(20).WithMessage(_ => Message(LocalizationKeys.CodeMaxLength20, "Code must not exceed 20 characters."));

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.NameRequired, "Name is required."))
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."));

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."));

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."));

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."));
    }
}

public sealed class UpdateSubjectRequestValidator : LocalizedValidator<UpdateSubjectRequest>
{
    public UpdateSubjectRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameMaxLength100, "Name must not exceed 100 characters."))
            .When(static x => x.Name is not null);

        RuleFor(x => x.NameAr)
            .MaximumLength(100).WithMessage(_ => Message(LocalizationKeys.NameArMaxLength100, "Arabic name must not exceed 100 characters."))
            .When(static x => x.NameAr is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."))
            .When(static x => x.Description is not null);

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."))
            .When(static x => x.DescriptionAr is not null);
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class CreateAcademicYearCommandValidator : LocalizedValidator<CreateAcademicYearCommand>
{
    public CreateAcademicYearCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateAcademicYearRequestValidator(localizer));
    }
}

public sealed class UpdateAcademicYearCommandValidator : LocalizedValidator<UpdateAcademicYearCommand>
{
    public UpdateAcademicYearCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateAcademicYearRequestValidator(localizer));
    }
}

public sealed class CreateGradeLevelCommandValidator : LocalizedValidator<CreateGradeLevelCommand>
{
    public CreateGradeLevelCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateGradeLevelRequestValidator(localizer));
    }
}

public sealed class UpdateGradeLevelCommandValidator : LocalizedValidator<UpdateGradeLevelCommand>
{
    public UpdateGradeLevelCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateGradeLevelRequestValidator(localizer));
    }
}

public sealed class CreateSubjectCommandValidator : LocalizedValidator<CreateSubjectCommand>
{
    public CreateSubjectCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateSubjectRequestValidator(localizer));
    }
}

public sealed class UpdateSubjectCommandValidator : LocalizedValidator<UpdateSubjectCommand>
{
    public UpdateSubjectCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateSubjectRequestValidator(localizer));
    }
}
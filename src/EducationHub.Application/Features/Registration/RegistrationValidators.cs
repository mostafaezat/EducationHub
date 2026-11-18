using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Validation;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Registration;

public sealed class SubmitRegistrationCommandValidator : LocalizedValidator<SubmitRegistrationCommand>
{
    public SubmitRegistrationCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));

        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.GuardianName)
                .NotEmpty().WithMessage(_ => Message(LocalizationKeys.RegistrationGuardianNameRequired, "The guardian's name is required."))
                .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.RegistrationNameTooLong, "Names must not exceed 200 characters."));

            RuleFor(x => x.Request.GuardianPhone)
                .NotEmpty().WithMessage(_ => Message(LocalizationKeys.PhoneRequired, "A mobile number is required."))
                .MaximumLength(32).WithMessage(_ => Message(LocalizationKeys.PhoneInvalid, "Enter a valid mobile number."))
                .Must(PhoneNumberFormat.LooksLikePhone).WithMessage(_ => Message(LocalizationKeys.PhoneInvalid, "Enter a valid mobile number."));

            RuleFor(x => x.Request.GuardianEmail)
                .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."))
                .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."))
                .When(x => !string.IsNullOrWhiteSpace(x.Request.GuardianEmail));

            RuleFor(x => x.Request.Relationship)
                .IsInEnum().WithMessage(_ => Message(LocalizationKeys.RegistrationRelationshipInvalid, "Choose the guardian's relationship to the student."));

            RuleFor(x => x.Request.StudentName)
                .NotEmpty().WithMessage(_ => Message(LocalizationKeys.RegistrationStudentNameRequired, "The student's name is required."))
                .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.RegistrationNameTooLong, "Names must not exceed 200 characters."));

            RuleFor(x => x.Request.BirthDate)
                .Must(d => d is null || (d.Value <= DateOnly.FromDateTime(DateTime.UtcNow) && d.Value.Year >= 1950))
                .WithMessage(_ => Message(LocalizationKeys.RegistrationBirthDateInvalid, "Enter a valid date of birth."));

            RuleFor(x => x.Request.School)
                .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.RegistrationNameTooLong, "Names must not exceed 200 characters."));

            RuleFor(x => x.Request.SubjectIds)
                .Must(ids => ids is null || ids.Count <= 30)
                .WithMessage(_ => Message(LocalizationKeys.RegistrationTooManySubjects, "Too many subjects were chosen."));

            RuleFor(x => x.Request.Notes)
                .MaximumLength(1000).WithMessage(_ => Message(LocalizationKeys.RegistrationNotesTooLong, "Notes must not exceed 1000 characters."));
        });
    }
}

public sealed class ApproveRegistrationCommandValidator : LocalizedValidator<ApproveRegistrationCommand>
{
    public ApproveRegistrationCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));

        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.StudentName)
                .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.RegistrationNameTooLong, "Names must not exceed 200 characters."));

            RuleFor(x => x.Request.GuardianName)
                .MaximumLength(200).WithMessage(_ => Message(LocalizationKeys.RegistrationNameTooLong, "Names must not exceed 200 characters."));

            RuleFor(x => x.Request.GuardianPhone)
                .Must(PhoneNumberFormat.LooksLikePhone).WithMessage(_ => Message(LocalizationKeys.PhoneInvalid, "Enter a valid mobile number."))
                .When(x => !string.IsNullOrWhiteSpace(x.Request.GuardianPhone));

            RuleFor(x => x.Request.GuardianEmail)
                .EmailAddress().WithMessage(_ => Message(LocalizationKeys.EmailValidRequired, "A valid email address is required."))
                .When(x => !string.IsNullOrWhiteSpace(x.Request.GuardianEmail));

            RuleFor(x => x.Request.Relationship)
                .IsInEnum().WithMessage(_ => Message(LocalizationKeys.RegistrationRelationshipInvalid, "Choose the guardian's relationship to the student."))
                .When(x => x.Request.Relationship.HasValue);

            RuleFor(x => x.Request.Notes)
                .MaximumLength(1000).WithMessage(_ => Message(LocalizationKeys.RegistrationNotesTooLong, "Notes must not exceed 1000 characters."));
        });
    }
}

public sealed class RejectRegistrationCommandValidator : LocalizedValidator<RejectRegistrationCommand>
{
    public RejectRegistrationCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));

        RuleFor(x => x.Request.Reason)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.RegistrationRejectReasonRequired, "A reason is required to reject a request."))
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.RegistrationRejectReasonTooLong, "The reason must not exceed 500 characters."))
            .When(x => x.Request is not null);
    }
}

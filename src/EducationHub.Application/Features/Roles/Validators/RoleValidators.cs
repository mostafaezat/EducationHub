using EducationHub.Application.Common.Validation;
using EducationHub.Application.Features.Roles.Commands;
using EducationHub.Application.Features.Roles.DTOs;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;
using FluentValidation;
using Microsoft.Extensions.Localization;

namespace EducationHub.Application.Features.Roles.Validators;

public sealed class CreateRoleRequestValidator : LocalizedValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => Message(LocalizationKeys.RoleNameRequired, "Role name is required."))
            .MaximumLength(256).WithMessage(_ => Message(LocalizationKeys.RoleNameMaxLength256, "Role name must not exceed 256 characters."))
            // Identity's NormalizedName drives uniqueness, and whitespace would make two
            // visibly identical names normalize differently.
            .Must(n => !n.Contains(' ', StringComparison.Ordinal) || n.Trim() == n)
            .WithMessage(_ => Message(LocalizationKeys.RoleNameSpaces, "Role name must not contain spaces."))
            // A tenant-defined role may not shadow a system role name: the platform scope
            // check reads the role claim from the token, so a tenant role named PlatformAdmin
            // would claim platform privileges a tenant must never have.
            .Must(n => !RoleNames.IsTenantRole(n) && !RoleNames.IsPlatformRole(n))
            .WithMessage(_ => Message(LocalizationKeys.RoleNameReserved, "That role name is reserved and cannot be created."));

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."));

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."));

        RuleFor(x => x.Permissions)
            .Must(p => p is null || p.Distinct(StringComparer.OrdinalIgnoreCase).Count() == p.Count)
            .WithMessage(_ => Message(LocalizationKeys.PermissionDuplicate, "The permission list must not contain duplicates."))
            .When(static x => x.Permissions is not null);
    }
}

public sealed class UpdateRoleRequestValidator : LocalizedValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionMaxLength500, "Description must not exceed 500 characters."))
            .When(static x => x.Description is not null);

        RuleFor(x => x.DescriptionAr)
            .MaximumLength(500).WithMessage(_ => Message(LocalizationKeys.DescriptionArMaxLength500, "Arabic description must not exceed 500 characters."))
            .When(static x => x.DescriptionAr is not null);
    }
}

public sealed class AssignPermissionsRequestValidator : LocalizedValidator<AssignPermissionsRequest>
{
    public AssignPermissionsRequestValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        // An empty list is legal and means "strip every permission", which is how a role is
        // made harmless. Rejecting it would force callers through a different code path.
        RuleFor(x => x.Permissions)
            .NotNull().WithMessage(_ => Message(LocalizationKeys.PermissionListRequired, "The permission list is required."));

        RuleForEach(x => x.Permissions)
            .Must(p => !string.IsNullOrWhiteSpace(p))
            .WithMessage(_ => Message(LocalizationKeys.PermissionBlank, "A permission code cannot be blank."))
            .MaximumLength(100)
            .When(static x => x.Permissions is not null);
    }
}

// Command-level validators that make the DTO rules above run through the MediatR pipeline.
// See AuthValidators.cs for why these exist (audit defect P0-1).

public sealed class CreateRoleCommandValidator : LocalizedValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new CreateRoleRequestValidator(localizer));
    }
}

public sealed class UpdateRoleCommandValidator : LocalizedValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new UpdateRoleRequestValidator(localizer));
    }
}

public sealed class AssignPermissionsCommandValidator : LocalizedValidator<AssignPermissionsCommand>
{
    public AssignPermissionsCommandValidator(IStringLocalizer<LocalizationResource>? localizer = null)
        : base(localizer)
    {
        RuleFor(x => x.Request).NotNull().WithMessage(_ => Message(LocalizationKeys.RequestBodyRequired, "The request body is required."));
        RuleFor(x => x.Request).SetValidator(new AssignPermissionsRequestValidator(localizer));
    }
}
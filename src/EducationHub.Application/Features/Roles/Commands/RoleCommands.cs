using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Roles.Commands;
using EducationHub.Application.Features.Roles.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Roles.Commands;

/// <summary>
/// Creates a tenant-defined role.
/// </summary>
/// <remarks>
/// A role that did not exist when the tenant was provisioned is how a tenant extends the
/// system beyond its seven built-in roles, so this endpoint is deliberately open to
/// tenant administrators - but only up to their own authority.
/// </remarks>
public sealed record CreateRoleCommand(CreateRoleRequest Request) : IRequest<Result<RoleDto>>;

public sealed class CreateRoleCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader,
    ICurrentUser currentUser)
    : IRequestHandler<CreateRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RoleCommandSupport.RequireTenant(tenantContext);

        if (!RoleCommandSupport.TryNormalizePermissions(
                command.Request.Permissions,
                out var permissions,
                out var unknown))
        {
            return Result<RoleDto>.Failure(
                "Roles.UnknownPermission",
                $"'{unknown}' is not a known permission.");
        }

        var callerPermissions = await RoleCommandSupport
            .LoadCallerPermissionsAsync(accessReader, currentUser, cancellationToken)
            .ConfigureAwait(false);

        RoleCommandSupport.EnsureNotEscalating(permissions, callerPermissions);

        var name = command.Request.Name.Trim();

        // The validator reports a reserved name as a clean 400, but this check is the
        // authority: a handler can be invoked directly, and the platform-scope check trusts
        // the token's role claim. A tenant role named PlatformAdmin would otherwise inherit
        // the platform bypass, so the entire system-role catalog is reserved here.
        if (RoleNames.IsTenantRole(name) || RoleNames.IsPlatformRole(name))
        {
            return Result<RoleDto>.Failure(
                "Roles.ReservedName",
                $"The role name '{name}' is reserved and cannot be created.");
        }

        // Identity stores the normalized (upper-cased) name and enforces uniqueness on it,
        // but only per query-filtered set. Checking first turns a raw duplicate-key error
        // into a readable validation failure.
        var existing = await roleManager.FindByNameAsync(name).ConfigureAwait(false);

        if (existing is not null)
        {
            return Result<RoleDto>.Failure(
                "Roles.DuplicateName",
                $"A role named '{name}' already exists.");
        }

        var role = new ApplicationRole
        {
            // TenantId is stamped by the DbContext from the request tenant; leaving it empty
            // here is what lets the save-time rule do that assignment.
            Id = Guid.NewGuid(),
            Name = name,
            Description = command.Request.Description?.Trim(),
            DescriptionAr = command.Request.DescriptionAr?.Trim(),
            IsSystemRole = false,
            IsActive = true
        };

        var created = await roleManager.CreateAsync(role).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return Result<RoleDto>.Failure(
                Features.Users.Commands.UserCommandSupport.IdentityErrorsToErrors(created),
                "The role could not be created.");
        }

        if (permissions.Count > 0)
        {
            var assigned = await RoleCommandSupport
                .ReplacePermissionsAsync(roleManager, role, permissions, cancellationToken)
                .ConfigureAwait(false);

            if (!assigned.Succeeded)
            {
                return Result<RoleDto>.Failure(
                    Features.Users.Commands.UserCommandSupport.IdentityErrorsToErrors(assigned),
                    "The role was created but its permissions could not be assigned.");
            }
        }

        return Result<RoleDto>.Success(
            RoleDto.FromEntity(role, permissions, 0),
            "Role created successfully.");
    }
}

/// <summary>
/// Updates a role's metadata and active flag.
/// </summary>
/// <remarks>
/// The name is intentionally not editable. Authorization policies, the seed data and the
/// frontend all reference roles by name; renaming one silently re-points every check that
/// mentioned it at a role that no longer exists.
/// </remarks>
public sealed record UpdateRoleCommand(Guid Id, UpdateRoleRequest Request) : IRequest<Result<RoleDto>>;

public sealed class UpdateRoleCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<UpdateRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RoleCommandSupport.RequireTenant(tenantContext);

        var role = await RoleCommandSupport
            .FindForTenantAsync(roleManager, tenantContext, command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (command.Request.IsActive == false && role.IsSystemRole)
        {
            // Deactivating a system role would silently strip capability from every user who
            // holds it, so it is refused rather than merely discouraged.
            return Result<RoleDto>.Failure(
                "Roles.CannotDeactivateSystemRole",
                "A system role cannot be deactivated.");
        }

        if (command.Request.Description is not null)
        {
            role.Description = command.Request.Description.Trim();
        }

        if (command.Request.DescriptionAr is not null)
        {
            role.DescriptionAr = command.Request.DescriptionAr.Trim();
        }

        if (command.Request.IsActive.HasValue)
        {
            role.IsActive = command.Request.IsActive.Value;
        }

        var updated = await roleManager.UpdateAsync(role).ConfigureAwait(false);

        if (!updated.Succeeded)
        {
            return Result<RoleDto>.Failure(
                Features.Users.Commands.UserCommandSupport.IdentityErrorsToErrors(updated),
                "The role could not be updated.");
        }

        var permissions = await RoleCommandSupport
            .GetPermissionsAsync(roleManager, role, cancellationToken)
            .ConfigureAwait(false);

        var counts = await accessReader
            .GetUserCountsByRoleIdsAsync([role.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<RoleDto>.Success(
            RoleDto.FromEntity(role, permissions, counts.GetValueOrDefault(role.Id)),
            "Role updated successfully.");
    }
}

/// <summary>
/// Deletes a custom role.
/// </summary>
public sealed record DeleteRoleCommand(Guid Id) : IRequest<Result<bool>>;

public sealed class DeleteRoleCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<DeleteRoleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RoleCommandSupport.RequireTenant(tenantContext);

        var role = await RoleCommandSupport
            .FindForTenantAsync(roleManager, tenantContext, command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (role.IsSystemRole || RoleNames.IsTenantRole(role.Name!))
        {
            return Result<bool>.Failure(
                "Roles.CannotDeleteSystemRole",
                "A system role cannot be deleted.");
        }

        var counts = await accessReader
            .GetUserCountsByRoleIdsAsync([role.Id], cancellationToken)
            .ConfigureAwait(false);

        var memberCount = counts.GetValueOrDefault(role.Id);

        if (memberCount > 0)
        {
            // Deleting the role instead would leave those users with less access than they
            // appear to have, with no record of what was lost.
            return Result<bool>.Failure(
                "Roles.RoleInUse",
                $"This role is assigned to {memberCount} user(s). Reassign them before deleting it.");
        }

        var deleted = await roleManager.DeleteAsync(role).ConfigureAwait(false);

        if (!deleted.Succeeded)
        {
            return Result<bool>.Failure(
                Features.Users.Commands.UserCommandSupport.IdentityErrorsToErrors(deleted),
                "The role could not be deleted.");
        }

        return Result<bool>.Success(true, "Role deleted successfully.");
    }
}

/// <summary>
/// Replaces a role's permission set.
/// </summary>
/// <remarks>
/// This is the highest-value target in the whole permission model: whoever can call it can
/// turn any role into a superuser. Two independent guards apply - the endpoint requires
/// <see cref="PermissionConstants.RolesAssignPermissions"/>, and the handler additionally
/// refuses to grant anything the caller does not already hold.
/// </remarks>
public sealed record AssignPermissionsCommand(Guid Id, AssignPermissionsRequest Request)
    : IRequest<Result<RoleDto>>;

public sealed class AssignPermissionsCommandHandler(
    RoleManager<ApplicationRole> roleManager,
    ITenantContext tenantContext,
    IUserAccessReader accessReader,
    ICurrentUser currentUser)
    : IRequestHandler<AssignPermissionsCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(AssignPermissionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        RoleCommandSupport.RequireTenant(tenantContext);

        var role = await RoleCommandSupport
            .FindForTenantAsync(roleManager, tenantContext, command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (role.TenantId == Guid.Empty)
        {
            // Platform roles carry Guid.Empty and are invisible to the tenant query filter,
            // so this is belt-and-braces rather than the primary defence.
            return Result<RoleDto>.Failure(
                "Roles.CannotModifyPlatformRole",
                "Platform-level roles cannot be modified from a tenant context.");
        }

        if (!RoleCommandSupport.TryNormalizePermissions(
                command.Request.Permissions,
                out var permissions,
                out var unknown))
        {
            return Result<RoleDto>.Failure(
                "Roles.UnknownPermission",
                $"'{unknown}' is not a known permission.");
        }

        var callerPermissions = await RoleCommandSupport
            .LoadCallerPermissionsAsync(accessReader, currentUser, cancellationToken)
            .ConfigureAwait(false);

        RoleCommandSupport.EnsureNotEscalating(permissions, callerPermissions);

        // A caller must not be able to strip their own authority through this endpoint, which
        // would leave the tenant without anyone able to restore it.
        if (currentUser.UserId is not { } callerId)
        {
            throw new BusinessRuleException("Auth.Unauthenticated", "Authentication is required.");
        }

        var memberIds = await accessReader
            .GetUserIdsByRoleAsync(role.Name!, cancellationToken)
            .ConfigureAwait(false);

        if (memberIds.Contains(callerId))
        {
            var lost = callerPermissions.Except(permissions, StringComparer.OrdinalIgnoreCase).ToArray();

            if (lost.Length > 0)
            {
                return Result<RoleDto>.Failure(
                    "Roles.CannotRevokeOwnPermissions",
                    $"You hold this role, so you cannot remove permissions you rely on: {string.Join(", ", lost)}.");
            }
        }

        var result = await RoleCommandSupport
            .ReplacePermissionsAsync(roleManager, role, permissions, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result<RoleDto>.Failure(
                Features.Users.Commands.UserCommandSupport.IdentityErrorsToErrors(result),
                "The permissions could not be assigned.");
        }

        var counts = await accessReader
            .GetUserCountsByRoleIdsAsync([role.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<RoleDto>.Success(
            RoleDto.FromEntity(role, permissions, counts.GetValueOrDefault(role.Id)),
            "Permissions updated successfully.");
    }
}

using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Users.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Application.Features.Users.Commands;

/// <summary>
/// Creates a user inside the caller's tenant and assigns their roles.
/// </summary>
/// <remarks>
/// <para>
/// The tenant id is taken from <see cref="ITenantContext"/>, never from the request: the
/// request DTO has no tenant field at all, so there is nothing for a client to tamper with.
/// The DbContext's save-time check provides a second, independent guarantee.
/// </para>
/// <para>
/// Role names are validated against <see cref="RoleNames"/> and the tenant's own role rows,
/// so a caller cannot invent a role and thereby grant themselves capability they were never
/// given.
/// </para>
/// </remarks>
public sealed record CreateUserCommand(CreateUserRequest Request) : IRequest<Result<UserDto>>;

public sealed class CreateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    UserCommandSupport support,
    ITenantContext tenantContext,
    IUserAccessReader accessReader)
    : IRequestHandler<CreateUserCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = BranchCommandSupport.RequireTenantId(tenantContext);
        var request = command.Request;

        var email = Normalize.Required(request.Email, string.Empty);

        var existing = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

        if (existing is not null)
        {
            // The same response whether the clash is a duplicate email or a different
            // account in another tenant. Email is globally unique, so confirming which
            // would leak account existence across tenants.
            return Result<UserDto>.Failure(
                "Users.EmailAlreadyExists",
                "A user with this email address already exists.");
        }

        var validRoles = await support.ResolveRolesAsync(request.Roles, cancellationToken).ConfigureAwait(false);

        if (validRoles is null)
        {
            return Result<UserDto>.Failure(
                "Users.UnknownRole",
                "One or more requested roles do not exist in this tenant.");
        }

        var user = new ApplicationUser
        {
            TenantId = tenantId,
            FullName = Normalize.Required(request.FullName, string.Empty),
            Email = email,
            UserName = email,
            PhoneNumber = Normalize.Optional(request.PhoneNumber),
            BranchId = request.BranchId,
            PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage)
                ? "ar"
                : Normalize.Required(request.PreferredLanguage, "ar"),
            MustChangePassword = request.MustChangePassword,
            IsActive = true,
            EmailConfirmed = !request.SendEmailConfirmation
        };

        // Identity performs the hashing; there is no password code anywhere in this project.
        var result = await userManager.CreateAsync(user, request.Password).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result<UserDto>.Failure(
                UserCommandSupport.IdentityErrorsToErrors(result),
                "The user could not be created.");
        }

        if (validRoles.Count > 0)
        {
            var assign = await userManager.AddToRolesAsync(user, validRoles).ConfigureAwait(false);

            if (!assign.Succeeded)
            {
                return Result<UserDto>.Failure(
                UserCommandSupport.IdentityErrorsToErrors(assign),
                    "The user was created but roles could not be assigned.");
            }
        }

        var permissions = await accessReader
            .GetPermissionsForUsersAsync([user.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<UserDto>.Success(
            UserDto.FromEntity(user, validRoles, permissions.GetValueOrDefault(user.Id) ?? []),
            "User created successfully.");
    }
}

public sealed record UpdateUserCommand(Guid Id, UpdateUserRequest Request) : IRequest<Result<UserDto>>;

public sealed class UpdateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IUserAccessReader accessReader)
    : IRequestHandler<UpdateUserCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        BranchCommandSupport.RequireTenantId(tenantContext);

        var user = await userManager.FindByIdAsync(command.Id.ToString()).ConfigureAwait(false);

        // ApplicationUser carries the tenant query filter, so FindByIdAsync cannot return
        // another tenant's user. A miss therefore means "not found for you".
        if (user is null || user.TenantId != tenantContext.TenantId)
        {
            return Result<UserDto>.Failure("Users.NotFound", "User not found.");
        }

        var request = command.Request;

        user.FullName = Normalize.Optional(request.FullName) ?? user.FullName;
        user.PhoneNumber = Normalize.Optional(request.PhoneNumber) ?? user.PhoneNumber;
        user.BranchId = request.BranchId;
        user.PreferredLanguage = Normalize.Optional(request.PreferredLanguage) ?? user.PreferredLanguage;
        user.Notes = Normalize.Optional(request.Notes) ?? user.Notes;

        if (request.IsActive.HasValue && request.IsActive.Value != user.IsActive)
        {
            // Deactivating oneself would leave the caller holding a token for an account that
            // can no longer authenticate, with no obvious cause.
            if (currentUser.UserId == user.Id)
            {
                throw new BusinessRuleException(
                    "Users.CannotDeactivateSelf",
                    "You cannot deactivate your own account.");
            }

            user.IsActive = request.IsActive.Value;
        }

        var result = await userManager.UpdateAsync(user).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result<UserDto>.Failure(
                UserCommandSupport.IdentityErrorsToErrors(result),
                "The user could not be updated.");
        }

        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var permissions = await accessReader
            .GetPermissionsForUsersAsync([user.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<UserDto>.Success(
            UserDto.FromEntity(user, [.. roles], permissions.GetValueOrDefault(user.Id) ?? []),
            "User updated successfully.");
    }
}

public sealed record DeleteUserCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IRepository<LoginHistory> loginHistory,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<DeleteUserCommand, Result>
{
    public async Task<Result> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        BranchCommandSupport.RequireTenantId(tenantContext);

        var user = await userManager.FindByIdAsync(command.Id.ToString()).ConfigureAwait(false);

        if (user is null || user.TenantId != tenantContext.TenantId)
        {
            return Result.Failure("Users.NotFound", "User not found.");
        }

        if (currentUser.UserId == user.Id)
        {
            throw new BusinessRuleException(
                "Users.CannotDeleteSelf",
                "You cannot delete your own account.");
        }

        // Deleting a user cascades away their refresh tokens and login history, which are
        // exactly the rows an auditor needs. So an account that has ever signed in must be
        // deactivated, not deleted.
        var hasHistory = await loginHistory.Query
            .AnyAsync(h => h.UserId == user.Id, cancellationToken)
            .ConfigureAwait(false);

        if (hasHistory || user.LastLoginAt is not null)
        {
            return Result.Failure(
                "Users.DeactivateInstead",
                "This user has sign-in history. Deactivate the account instead of deleting it.");
        }

        // Both UserRoles foreign keys are deliberately Restrict (see ApplyIdentityTableNames),
        // so the delete would be rejected by SQL with a reference conflict - an unhandled
        // 500 - unless the memberships are lifted first. Role deletion guards the same
        // constraint from its side with Roles.RoleInUse; this is the user side.
        var currentRoles = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        if (currentRoles.Count > 0)
        {
            var removeRoles = await userManager.RemoveFromRolesAsync(user, currentRoles).ConfigureAwait(false);

            if (!removeRoles.Succeeded)
            {
                return Result.Failure(
                    UserCommandSupport.IdentityErrorsToErrors(removeRoles),
                    "The user's roles could not be removed.");
            }
        }

        var result = await userManager.DeleteAsync(user).ConfigureAwait(false);

        return result.Succeeded
            ? Result.Success("User deleted successfully.")
            : Result.Failure(
                UserCommandSupport.IdentityErrorsToErrors(result),
                "The user could not be deleted.");
    }
}

public sealed record ResetPasswordCommand(Guid Id, AdminResetPasswordRequest Request) : IRequest<Result>;

public sealed class ResetPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext)
    : IRequestHandler<ResetPasswordCommand, Result>
{
    public async Task<Result> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        BranchCommandSupport.RequireTenantId(tenantContext);

        var user = await userManager.FindByIdAsync(command.Id.ToString()).ConfigureAwait(false);

        if (user is null || user.TenantId != tenantContext.TenantId)
        {
            return Result.Failure("Users.NotFound", "User not found.");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var result = await userManager.ResetPasswordAsync(user, token, command.Request.NewPassword).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Result.Failure(
                UserCommandSupport.IdentityErrorsToErrors(result),
                "The password could not be reset.");
        }

        // Force a password change at next sign-in, and clear any lockout so the user is not
        // locked out by the very act of being reset.
        user.MustChangePassword = command.Request.MustChangePassword;
        user.LockedOutUntil = null;
        user.AccessFailedCount = 0;

        await userManager.UpdateAsync(user).ConfigureAwait(false);

        return Result.Success("Password reset successfully.");
    }
}

public sealed record ToggleUserActiveCommand(Guid Id, ChangeUserStatusRequest Request) : IRequest<Result>;

public sealed class ToggleUserActiveCommandHandler(
    UserManager<ApplicationUser> userManager,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<ToggleUserActiveCommand, Result>
{
    public async Task<Result> Handle(ToggleUserActiveCommand command, CancellationToken cancellationToken)
    {
        BranchCommandSupport.RequireTenantId(tenantContext);

        var user = await userManager.FindByIdAsync(command.Id.ToString()).ConfigureAwait(false);

        if (user is null || user.TenantId != tenantContext.TenantId)
        {
            return Result.Failure("Users.NotFound", "User not found.");
        }

        if (currentUser.UserId == user.Id)
        {
            throw new BusinessRuleException(
                "Users.CannotDeactivateSelf",
                "You cannot deactivate your own account.");
        }

        user.IsActive = command.Request.IsActive;

        if (!user.IsActive)
        {
            // Deactivation must end active sessions too, otherwise a deactivated user keeps
            // working until their access token expires.
            user.LockedOutUntil = DateTime.UtcNow.AddYears(100);
        }
        else
        {
            user.LockedOutUntil = null;
            user.AccessFailedCount = 0;
        }

        var result = await userManager.UpdateAsync(user).ConfigureAwait(false);

        return result.Succeeded
            ? Result.Success(user.IsActive ? "User activated." : "User deactivated.")
            : Result.Failure(
                UserCommandSupport.IdentityErrorsToErrors(result),
                "The user's status could not be changed.");
    }
}

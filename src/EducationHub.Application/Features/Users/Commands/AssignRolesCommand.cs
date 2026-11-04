using EducationHub.Application.Common.Exceptions;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Application.Features.Users.DTOs;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace EducationHub.Application.Features.Users.Commands;

/// <summary>
/// Replaces a user's role assignments.
/// </summary>
/// <remarks>
/// <para>
/// Roles are the only source of capability in this system, so this endpoint is the sharpest
/// privilege-escalation risk in the product: a caller who can reach it could grant
/// themselves <c>TenantAdmin</c> and then anything else.
/// </para>
/// <para>
/// Two rules close that off. The caller must hold
/// <see cref="PermissionConstants.UsersAssignRoles"/> or
/// <see cref="PermissionConstants.RolesAssignPermissions"/>, and may never assign a role
/// whose permissions exceed their own - so a tenant administrator cannot mint a role more
/// powerful than themselves.
/// </para>
/// </remarks>
public sealed record AssignRolesCommand(Guid Id, AssignRolesRequest Request) : IRequest<Result<UserDto>>;

public sealed class AssignRolesCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    UserCommandSupport support,
    ICurrentUser currentUser,
    IUserAccessReader accessReader)
    : IRequestHandler<AssignRolesCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(AssignRolesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.TenantId is null)
        {
            throw new BusinessRuleException(
                "Errors.NoTenantContext",
                "This operation requires an active tenant context.");
        }

        var user = await userManager.FindByIdAsync(command.Id.ToString()).ConfigureAwait(false);

        if (user is null || user.TenantId != currentUser.TenantId)
        {
            return Result<UserDto>.Failure("Users.NotFound", "User not found.");
        }

        var requested = command.Request.Roles?
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var resolved = await support
            .ResolveRolesAsync(requested, cancellationToken)
            .ConfigureAwait(false);

        if (resolved is null)
        {
            return Result<UserDto>.Failure(
                "Users.UnknownRole",
                "One or more requested roles do not exist in this tenant.");
        }

        // Re-derive the caller's effective permissions from the DATABASE, not from their
        // token. A token's claims go stale the moment the caller's own roles change, so
        // trusting them here would let a demoted administrator keep escalating for the
        // remaining life of their access token.
        var callerPermissions = await LoadOwnPermissionsAsync(cancellationToken).ConfigureAwait(false);

        var targetPermissions = await ResolvePermissionsAsync(resolved, cancellationToken).ConfigureAwait(false);

        if (targetPermissions.Except(callerPermissions, StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new BusinessRuleException(
                "Users.CannotGrantMoreThanOwn",
                "You cannot assign a role whose permissions exceed your own.");
        }

        var currentRoles = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        var removed = currentRoles.Where(r => !resolved.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();
        var added = resolved.Where(r => !currentRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (removed.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, removed).ConfigureAwait(false);

            if (!removeResult.Succeeded)
            {
                return Result<UserDto>.Failure(
                    UserCommandSupport.IdentityErrorsToErrors(removeResult),
                    "Roles could not be removed.");
            }
        }

        if (added.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, added).ConfigureAwait(false);

            if (!addResult.Succeeded)
            {
                return Result<UserDto>.Failure(
                    UserCommandSupport.IdentityErrorsToErrors(addResult),
                    "Roles could not be assigned.");
            }
        }

        IReadOnlyList<string> finalRoles = [.. await userManager.GetRolesAsync(user).ConfigureAwait(false)];

        var finalPermissions = await accessReader
            .GetPermissionsForUsersAsync([user.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result<UserDto>.Success(
            UserDto.FromEntity(user, finalRoles, finalPermissions.GetValueOrDefault(user.Id) ?? []),
            "Roles updated successfully.");
    }

    private async Task<IReadOnlyCollection<string>> LoadOwnPermissionsAsync(CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId
            ?? throw new BusinessRuleException("Auth.Unauthenticated", "Authentication is required.");

        var permissions = await accessReader
            .GetPermissionsForUsersAsync([callerId], cancellationToken)
            .ConfigureAwait(false);

        return permissions.GetValueOrDefault(callerId) ?? [];
    }

    private async Task<IReadOnlyCollection<string>> ResolvePermissionsAsync(
        IReadOnlyList<string> roleNames,
        CancellationToken cancellationToken)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in roleNames)
        {
            // FindByNameAsync goes through the tenant-filtered DbSet, so this can only ever
            // resolve roles belonging to the caller's own tenant.
            var role = await roleManager.FindByNameAsync(name).ConfigureAwait(false);

            if (role is null)
            {
                continue;
            }

            var claims = await roleManager.GetClaimsAsync(role).ConfigureAwait(false);

            foreach (var claim in claims.Where(static c => c.Type == EducationHubClaimTypes.PermissionClaimType))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions;
    }
}

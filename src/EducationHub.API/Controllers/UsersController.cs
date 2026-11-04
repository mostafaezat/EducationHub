using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Users.Commands;
using EducationHub.Application.Features.Users.DTOs;
using EducationHub.Application.Features.Users.Queries;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Tenant user administration.</summary>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public sealed class UsersController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's users.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.UsersView)]
    [ProducesResponseType(typeof(Result<PaginatedList<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<UserDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? role = null,
        [FromQuery] Guid? branchId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetUsersQuery(pagination, activeOnly, role, branchId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one user.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.UsersView)]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<UserDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetUserQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a user in the caller's tenant.</summary>
    /// <remarks>
    /// The tenant is taken from the resolved context, never from the request body, so a
    /// caller cannot place a user into a tenant they do not administer.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.UsersCreate)]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<UserDto>>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateUserCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a user's editable fields.</summary>
    /// <remarks>
    /// Email, tenant and roles are intentionally not editable here: changing email is a
    /// verification event, changing roles is <c>AssignRoles</c>, and changing tenant is not
    /// a supported operation at all.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.UsersUpdate)]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<UserDto>>> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateUserCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a user.</summary>
    /// <remarks>
    /// Soft delete, not hard: audit rows and grade history reference the user, and those
    /// must stay readable after someone leaves.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.UsersDelete)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteUserCommand(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Enables or disables a user.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = PermissionConstants.UsersToggleActive)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> SetStatus(
        Guid id,
        [FromBody] ChangeUserStatusRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ToggleUserActiveCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Replaces a user's password on the caller's behalf.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = PermissionConstants.UsersResetPassword)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> ResetPassword(
        Guid id,
        [FromBody] AdminResetPasswordRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new ResetPasswordCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Replaces the user's role assignments wholesale.</summary>
    /// <remarks>
    /// Replace rather than add/remove: the frontend always holds the full desired set, and
    /// a replace cannot leave a role behind by omission.
    /// </remarks>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = PermissionConstants.UsersAssignRoles)]
    [ProducesResponseType(typeof(Result<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<UserDto>>> AssignRoles(
        Guid id,
        [FromBody] AssignRolesRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new AssignRolesCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Pages the members of one role. Backs the role editor's members panel.</summary>
    [HttpGet("by-role/{role}")]
    [Authorize(Policy = PermissionConstants.UsersView)]
    [ProducesResponseType(typeof(Result<PaginatedList<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<UserDto>>>> GetByRole(
        string role,
        [FromQuery] PaginationRequest pagination,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetUsersByRoleQuery(role, pagination), cancellationToken)
            .ConfigureAwait(false));
}
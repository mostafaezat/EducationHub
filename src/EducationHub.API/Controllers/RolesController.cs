using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth.Queries;
using EducationHub.Application.Features.Roles.Commands;
using EducationHub.Application.Features.Roles.DTOs;
using EducationHub.Application.Features.Roles.Queries;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Role and permission administration for the current tenant.</summary>
[ApiController]
[Route("api/roles")]
[Authorize]
[Produces("application/json")]
public sealed class RolesController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's roles.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.RolesView)]
    [ProducesResponseType(typeof(Result<PaginatedList<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<RoleDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetRolesQuery(pagination, search, activeOnly), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one role with its permissions and member count.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.RolesView)]
    [ProducesResponseType(typeof(Result<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<RoleDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRoleQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a role.</summary>
    /// <remarks>
    /// A new role starts with no permissions. Granting them is a separate call, and both are
    /// checked against the caller's own permissions so authority cannot be escalated.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.RolesCreate)]
    [ProducesResponseType(typeof(Result<RoleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<RoleDto>>> Create(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateRoleCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a role's descriptive fields and active flag.</summary>
    /// <remarks>
    /// The role name is immutable: it is the join key for user assignments and is referenced
    /// by seed data and policies, so renaming is not offered.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.RolesUpdate)]
    [ProducesResponseType(typeof(Result<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<RoleDto>>> Update(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateRoleCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Replaces a role's permission set wholesale.</summary>
    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = PermissionConstants.RolesAssignPermissions)]
    [ProducesResponseType(typeof(Result<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<RoleDto>>> AssignPermissions(
        Guid id,
        [FromBody] AssignPermissionsRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new AssignPermissionsCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Deletes a role.</summary>
    /// <remarks>
    /// System roles and roles that still have members are refused by the handler; a 400 with
    /// a specific code tells the UI what to offer next.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.RolesDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteRoleCommand(id), cancellationToken).ConfigureAwait(false));
}

/// <summary>The permission catalog, independent of roles.</summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
[Produces("application/json")]
public sealed class PermissionsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Every permission the system knows, pre-grouped by area.
    /// </summary>
    /// <remarks>
    /// Derived from <c>PermissionConstants</c> on the server, so the role editor's checkbox
    /// list can never drift from what the authorization handler will actually accept.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.PermissionsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<PermissionAreaDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<PermissionAreaDto>>>> Catalog(
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetPermissionCatalogQuery(), cancellationToken)
            .ConfigureAwait(false));
}
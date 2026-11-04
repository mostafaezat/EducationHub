using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Tenancy.Commands;
using EducationHub.Application.Features.Tenancy.DTOs;
using EducationHub.Application.Features.Tenancy.Queries;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Platform-level tenant administration.</summary>
[ApiController]
[Route("api/tenants")]
[Authorize]
[Produces("application/json")]
public sealed class TenantsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Pages all tenants.
    /// </summary>
    /// <remarks>
    /// The one deliberately tenant-blind list endpoint: a platform administrator has no
    /// tenant context to filter by. Tenant-owned endpoints filter through the global query
    /// filter instead, which this bypasses only because it is itself platform-scoped.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = RoleNames.PlatformAdmin)]
    [ProducesResponseType(typeof(Result<PaginatedList<TenantDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<TenantDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetTenantsQuery(pagination), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one tenant by id or by slug.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = RoleNames.PlatformAdmin)]
    [ProducesResponseType(typeof(Result<TenantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<TenantDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTenantQuery(id, null), cancellationToken).ConfigureAwait(false));

    /// <summary>Provisions a new tenant together with its default roles and settings.</summary>
    [HttpPost]
    [Authorize(Policy = RoleNames.PlatformAdmin)]
    [ProducesResponseType(typeof(Result<TenantDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<TenantDto>>> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateTenantCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a tenant's profile, status and subscription window.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = RoleNames.PlatformAdmin)]
    [ProducesResponseType(typeof(Result<TenantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<TenantDto>>> Update(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateTenantCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a tenant.</summary>
    /// <remarks>
    /// Soft delete keeps the tenant's audit trail intact and lets a mistaken deletion be
    /// investigated. Signing-in is blocked separately by the tenant's active flag.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = RoleNames.PlatformAdmin)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteTenantCommand(id), cancellationToken).ConfigureAwait(false));
}

/// <summary>The caller's branches. Every branch endpoint is tenant-scoped.</summary>
[ApiController]
[Route("api/branches")]
[Authorize]
[Produces("application/json")]
public sealed class BranchesController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the current tenant's branches.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.BranchesView)]
    [ProducesResponseType(typeof(Result<PaginatedList<BranchDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<BranchDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetBranchesQuery(pagination, activeOnly), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one branch.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.BranchesView)]
    [ProducesResponseType(typeof(Result<BranchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<BranchDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetBranchQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a branch in the current tenant.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.BranchesCreate)]
    [ProducesResponseType(typeof(Result<BranchDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<BranchDto>>> Create(
        [FromBody] CreateBranchRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateBranchCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a branch.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.BranchesUpdate)]
    [ProducesResponseType(typeof(Result<BranchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<BranchDto>>> Update(
        Guid id,
        [FromBody] UpdateBranchRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateBranchCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a branch.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.BranchesDelete)]
    [ProducesResponseType(typeof(Result), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteBranchCommand(id), cancellationToken).ConfigureAwait(false));
}

/// <summary>Per-tenant operational settings.</summary>
[ApiController]
[Route("api/settings")]
[Authorize]
[Produces("application/json")]
public sealed class TenantSettingsController(IMediator mediator) : ControllerBase
{
    /// <summary>Reads the current tenant's settings.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.SettingsView)]
    [ProducesResponseType(typeof(Result<TenantSettingsDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<TenantSettingsDto>>> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTenantSettingsQuery(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Updates the current tenant's settings.
    /// </summary>
    /// <remarks>
    /// Partial by field: null means "leave as is". The alternative - a full settings object on
    /// every save - means two concurrent editors silently overwrite each other's changes.
    /// </remarks>
    [HttpPut]
    [Authorize(Policy = PermissionConstants.SettingsUpdate)]
    [ProducesResponseType(typeof(Result<TenantSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<TenantSettingsDto>>> Update(
        [FromBody] UpdateTenantSettingsRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateTenantSettingsCommand(request), cancellationToken)
            .ConfigureAwait(false));
}
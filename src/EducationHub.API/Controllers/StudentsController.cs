using EducationHub.Application.Features.Students.Commands;
using EducationHub.Application.Features.Students.DTOs;
using EducationHub.Application.Features.Students.Queries;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Students and their guardian links (Mission Phase 2.5).</summary>
[ApiController]
[Route("api/students")]
[Authorize]
[Produces("application/json")]
public sealed class StudentsController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's students, optionally filtered by grade and status.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.StudentsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<StudentProfileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<StudentProfileDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] Guid? gradeLevelId = null,
        [FromQuery] StudentStatus? status = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetStudentsQuery(pagination, search, gradeLevelId, status), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one student with grade, branch and guardians.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.StudentsView)]
    [ProducesResponseType(typeof(Result<StudentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<StudentProfileDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetStudentQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Creates a student; optionally links an existing account (<c>userId</c>) or creates
    /// one with the Student role (<c>accountEmail</c> + <c>accountPassword</c>), atomically.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.StudentsCreate)]
    [ProducesResponseType(typeof(Result<StudentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<StudentProfileDto>>> Create(
        [FromBody] CreateStudentRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateStudentCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Partially updates a student's profile.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.StudentsUpdate)]
    [ProducesResponseType(typeof(Result<StudentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<StudentProfileDto>>> Update(
        Guid id,
        [FromBody] UpdateStudentRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateStudentCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Replaces the student's guardians. An empty list clears them.</summary>
    [HttpPut("{id:guid}/guardians")]
    [Authorize(Policy = PermissionConstants.StudentsUpdate)]
    [ProducesResponseType(typeof(Result<StudentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<StudentProfileDto>>> SetGuardians(
        Guid id,
        [FromBody] SetStudentGuardiansRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new SetStudentGuardiansCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes the student and removes their guardian links.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.StudentsDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<bool>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteStudentCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Parents / guardians (Mission Phase 2.5).</summary>
[ApiController]
[Route("api/parents")]
[Authorize]
[Produces("application/json")]
public sealed class ParentsController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's parents.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.ParentsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<ParentProfileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<ParentProfileDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetParentsQuery(pagination, search), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// The signed-in parent's own children. Scoped by the caller's account; takes no id.
    /// </summary>
    [HttpGet("me/children")]
    [Authorize(Policy = PermissionConstants.ParentsViewOwnChildren)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ChildLinkDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<ChildLinkDto>>>> GetMyChildren(
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetMyChildrenQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one parent with their linked children.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.ParentsView)]
    [ProducesResponseType(typeof(Result<ParentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<ParentProfileDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetParentQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Creates a parent; optionally links an existing account (<c>userId</c>) or creates one
    /// with the Parent role signing in with <c>email</c> + <c>accountPassword</c>, atomically.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.ParentsCreate)]
    [ProducesResponseType(typeof(Result<ParentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<ParentProfileDto>>> Create(
        [FromBody] CreateParentRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateParentCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Partially updates a parent's profile.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.ParentsUpdate)]
    [ProducesResponseType(typeof(Result<ParentProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<ParentProfileDto>>> Update(
        Guid id,
        [FromBody] UpdateParentRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateParentCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes the parent and removes their guardian links.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.ParentsDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<bool>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteParentCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

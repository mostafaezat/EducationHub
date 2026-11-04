using EducationHub.Application.Features.Teachers.Commands;
using EducationHub.Application.Features.Teachers.DTOs;
using EducationHub.Application.Features.Teachers.Queries;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Teachers and their subject assignments.</summary>
[ApiController]
[Route("api/teachers")]
[Authorize]
[Produces("application/json")]
public sealed class TeachersController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's teachers.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.TeachersView)]
    [ProducesResponseType(typeof(Result<PaginatedList<TeacherProfileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<TeacherProfileDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetTeachersQuery(pagination, search, activeOnly), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one teacher with identity data and assigned subjects.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.TeachersView)]
    [ProducesResponseType(typeof(Result<TeacherProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<TeacherProfileDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetTeacherQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Creates a teacher, optionally with a brand-new account carrying the Teacher role -
    /// atomically, or linked to an existing account when <c>userId</c> is supplied.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.TeachersCreate)]
    [ProducesResponseType(typeof(Result<TeacherProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<TeacherProfileDto>>> Create(
        [FromBody] CreateTeacherRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateTeacherCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Partially updates a teacher's profile.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.TeachersUpdate)]
    [ProducesResponseType(typeof(Result<TeacherProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<TeacherProfileDto>>> Update(
        Guid id,
        [FromBody] UpdateTeacherRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateTeacherCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Replaces the teacher's subject set. An empty list clears it.</summary>
    [HttpPut("{id:guid}/subjects")]
    [Authorize(Policy = PermissionConstants.TeachersUpdate)]
    [ProducesResponseType(typeof(Result<TeacherProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<TeacherProfileDto>>> AssignSubjects(
        Guid id,
        [FromBody] AssignTeacherSubjectsRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new AssignTeacherSubjectsCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// The compensation rule in force for this teacher under an optional subject, with the
    /// precedence rank that selected it. <c>rule</c> is null when no active rule matches.
    /// </summary>
    [HttpGet("{id:guid}/compensation")]
    [Authorize(Policy = PermissionConstants.TeachersView)]
    [ProducesResponseType(typeof(Result<ResolvedCompensationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<ResolvedCompensationDto>>> GetCompensation(
        Guid id,
        [FromQuery] Guid? subjectId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetTeacherCompensationQuery(id, subjectId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Soft-deletes the teacher profile with its rules and subject links. The linked
    /// account is left to the Users feature.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.TeachersDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<bool>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteTeacherCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Compensation rules (rates) and their precedence scope.</summary>
[ApiController]
[Route("api/compensation-rules")]
[Authorize]
[Produces("application/json")]
public sealed class CompensationRulesController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's compensation rules, optionally narrowed to one teacher.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.TeachersView)]
    [ProducesResponseType(typeof(Result<PaginatedList<CompensationRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<CompensationRuleDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] Guid? teacherProfileId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetCompensationRulesQuery(pagination, teacherProfileId), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Creates a compensation rule. All-null scope creates the tenant default.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.TeachersCreate)]
    [ProducesResponseType(typeof(Result<CompensationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<CompensationRuleDto>>> Create(
        [FromBody] CreateCompensationRuleRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateCompensationRuleCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Partially updates a rule's rate, basis or active flag. The scope is fixed.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.TeachersUpdate)]
    [ProducesResponseType(typeof(Result<CompensationRuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<CompensationRuleDto>>> Update(
        Guid id,
        [FromBody] UpdateCompensationRuleRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateCompensationRuleCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a compensation rule.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.TeachersDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<bool>>> Delete(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteCompensationRuleCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

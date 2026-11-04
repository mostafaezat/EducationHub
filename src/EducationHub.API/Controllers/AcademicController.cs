using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Academic.Commands;
using EducationHub.Application.Features.Academic.DTOs;
using EducationHub.Application.Features.Academic.Queries;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Academic years.</summary>
[ApiController]
[Route("api/academic-years")]
[Authorize]
[Produces("application/json")]
public sealed class AcademicYearsController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's academic years.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.AcademicYearsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<AcademicYearDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<AcademicYearDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetAcademicYearsQuery(pagination, search, includeArchived), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// The tenant's current academic year.
    /// </summary>
    /// <remarks>
    /// Separate from the list because most screens need this one row and a 404 is meaningful
    /// there: it tells the UI to prompt for setup rather than render an empty table.
    /// </remarks>
    [HttpGet("current")]
    [Authorize(Policy = PermissionConstants.AcademicYearsView)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<AcademicYearDto>>> GetCurrent(CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetCurrentAcademicYearQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Resolves the academic year covering a given date.</summary>
    [HttpGet("for-date/{date:datetime}")]
    [Authorize(Policy = PermissionConstants.AcademicYearsView)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<AcademicYearDto>>> GetForDate(
        DateOnly date,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new GetAcademicYearForDateQuery(date), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one academic year.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.AcademicYearsView)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<AcademicYearDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAcademicYearQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates an academic year.</summary>
    /// <remarks>
    /// Creating a year marked current demotes whichever year was current; the handler owns
    /// that rule so it cannot be bypassed by writing the flag directly from two places.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.AcademicYearsCreate)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<AcademicYearDto>>> Create(
        [FromBody] CreateAcademicYearRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateAcademicYearCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates an academic year.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.AcademicYearsUpdate)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<AcademicYearDto>>> Update(
        Guid id,
        [FromBody] UpdateAcademicYearRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateAcademicYearCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Marks an academic year as the tenant's current one.</summary>
    [HttpPost("{id:guid}/set-current")]
    [Authorize(Policy = PermissionConstants.AcademicYearsUpdate)]
    [ProducesResponseType(typeof(Result<AcademicYearDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<AcademicYearDto>>> SetCurrent(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new SetCurrentAcademicYearCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes an academic year.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.AcademicYearsDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteAcademicYearCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Grade levels.</summary>
[ApiController]
[Route("api/grades")]
[Authorize]
[Produces("application/json")]
public sealed class GradesController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's grade levels.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.GradesView)]
    [ProducesResponseType(typeof(Result<PaginatedList<GradeLevelDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<GradeLevelDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetGradeLevelsQuery(pagination, search, activeOnly), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one grade level.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GradesView)]
    [ProducesResponseType(typeof(Result<GradeLevelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<GradeLevelDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetGradeLevelQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a grade level.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.GradesCreate)]
    [ProducesResponseType(typeof(Result<GradeLevelDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<GradeLevelDto>>> Create(
        [FromBody] CreateGradeLevelRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateGradeLevelCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a grade level.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GradesUpdate)]
    [ProducesResponseType(typeof(Result<GradeLevelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<GradeLevelDto>>> Update(
        Guid id,
        [FromBody] UpdateGradeLevelRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateGradeLevelCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a grade level.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GradesDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteGradeLevelCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Subjects.</summary>
[ApiController]
[Route("api/subjects")]
[Authorize]
[Produces("application/json")]
public sealed class SubjectsController(IMediator mediator) : ControllerBase
{
    /// <summary>Pages the tenant's subjects.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.SubjectsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<SubjectDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<SubjectDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator
            .Send(new GetSubjectsQuery(pagination, search, activeOnly), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Gets one subject.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SubjectsView)]
    [ProducesResponseType(typeof(Result<SubjectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<SubjectDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSubjectQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Creates a subject.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.SubjectsCreate)]
    [ProducesResponseType(typeof(Result<SubjectDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<SubjectDto>>> Create(
        [FromBody] CreateSubjectRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new CreateSubjectCommand(request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Updates a subject.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SubjectsUpdate)]
    [ProducesResponseType(typeof(Result<SubjectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<SubjectDto>>> Update(
        Guid id,
        [FromBody] UpdateSubjectRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new UpdateSubjectCommand(id, request), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Soft-deletes a subject.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SubjectsDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator
            .Send(new DeleteSubjectCommand(id), cancellationToken)
            .ConfigureAwait(false));
}
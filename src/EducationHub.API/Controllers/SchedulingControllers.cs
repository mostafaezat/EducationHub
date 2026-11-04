using EducationHub.Application.Features.Scheduling;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Study groups, their enrollments and weekly schedule slots (Mission Phase 3).</summary>
[ApiController]
[Route("api/groups")]
[Authorize]
[Produces("application/json")]
public sealed class GroupsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.GroupsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<StudyGroupDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<StudyGroupDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] string? search = null,
        [FromQuery] Guid? subjectId = null,
        [FromQuery] Guid? teacherProfileId = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetStudyGroupsQuery(pagination, search, subjectId, teacherProfileId, activeOnly), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GroupsView)]
    [ProducesResponseType(typeof(Result<StudyGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<StudyGroupDto>>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudyGroupQuery(id), cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = PermissionConstants.GroupsCreate)]
    [ProducesResponseType(typeof(Result<StudyGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<StudyGroupDto>>> Create([FromBody] CreateStudyGroupRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateStudyGroupCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GroupsUpdate)]
    [ProducesResponseType(typeof(Result<StudyGroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<StudyGroupDto>>> Update(Guid id, [FromBody] UpdateStudyGroupRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateStudyGroupCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.GroupsDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteStudyGroupCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}/enrollments")]
    [Authorize(Policy = PermissionConstants.GroupsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<EnrollmentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<EnrollmentDto>>>> GetEnrollments(
        Guid id,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetEnrollmentsQuery(id, activeOnly), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/enrollments")]
    [Authorize(Policy = PermissionConstants.GroupsUpdate)]
    [ProducesResponseType(typeof(Result<EnrollmentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<EnrollmentDto>>> Enroll(Guid id, [FromBody] EnrollStudentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new EnrollStudentCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}/enrollments/{enrollmentId:guid}")]
    [Authorize(Policy = PermissionConstants.GroupsUpdate)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> EndEnrollment(Guid id, Guid enrollmentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new EndEnrollmentCommand(id, enrollmentId), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/slots")]
    [Authorize(Policy = PermissionConstants.GroupsUpdate)]
    [ProducesResponseType(typeof(Result<ScheduleSlotDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ScheduleSlotDto>>> CreateSlot(Guid id, [FromBody] CreateScheduleSlotRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateScheduleSlotCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}/slots/{slotId:guid}")]
    [Authorize(Policy = PermissionConstants.GroupsUpdate)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> DeleteSlot(Guid id, Guid slotId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteScheduleSlotCommand(id, slotId), cancellationToken).ConfigureAwait(false));
}

/// <summary>A student's enrollments across all groups.</summary>
[ApiController]
[Route("api/students/{studentId:guid}/enrollments")]
[Authorize]
[Produces("application/json")]
public sealed class StudentEnrollmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.StudentsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<EnrollmentDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<EnrollmentDto>>>> Get(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentEnrollmentsQuery(studentId), cancellationToken).ConfigureAwait(false));
}

/// <summary>Class sessions and attendance (Mission Phase 3).</summary>
[ApiController]
[Route("api/sessions")]
[Authorize]
[Produces("application/json")]
public sealed class SessionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.SessionsView)]
    [ProducesResponseType(typeof(Result<PaginatedList<ClassSessionDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<PaginatedList<ClassSessionDto>>>> GetAll(
        [FromQuery] PaginationRequest pagination,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? studyGroupId = null,
        [FromQuery] Guid? teacherProfileId = null,
        [FromQuery] SessionStatus? status = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetSessionsQuery(pagination, from, to, studyGroupId, teacherProfileId, status), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SessionsView)]
    [ProducesResponseType(typeof(Result<ClassSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ClassSessionDto>>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSessionQuery(id), cancellationToken).ConfigureAwait(false));

    /// <summary>Materializes sessions from the weekly slots for a date range (idempotent).</summary>
    [HttpPost("generate")]
    [Authorize(Policy = PermissionConstants.SessionsManage)]
    [ProducesResponseType(typeof(Result<GenerateSessionsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<GenerateSessionsResultDto>>> Generate([FromBody] GenerateSessionsRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GenerateSessionsCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = PermissionConstants.SessionsManage)]
    [ProducesResponseType(typeof(Result<ClassSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ClassSessionDto>>> Create([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateSessionCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SessionsManage)]
    [ProducesResponseType(typeof(Result<ClassSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ClassSessionDto>>> Update(Guid id, [FromBody] UpdateSessionRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateSessionCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.SessionsManage)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteSessionCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}/attendance")]
    [Authorize(Policy = PermissionConstants.AttendanceView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<AttendanceRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<AttendanceRowDto>>>> GetAttendance(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSessionAttendanceQuery(id), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}/attendance")]
    [Authorize(Policy = PermissionConstants.AttendanceMark)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<AttendanceRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<AttendanceRowDto>>>> MarkAttendance(Guid id, [FromBody] MarkAttendanceRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new MarkAttendanceCommand(id, request), cancellationToken).ConfigureAwait(false));
}

using EducationHub.Application.Features.Learning;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EducationHub.API.Controllers;

/// <summary>Homework assignments and submissions (Mission Phase 6).</summary>
[ApiController]
[Route("api/homework")]
[Authorize]
[Produces("application/json")]
public sealed class HomeworkController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.HomeworkView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<HomeworkDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<HomeworkDto>>>> GetAll(
        [FromQuery] Guid? studyGroupId = null, [FromQuery] DateOnly? dueFrom = null, [FromQuery] DateOnly? dueTo = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetHomeworkListQuery(studyGroupId, dueFrom, dueTo), cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = PermissionConstants.HomeworkManage)]
    [ProducesResponseType(typeof(Result<HomeworkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<HomeworkDto>>> Create([FromBody] CreateHomeworkRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateHomeworkCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.HomeworkManage)]
    [ProducesResponseType(typeof(Result<HomeworkDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<HomeworkDto>>> Update(Guid id, [FromBody] UpdateHomeworkRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateHomeworkCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.HomeworkManage)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteHomeworkCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}/submissions")]
    [Authorize(Policy = PermissionConstants.HomeworkView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<SubmissionRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<SubmissionRowDto>>>> GetSubmissions(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHomeworkSubmissionsQuery(id), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}/submissions")]
    [Authorize(Policy = PermissionConstants.HomeworkManage)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<SubmissionRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<SubmissionRowDto>>>> UpdateSubmissions(
        Guid id, [FromBody] UpdateSubmissionsRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateSubmissionsCommand(id, request), cancellationToken).ConfigureAwait(false));
}

/// <summary>Exams and results (Mission Phase 6).</summary>
[ApiController]
[Route("api/exams")]
[Authorize]
[Produces("application/json")]
public sealed class ExamsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionConstants.ExamsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ExamDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<ExamDto>>>> GetAll([FromQuery] Guid? studyGroupId = null, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetExamsQuery(studyGroupId), cancellationToken).ConfigureAwait(false));

    [HttpPost]
    [Authorize(Policy = PermissionConstants.ExamsManage)]
    [ProducesResponseType(typeof(Result<ExamDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ExamDto>>> Create([FromBody] CreateExamRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CreateExamCommand(request), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionConstants.ExamsManage)]
    [ProducesResponseType(typeof(Result<ExamDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<ExamDto>>> Update(Guid id, [FromBody] UpdateExamRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateExamCommand(id, request), cancellationToken).ConfigureAwait(false));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.ExamsManage)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new DeleteExamCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpGet("{id:guid}/results")]
    [Authorize(Policy = PermissionConstants.ExamsView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ExamResultRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<ExamResultRowDto>>>> GetResults(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExamResultsQuery(id), cancellationToken).ConfigureAwait(false));

    [HttpPut("{id:guid}/results")]
    [Authorize(Policy = PermissionConstants.ExamsManage)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<ExamResultRowDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<ExamResultRowDto>>>> RecordResults(
        Guid id, [FromBody] RecordExamResultsRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new RecordExamResultsCommand(id, request), cancellationToken).ConfigureAwait(false));
}

/// <summary>The signed-in user's own in-app notifications. Any authenticated account.</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public sealed class NotificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Result<NotificationFeedDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<NotificationFeedDto>>> GetMine(
        [FromQuery] bool unreadOnly = false, [FromQuery] int take = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetMyNotificationsQuery(unreadOnly, take), cancellationToken).ConfigureAwait(false));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<int>>> MarkRead(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new MarkNotificationsReadCommand(id), cancellationToken).ConfigureAwait(false));

    [HttpPost("read-all")]
    [ProducesResponseType(typeof(Result<int>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<int>>> MarkAllRead(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new MarkNotificationsReadCommand(null), cancellationToken).ConfigureAwait(false));
}

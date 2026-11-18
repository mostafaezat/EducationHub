using EducationHub.Application.Common.Access;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Family;
using EducationHub.Application.Features.Learning;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Learning;
using EducationHub.Domain.Entities.Scheduling;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Files;
using EducationHub.Shared.Common.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.API.Controllers;

/// <summary>Multipart form for <c>POST /api/my/homework/{homeworkId}/submission</c>.</summary>
public sealed class SubmitHomeworkForm
{
    /// <summary>The answer: a photo of the work or a document.</summary>
    public IFormFile? File { get; set; }

    /// <summary>Optional note for the teacher.</summary>
    public string? Note { get; set; }

    /// <summary>Which child, when a parent with several children submits; a student may omit it.</summary>
    public Guid? StudentProfileId { get; set; }
}

/// <summary>
/// The signed-in account's own data. Every route here is scoped by the caller's account and
/// takes no student id it is not entitled to, so it needs no extra permission: a family only
/// ever reaches itself.
/// </summary>
[ApiController]
[Route("api/my")]
[Authorize]
[Produces("application/json")]
public sealed class MyController(
    IMediator mediator,
    DataScopeService dataScope,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IFileStorageService storage,
    IRepository<Homework> homework,
    IRepository<HomeworkSubmission> submissions,
    IRepository<Enrollment> enrollments,
    IRepository<StoredFile> files,
    IDateTimeService clock,
    IUnitOfWork unitOfWork) : ControllerBase
{
    private const long MaxRequestBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The family view: the student themself, or every child linked to the parent, each with
    /// groups, attendance, sessions, homework (own submission), exams (own result) and fees.
    /// </summary>
    [HttpGet("family")]
    [ProducesResponseType(typeof(Result<IReadOnlyList<FamilyStudentOverviewDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<FamilyStudentOverviewDto>>>> GetFamily(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMyFamilyOverviewQuery(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Uploads a homework answer (photo or document) for the signed-in student, or for one of the
    /// signed-in parent's children, and marks the homework as submitted. Refused once graded.
    /// </summary>
    [HttpPost("homework/{homeworkId:guid}/submission")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(Result<SubmissionFileDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<SubmissionFileDto>>> SubmitHomework(
        Guid homeworkId,
        [FromForm] SubmitHomeworkForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Ok(Result<SubmissionFileDto>.Failure("Files.NoTenantContext", "Uploads require a tenant context."));
        }

        var studentId = await ResolveStudentAsync(form.StudentProfileId, cancellationToken).ConfigureAwait(false);

        if (studentId is null)
        {
            return Ok(Result<SubmissionFileDto>.Failure("Students.NotFound", "Student not found."));
        }

        var item = await homework.GetByIdAsync(homeworkId, cancellationToken).ConfigureAwait(false);

        var enrolled = item is not null && await enrollments.Query
            .AnyAsync(
                e => e.StudyGroupId == item.StudyGroupId
                    && e.StudentProfileId == studentId.Value
                    && e.Status == EnrollmentStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        if (item is null || !enrolled)
        {
            return Ok(Result<SubmissionFileDto>.Failure("Homework.NotFound", "Homework not found."));
        }

        var file = form.File;

        if (file is null || file.Length == 0)
        {
            return Ok(Result<SubmissionFileDto>.Failure("Files.Empty", "No file was uploaded."));
        }

        var contentType = file.ContentType ?? string.Empty;
        var isImage = FileValidationRules.IsAllowedImage(contentType);

        if (!isImage && !FileValidationRules.IsAllowedDocument(contentType))
        {
            return Ok(Result<SubmissionFileDto>.Failure("Files.TypeNotAllowed", $"Files of type '{contentType}' are not accepted."));
        }

        var limit = isImage ? FileValidationRules.MaxImageSizeBytes : FileValidationRules.MaxDocumentSizeBytes;

        if (file.Length > limit)
        {
            return Ok(Result<SubmissionFileDto>.Failure("Files.TooLarge", $"The file exceeds the {limit / (1024 * 1024)} MB limit."));
        }

        var submission = await submissions.Query
            .FirstOrDefaultAsync(s => s.HomeworkId == item.Id && s.StudentProfileId == studentId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (submission is { Status: SubmissionStatus.Graded })
        {
            return Ok(Result<SubmissionFileDto>.Failure("Homework.AlreadyGraded", "This homework has already been graded."));
        }

        if (submission is null)
        {
            submission = new HomeworkSubmission { TenantId = tenantId, HomeworkId = item.Id, StudentProfileId = studentId.Value };
            await submissions.AddAsync(submission, cancellationToken).ConfigureAwait(false);
        }

        submission.Status = SubmissionStatus.Submitted;
        submission.SubmittedAt = clock.UtcNow;

        var note = string.IsNullOrWhiteSpace(form.Note) ? null : form.Note.Trim();

        if (note is { Length: > 500 })
        {
            note = note[..500];
        }

        // The storage service saves the shared unit of work, so the submission row and the file's
        // metadata are written together.
        await using var stream = file.OpenReadStream();

        var uploaded = await storage.UploadAsync(
                new FileUploadRequest(file.FileName, contentType, file.Length, stream),
                StoredFileCategory.AssignmentAttachment,
                tenantId,
                submission.Id,
                HomeworkFiles.OwnerName,
                note,
                cancellationToken)
            .ConfigureAwait(false);

        return Ok(Result<SubmissionFileDto>.Success(
            new SubmissionFileDto(uploaded.FileId, uploaded.OriginalFileName, uploaded.ContentType, uploaded.SizeInBytes, note, clock.UtcNow),
            "Homework submitted successfully."));
    }

    /// <summary>Removes one of the family's own uploaded answers while the homework is not graded.</summary>
    [HttpDelete("homework/{homeworkId:guid}/files/{fileId:guid}")]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<bool>>> DeleteHomeworkFile(Guid homeworkId, Guid fileId, CancellationToken cancellationToken)
    {
        var submission = await FindOwnSubmissionForFileAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (submission is null || submission.HomeworkId != homeworkId)
        {
            return Ok(Result<bool>.Failure("Files.NotFound", "File not found."));
        }

        if (submission.Status == SubmissionStatus.Graded)
        {
            return Ok(Result<bool>.Failure("Homework.AlreadyGraded", "This homework has already been graded."));
        }

        var deleted = await storage.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);

        // With the last answer gone the homework is no longer submitted.
        var remaining = await files.Query
            .AnyAsync(f => f.OwnerEntityName == HomeworkFiles.OwnerName && f.OwnerEntityId == submission.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!remaining)
        {
            submission.Status = SubmissionStatus.Pending;
            submission.SubmittedAt = null;
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ok(deleted
            ? Result<bool>.Success(true, "File deleted successfully.")
            : Result<bool>.Failure("Files.NotFound", "File not found."));
    }

    /// <summary>Downloads one of the family's own uploaded answers.</summary>
    [HttpGet("files/{fileId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadOwnFile(Guid fileId, CancellationToken cancellationToken)
    {
        var submission = await FindOwnSubmissionForFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        var entity = submission is null ? null : await files.GetByIdAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (entity is null)
        {
            return Ok(Result<bool>.Failure("Files.NotFound", "File not found."));
        }

        Stream content;

        try
        {
            content = await storage.OpenReadAsync(fileId, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return Ok(Result<bool>.Failure("Files.NotFound", "File content not found."));
        }

        return File(content, entity.ContentType, entity.OriginalFileName);
    }

    /// <summary>
    /// The student the caller acts for: the requested one when it belongs to the caller's family,
    /// otherwise the only student of the family; null when none (or ambiguous).
    /// </summary>
    private async Task<Guid?> ResolveStudentAsync(Guid? requested, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var family = await dataScope.FamilyStudentIdsAsync(userId, cancellationToken).ConfigureAwait(false);

        if (requested is { } id)
        {
            return family.Contains(id) ? id : null;
        }

        return family.Count == 1 ? family[0] : null;
    }

    /// <summary>The submission owning <paramref name="fileId"/>, when it belongs to the caller's family.</summary>
    private async Task<HomeworkSubmission?> FindOwnSubmissionForFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var file = await files.GetByIdAsync(fileId, cancellationToken).ConfigureAwait(false);

        if (file is not { OwnerEntityName: HomeworkFiles.OwnerName, OwnerEntityId: { } submissionId })
        {
            return null;
        }

        var submission = await submissions.GetByIdAsync(submissionId, cancellationToken).ConfigureAwait(false);

        if (submission is null)
        {
            return null;
        }

        var family = await dataScope.FamilyStudentIdsAsync(userId, cancellationToken).ConfigureAwait(false);

        return family.Contains(submission.StudentProfileId) ? submission : null;
    }
}

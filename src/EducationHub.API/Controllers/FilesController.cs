using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Entities.Students;
using EducationHub.Domain.Entities.Teachers;
using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Files;
using EducationHub.Shared.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.API.Controllers;

/// <summary>Metadata of one stored file.</summary>
public sealed record StoredFileDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long SizeInBytes,
    StoredFileCategory Category,
    Guid? OwnerEntityId,
    string? OwnerEntityName,
    string? Description,
    bool IsImage,
    DateTime CreatedAt)
{
    public static StoredFileDto FromEntity(StoredFile file)
        => new(
            file.Id,
            file.OriginalFileName,
            file.ContentType,
            file.SizeInBytes,
            file.Category,
            file.OwnerEntityId,
            file.OwnerEntityName,
            file.Description,
            file.IsImage,
            file.CreatedAt);
}

/// <summary>Multipart upload form for <c>POST /api/files</c>.</summary>
public sealed class UploadFileForm
{
    public IFormFile? File { get; set; }

    public StoredFileCategory Category { get; set; } = StoredFileCategory.StudentDocument;

    /// <summary>What the file is attached to: <c>Student</c>, <c>Parent</c> or <c>Teacher</c>.</summary>
    public string? OwnerEntityName { get; set; }

    public Guid? OwnerEntityId { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// File upload and download (BL-005, Mission Phase 2.5b) over <see cref="IFileStorageService"/>.
/// </summary>
/// <remarks>
/// Every lookup goes through the tenant-filtered <c>StoredFiles</c> set, so another tenant's
/// file id answers 404. An owner reference is checked to exist in the caller's tenant before
/// anything is written, so a file can never be attached to someone else's record.
/// </remarks>
[ApiController]
[Route("api/files")]
[Authorize]
[Produces("application/json")]
public sealed class FilesController(
    IFileStorageService storage,
    IRepository<StoredFile> files,
    IRepository<StudentProfile> students,
    IRepository<ParentProfile> parents,
    IRepository<TeacherProfile> teachers,
    ITenantContext tenantContext) : ControllerBase
{
    private const long MaxRequestBytes = 25L * 1024 * 1024;

    private static readonly string[] OwnerNames = ["Student", "Parent", "Teacher"];

    /// <summary>Lists files, optionally only those attached to one owner.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstants.FilesView)]
    [ProducesResponseType(typeof(Result<IReadOnlyList<StoredFileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Result<IReadOnlyList<StoredFileDto>>>> List(
        [FromQuery] string? ownerEntityName = null,
        [FromQuery] Guid? ownerEntityId = null,
        CancellationToken cancellationToken = default)
    {
        var query = files.Query;

        if (!string.IsNullOrWhiteSpace(ownerEntityName))
        {
            query = query.Where(f => f.OwnerEntityName == ownerEntityName);
        }

        if (ownerEntityId.HasValue)
        {
            query = query.Where(f => f.OwnerEntityId == ownerEntityId);
        }

        var rows = await query
            .OrderByDescending(static f => f.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(Result<IReadOnlyList<StoredFileDto>>.Success(rows.Select(StoredFileDto.FromEntity).ToList()));
    }

    /// <summary>Uploads one file (images up to 5 MB, documents up to 20 MB).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionConstants.FilesUpload)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType(typeof(Result<StoredFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Result<StoredFileDto>>> Upload(
        [FromForm] UploadFileForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return Ok(Result<StoredFileDto>.Failure("Files.NoTenantContext", "Uploads require a tenant context."));
        }

        var file = form.File;

        if (file is null || file.Length == 0)
        {
            return Ok(Result<StoredFileDto>.Failure("Files.Empty", "No file was uploaded."));
        }

        var contentType = file.ContentType ?? string.Empty;
        var isImage = FileValidationRules.IsAllowedImage(contentType);

        if (!isImage && !FileValidationRules.IsAllowedDocument(contentType))
        {
            return Ok(Result<StoredFileDto>.Failure("Files.TypeNotAllowed", $"Files of type '{contentType}' are not accepted."));
        }

        var limit = isImage ? FileValidationRules.MaxImageSizeBytes : FileValidationRules.MaxDocumentSizeBytes;

        if (file.Length > limit)
        {
            return Ok(Result<StoredFileDto>.Failure("Files.TooLarge", $"The file exceeds the {limit / (1024 * 1024)} MB limit."));
        }

        var ownerName = string.IsNullOrWhiteSpace(form.OwnerEntityName) ? null : form.OwnerEntityName.Trim();

        if (ownerName is not null || form.OwnerEntityId.HasValue)
        {
            var match = OwnerNames.FirstOrDefault(n => string.Equals(n, ownerName, StringComparison.OrdinalIgnoreCase));

            if (match is null || form.OwnerEntityId is not { } ownerId)
            {
                return Ok(Result<StoredFileDto>.Failure("Files.InvalidOwner", "Owner must be Student, Parent or Teacher with an id."));
            }

            var exists = match switch
            {
                "Student" => await students.ExistsAsync(ownerId, cancellationToken).ConfigureAwait(false),
                "Parent" => await parents.ExistsAsync(ownerId, cancellationToken).ConfigureAwait(false),
                _ => await teachers.ExistsAsync(ownerId, cancellationToken).ConfigureAwait(false)
            };

            if (!exists)
            {
                return Ok(Result<StoredFileDto>.Failure("Files.OwnerNotFound", "The owner record was not found."));
            }

            ownerName = match;
        }

        await using var stream = file.OpenReadStream();

        var uploaded = await storage.UploadAsync(
                new FileUploadRequest(file.FileName, contentType, file.Length, stream),
                form.Category,
                tenantId,
                form.OwnerEntityId,
                ownerName,
                string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        var entity = await files.GetByIdAsync(uploaded.FileId, cancellationToken).ConfigureAwait(false);

        return entity is null
            ? Ok(Result<StoredFileDto>.Failure("Files.NotFound", "The file was not found after upload."))
            : Ok(Result<StoredFileDto>.Success(StoredFileDto.FromEntity(entity), "File uploaded successfully."));
    }

    /// <summary>Streams the file's content.</summary>
    [HttpGet("{id:guid}/content")]
    [Authorize(Policy = PermissionConstants.FilesView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var entity = await files.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (entity is null)
        {
            return Ok(Result<bool>.Failure("Files.NotFound", "File not found."));
        }

        Stream content;

        try
        {
            content = await storage.OpenReadAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return Ok(Result<bool>.Failure("Files.NotFound", "File content not found."));
        }

        return File(content, entity.ContentType, entity.OriginalFileName);
    }

    /// <summary>Deletes a file (metadata soft-deleted, bytes removed).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionConstants.FilesDelete)]
    [ProducesResponseType(typeof(Result<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Result<bool>>> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await storage.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return Ok(deleted
            ? Result<bool>.Success(true, "File deleted successfully.")
            : Result<bool>.Failure("Files.NotFound", "File not found."));
    }
}

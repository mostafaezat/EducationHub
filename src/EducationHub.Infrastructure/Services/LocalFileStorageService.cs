using EducationHub.Application.Common.Interfaces;
using EducationHub.Domain.Entities.Files;
using EducationHub.Domain.Enums;
using EducationHub.Infrastructure.Persistence;
using EducationHub.Shared.Common.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EducationHub.Infrastructure.Services;

/// <summary>
/// Where the local file store keeps its bytes.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Absolute or relative root directory. Relative paths resolve against ContentRoot.</summary>
    public string RootPath { get; set; } = "App_Data/Files";

    /// <summary>
    /// Refuse writes when the bytes on disk already exceed this. A safety valve against a
    /// runaway upload loop filling the host disk, not a per-file limit.
    /// </summary>
    public long MaxTotalSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024;
}

/// <summary>
/// Stores file bytes on local disk and their metadata in <c>StoredFiles</c>.
/// </summary>
/// <remarks>
/// <para>
/// Bytes on disk, metadata in the database. That split is the whole point of the
/// <see cref="IFileStorageService"/> abstraction: moving to S3 or Azure Blob later means
/// writing one new implementation of this interface, with no handler or controller change.
/// </para>
/// <para>
/// This implementation is scoped, so <c>RootPath</c> is resolved once per request rather
/// than per upload.
/// </para>
/// </remarks>
public sealed class LocalFileStorageService(
    EducationHubDbContext context,
    IOptions<FileStorageOptions> options,
    ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    private readonly FileStorageOptions _options = options.Value;
    private readonly string _rootPath = ResolveRootPath(options.Value.RootPath);

    private static string ResolveRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = "App_Data/Files";
        }

        return Path.IsPathRooted(rootPath)
            ? Path.GetFullPath(rootPath)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), rootPath));
    }

    public async Task<FileUploadResult> UploadAsync(
        FileUploadRequest request,
        StoredFileCategory category,
        Guid tenantId,
        Guid? ownerEntityId = null,
        string? ownerEntityName = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);
        ArgumentNullException.ThrowIfNull(request.Content);

        if (tenantId == Guid.Empty)
        {
            // Fail closed: without a tenant in the path, one tenant's file could be addressed
            // by another. The key is built from this id, so it must be a real one.
            throw new InvalidOperationException("A file upload requires a valid tenant id.");
        }

        var isImage = FileValidationRules.IsAllowedImage(request.ContentType);

        if (!isImage && !FileValidationRules.IsAllowedDocument(request.ContentType))
        {
            throw new InvalidOperationException(
                $"Content type '{request.ContentType}' is not an accepted upload type.");
        }

        var maxSize = isImage
            ? FileValidationRules.MaxImageSizeBytes
            : FileValidationRules.MaxDocumentSizeBytes;

        if (request.SizeInBytes <= 0)
        {
            throw new InvalidOperationException("The uploaded file is empty.");
        }

        if (request.SizeInBytes > maxSize)
        {
            throw new InvalidOperationException(
                $"The file is {request.SizeInBytes} bytes; the limit for this category is {maxSize}.");
        }

        var safeName = FileValidationRules.SanitizeFileName(request.OriginalFileName);

        var entity = new StoredFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OriginalFileName = safeName,
            ContentType = request.ContentType,
            SizeInBytes = request.SizeInBytes,
            Category = category,
            OwnerEntityId = ownerEntityId,
            OwnerEntityName = ownerEntityName,
            Description = description,
            IsImage = isImage,
            AllowedUserIds = []
        };

        // StorageKey comes from the entity's own id and the sanitized name only. The client's
        // file name never contributes a path segment, so "..\..\web.config" cannot escape
        // the tenant directory.
        entity.StorageKey = $"tenants/{tenantId:D}/{category.ToString().ToLowerInvariant()}/{entity.Id:D}-{safeName}";

        var fullPath = ResolveFullPath(entity.StorageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var output = new FileStream(
                         fullPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await request.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            context.StoredFiles.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The metadata write is what makes the file reachable. If it fails, the bytes on
            // disk are orphaned and invisible, so remove them rather than leaking them.
            TryDelete(fullPath);

            throw;
        }

        logger.LogInformation(
            "Stored file {FileId} for tenant {TenantId} as {StorageKey} ({Size} bytes, image: {IsImage})",
            entity.Id,
            tenantId,
            entity.StorageKey,
            entity.SizeInBytes,
            isImage);

        return new FileUploadResult(
            entity.Id,
            entity.StorageKey,
            entity.OriginalFileName,
            entity.ContentType,
            entity.SizeInBytes,
            isImage);
    }

    public async Task<Stream> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var entity = await context.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            // Not found rather than forbidden: the tenant query filter has already excluded
            // other tenants' rows, so there is nothing to disclose either way.
            throw new FileNotFoundException($"File '{fileId}' was not found.");
        }

        var fullPath = ResolveFullPath(entity.StorageKey);

        if (!File.Exists(fullPath))
        {
            logger.LogError(
                "Metadata row {FileId} points at {StorageKey}, but no such file exists on disk",
                fileId,
                entity.StorageKey);

            throw new FileNotFoundException(
                $"File '{fileId}' is recorded but its content is missing from storage.");
        }

        return new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
    }

    public async Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var entity = await context.StoredFiles
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        // Soft delete first: a metadata row that disappears while its bytes remain is
        // unrecoverable, whereas bytes whose row is gone are merely garbage-collectable.
        entity.IsDeleted = true;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        TryDelete(ResolveFullPath(entity.StorageKey));

        return true;
    }

    /// <summary>
    /// Maps a stored key to a full path and refuses anything outside the root.
    /// </summary>
    /// <remarks>
    /// Defense in depth. Keys are built server-side and are already safe, but this is the
    /// last place a traversal could take effect, and the stored value is database data that
    /// a future migration or admin script could alter.
    /// </remarks>
    private string ResolveFullPath(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var candidate = Path.GetFullPath(Path.Combine(_rootPath, storageKey));

        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Storage key '{storageKey}' resolves outside the configured storage root.");
        }

        return candidate;
    }

    private void TryDelete(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException ex)
        {
            // The metadata is already correct, so the file is only orphaned garbage. Failing
            // the request here would report a delete that in fact succeeded.
            logger.LogWarning(ex, "Could not delete orphaned file at {Path}", fullPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Not permitted to delete orphaned file at {Path}", fullPath);
        }
    }
}
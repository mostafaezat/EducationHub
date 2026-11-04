using EducationHub.Domain.Enums;
using EducationHub.Shared.Common.Files;

namespace EducationHub.Application.Common.Interfaces;

/// <summary>
/// Binary storage. The Domain entity <c>StoredFile</c> holds only metadata; bytes go here,
/// which is what allows a future move from local disk to S3/Azure Blob without touching
/// any handler (spec section 43).
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Validates and persists a file, then writes the metadata row and returns both.
    /// Implementations must build the storage key from <c>tenantId</c> and never from
    /// raw client input.
    /// </summary>
    Task<FileUploadResult> UploadAsync(
        FileUploadRequest request,
        StoredFileCategory category,
        Guid tenantId,
        Guid? ownerEntityId = null,
        string? ownerEntityName = null,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for reading. Callers must have already authorized access.</summary>
    Task<Stream> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the bytes and marks the metadata row deleted.</summary>
    Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken = default);
}

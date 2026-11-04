using EducationHub.Domain.Common;
using EducationHub.Domain.Enums;

namespace EducationHub.Domain.Entities.Files;

/// <summary>
/// Metadata for a file held in <c>IFileStorageService</c>. The bytes never live in the
/// database (spec section 43); only the addressing and ownership information does.
/// </summary>
public class StoredFile : TenantOwnedEntity
{
    /// <summary>Path relative to the storage root, e.g. <c>tenants/{id}/students/{id}/...</c>.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeInBytes { get; set; }

    public StoredFileCategory Category { get; set; }

    /// <summary>Owning domain entity id (student, teacher, payment, ...).</summary>
    public Guid? OwnerEntityId { get; set; }

    public string? OwnerEntityName { get; set; }

    public string? Description { get; set; }

    public bool IsImage { get; set; }

    /// <summary>
    /// Users allowed to download this file beyond the tenant boundary. Empty means
    /// "anyone in the tenant who holds the relevant permission".
    /// </summary>
    public Guid[] AllowedUserIds { get; set; } = [];

    public string ToEntityPath() => $"tenants/{TenantId:D}/{Category.ToString().ToLowerInvariant()}/{Id:D}-{OriginalFileName}";
}

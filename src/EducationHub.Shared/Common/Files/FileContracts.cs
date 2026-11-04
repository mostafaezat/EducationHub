namespace EducationHub.Shared.Common.Files;

/// <summary>
/// What the API needs to know about an uploaded file, and what the client receives back.
/// This is a DTO contract, not an entity - <c>StoredFile</c> lives in the Domain layer.
/// </summary>
public sealed record FileUploadRequest(
    string OriginalFileName,
    string ContentType,
    long SizeInBytes,
    Stream Content);

public sealed record FileUploadResult(
    Guid FileId,
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long SizeInBytes,
    bool IsImage);

/// <summary>
/// File acceptance policy, applied by the API before any bytes are written to disk so an
/// oversized or disallowed upload never touches storage.
/// </summary>
public static class FileValidationRules
{
    public static readonly string[] AllowedImageContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public static readonly string[] AllowedDocumentContentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain",
        "text/csv"
    ];

    /// <summary>Tenant logo. Square-ish and small, so it can be cached aggressively.</summary>
    public const long MaxImageSizeBytes = 5L * 1024 * 1024;

    /// <summary>General attachment ceiling.</summary>
    public const long MaxDocumentSizeBytes = 20L * 1024 * 1024;

    public static bool IsAllowedImage(string? contentType)
        => contentType is not null && AllowedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedDocument(string? contentType)
        => contentType is not null && AllowedDocumentContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Strips directory components and anything unsafe from a client-supplied name before
    /// it is used to build a storage path.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "file";
        }

        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Where(c => !invalid.Contains(c))]);

        cleaned = cleaned.Trim().TrimStart('.');

        if (cleaned.Length == 0)
        {
            return "file";
        }

        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }
}

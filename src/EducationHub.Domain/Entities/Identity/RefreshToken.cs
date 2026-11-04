using EducationHub.Domain.Common;

namespace EducationHub.Domain.Entities.Identity;

/// <summary>
/// A rotating refresh token. Only the SHA-256 hash is stored, so a database leak cannot
/// be replayed against the token endpoint.
/// </summary>
/// <remarks>
/// Rotation with reuse detection (spec section 52): each successful refresh revokes the
/// presented token and issues a replacement, linked through <see cref="ReplacedByTokenId"/>.
/// If an already-rotated token is ever presented again, that is the signature of a stolen
/// token, so <see cref="RevokedAt"/> is stamped on the entire family - see
/// <c>RefreshTokenHandler</c>.
/// </remarks>
public class RefreshToken : TenantOwnedEntity
{
    public Guid UserId { get; set; }

    /// <summary>Hex-encoded SHA-256 hash. The raw token is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Inherited from <see cref="Common.AuditableEntity"/>. Set when the entity is
    /// constructed, which is what makes the lifetime of a rotated token computable before
    /// the row is ever written.
    /// </summary>
    public DateTime IssuedAt => CreatedAt;

    public DateTime? RevokedAt { get; set; }

    /// <summary>Why this token was revoked: <c>Rotated</c>, <c>ReuseDetected</c>, <c>Logout</c>, <c>RevokedByUser</c>.</summary>
    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>
    /// Groups every token descended from a single login, so a detected reuse can revoke
    /// the whole chain in one statement.
    /// </summary>
    public Guid FamilyId { get; set; }

    public bool IsActive(DateTime utcNow)
        => !IsDeleted && RevokedAt is null && ExpiresAt > utcNow;

    public ApplicationUser User { get; set; } = null!;
}

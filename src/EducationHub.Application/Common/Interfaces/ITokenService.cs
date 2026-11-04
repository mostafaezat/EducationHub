using EducationHub.Domain.Entities.Identity;

namespace EducationHub.Application.Common.Interfaces;

/// <summary>An access token together with the instant it stops being valid.</summary>
public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// A freshly generated refresh token. <see cref="RawToken"/> goes to the client and is
/// never persisted; <see cref="TokenHash"/> is what the database stores.
/// </summary>
public sealed record RefreshTokenMaterial(string RawToken, string TokenHash, DateTime ExpiresAtUtc);

/// <summary>
/// Token minting and hashing. Implemented in Infrastructure because signing requires the
/// JWT key material and hashing requires the crypto stack; declared here so the Auth
/// handlers never reference either.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Builds a short-lived (15 min) access token carrying the user id, tenant id, roles
    /// and permissions. The signed <c>TenantId</c> claim is the authoritative tenant for
    /// the whole request.
    /// </summary>
    AccessTokenResult CreateAccessToken(
        ApplicationUser user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);

    /// <summary>Generates a cryptographically random refresh token and its stored hash.</summary>
    RefreshTokenMaterial CreateRefreshToken();

    /// <summary>SHA-256 hash used for at-rest storage and lookup of a presented token.</summary>
    string HashRefreshToken(string rawToken);

    /// <summary>
    /// Constant-time comparison of a presented token against a stored hash, so a timing
    /// side channel cannot be used to guess tokens.
    /// </summary>
    bool VerifyRefreshToken(string rawToken, string storedHash);
}

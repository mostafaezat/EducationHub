using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Application.Features.Auth;
using EducationHub.Domain.Entities.Identity;
using EducationHub.Shared.Common.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EducationHub.Infrastructure.Services;

/// <summary>
/// Signing key material for access tokens.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "EducationHub";

    public string Audience { get; set; } = "EducationHub.Web";

    /// <summary>
    /// Symmetric signing key. Must be at least 32 bytes, because HMAC-SHA256 silently
    /// accepts shorter keys and fails only at runtime under load.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Overrides <see cref="AuthConstants.AccessTokenLifetime"/>. Present so tests can issue
    /// already-expired tokens without waiting.
    /// </summary>
    public TimeSpan? AccessTokenLifetimeOverride { get; set; }

    /// <summary>Bytes of entropy in a refresh token.</summary>
    public const int RefreshTokenByteLength = 32;

    /// <summary>
    /// Resolves the signing key from configuration, preferring the <c>Jwt:SigningKey</c> path
    /// and falling back to the canonical <c>EducationHub__Jwt__SigningKey</c> environment
    /// variable (read in its <c>EducationHub:Jwt:SigningKey</c> key form). The key is
    /// deliberately NOT stored in appsettings: a committed symmetric key would let anyone who
    /// reads the repository forge access tokens.
    /// </summary>
    public static string? ReadSigningKey(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration["Jwt:SigningKey"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration["EducationHub:Jwt:SigningKey"];
        }

        var trimmed = raw?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Fails at startup rather than when the first token is issued.
    /// </summary>
    /// <remarks>
    /// <see cref="JwtTokenService"/> performs the same checks at resolve time; this method
    /// exists so <c>Program.cs</c> can reject a missing, short or copied-placeholder key
    /// before the host starts serving traffic.
    /// </remarks>
    public static void ValidateSigningKey(string? signingKey)
    {
        var key = signingKey?.Trim();

        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set the EducationHub__Jwt__SigningKey " +
                "environment variable (or Jwt:SigningKey in configuration); the API cannot " +
                "issue or validate tokens without it.");
        }

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least 32 bytes for HMAC-SHA256; got {Encoding.UTF8.GetByteCount(key)}.");
        }

        if (IsPlaceholderKey(key))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey matches a known placeholder value. Generate a real, random key " +
                "(at least 32 bytes) and set it via the EducationHub__Jwt__SigningKey environment variable.");
        }
    }

    private static bool IsPlaceholderKey(string key)
    {
        var lower = key.ToLowerInvariant();

        return lower.Contains("replace-me", StringComparison.Ordinal)
            || lower.Contains("change-me", StringComparison.Ordinal);
    }
}

/// <summary>
/// Issues and verifies the JWT access token, and mints refresh tokens.
/// </summary>
/// <remarks>
/// Lives in Infrastructure because signing needs the key material and hashing needs the
/// crypto stack; the Application layer sees only <see cref="ITokenService"/>.
/// </remarks>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly IDateTimeService _dateTime;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeService dateTime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dateTime);

        _options = options.Value;
        _dateTime = dateTime;

        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it in appsettings (user secrets locally, " +
                "an environment variable in production) - the API cannot issue tokens without it.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);

        // HMAC-SHA256 accepts shorter keys and fails only when signing, which in practice
        // means a production outage rather than a startup error. Checked here instead.
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least 32 bytes for HMAC-SHA256; got {keyBytes.Length}.");
        }

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult CreateAccessToken(
        ApplicationUser user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);

        var issuedAt = _dateTime.UtcNow;
        var lifetime = _options.AccessTokenLifetimeOverride ?? AuthConstants.AccessTokenLifetime;
        var expiresAt = issuedAt.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email ?? user.UserName ?? string.Empty),
            new(EducationHubClaimTypes.UserId, user.Id.ToString()),
            new(EducationHubClaimTypes.FullName, user.FullName ?? string.Empty),
            new(EducationHubClaimTypes.TenantId, user.TenantId.ToString("D", CultureInfo.InvariantCulture)),
            new(EducationHubClaimTypes.PreferredLanguage, user.PreferredLanguage),
            new(
                JwtRegisteredClaimNames.Iat,
                EpochSeconds(issuedAt).ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        if (user.BranchId is { } branchId)
        {
            claims.Add(new Claim(EducationHubClaimTypes.BranchId, branchId.ToString("D", CultureInfo.InvariantCulture)));
        }

        // A password-change obligation rides the token rather than being re-read from the
        // database on every request. It is written only when set: presence of the claim is
        // the gate, so an unencumbered token carries nothing to strip.
        if (user.MustChangePassword)
        {
            claims.Add(new Claim(EducationHubClaimTypes.MustChangePassword, "true"));
        }

        // Roles and permissions are embedded so that authorization needs no database round
        // trip on the hot path. The 15-minute lifetime is what bounds the staleness this
        // introduces; handlers that enforce a privilege boundary re-read from the database
        // rather than trusting these claims.
        claims.AddRange(roles
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => new Claim(ClaimTypes.Role, r)));

        claims.AddRange(permissions
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => new Claim(EducationHubClaimTypes.PermissionClaimType, p)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: _credentials);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshTokenMaterial CreateRefreshToken()
    {
        // 32 bytes of CSPRNG entropy, URL-safe so it can travel in JSON without escaping.
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(JwtOptions.RefreshTokenByteLength));

        return new RefreshTokenMaterial(raw, HashRefreshToken(raw), _dateTime.UtcNow.Add(AuthConstants.RefreshTokenLifetime));
    }

    /// <summary>
    /// SHA-256 of the presented token.
    /// </summary>
    /// <remarks>
    /// A plain hash rather than a slow KDF on purpose. The input already has 256 bits of
    /// entropy, so there is no dictionary to brute-force, and bcrypt/argon2 would make every
    /// refresh needlessly expensive without adding protection here.
    /// </remarks>
    public string HashRefreshToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    /// <summary>
    /// Constant-time comparison, so response timing cannot reveal a matching prefix.
    /// </summary>
    public bool VerifyRefreshToken(string rawToken, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var computed = Encoding.UTF8.GetBytes(HashRefreshToken(rawToken));
        var stored = Encoding.UTF8.GetBytes(storedHash);

        // Length is compared separately: FixedTimeEquals requires equal-length inputs and
        // throws otherwise, and a length mismatch is public information anyway since both
        // sides are fixed-width hex.
        if (computed.Length != stored.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }

    private static long EpochSeconds(DateTime instant)
        => new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
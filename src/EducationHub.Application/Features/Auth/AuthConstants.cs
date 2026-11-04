namespace EducationHub.Application.Features.Auth;

/// <summary>
/// Shared authentication constants, kept next to the auth feature because both the
/// handlers and the Infrastructure token service need them.
/// </summary>
public static class AuthConstants
{
    /// <summary>Authorization header scheme. Bearer only - no cookies, no query strings.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>Where a refresh token lives inside <see cref="UserProfileDto"/>'s sibling response.</summary>
    public const string RefreshTokenPurpose = "EducationHub.RefreshToken";

    /// <summary>
    /// Absolute lifetime of a refresh token. Deliberately shorter than a "remember me"
    /// session because every use rotates it, so an attacker holding one stolen token gets
    /// a single use before it is burned.
    /// </summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>Lifetime of the rotating refresh token when "remember me" was not ticked.</summary>
    public static readonly TimeSpan SessionRefreshTokenLifetime = TimeSpan.FromHours(12);

    /// <summary>Access token lifetime. Short by design; it is not renewable.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Minimum seconds between two login history rows for the same account.</summary>
    public const int FailedLoginLockoutThreshold = 5;

    /// <summary>How long an account stays locked after reaching the failed-login threshold.</summary>
    public static readonly TimeSpan FailedLoginLockoutDuration = TimeSpan.FromMinutes(15);
}

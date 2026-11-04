namespace EducationHub.API.Auth;

/// <summary>
/// Options for the credential-entry rate limiter (login / refresh). Bound from the
/// <c>Auth:RateLimit</c> section; intended as a coarse, in-memory per-IP guard on top of the
/// per-account lockout, so values are deliberately conservative for shared-NAT deployments.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "Auth:RateLimit";

    /// <summary>Maximum number of credential attempts per window, per remote IP.</summary>
    public int PermitLimit { get; set; } = 5;

    /// <summary>The duration of the fixed window (parsed from ISO-8601, e.g. "00:05:00").</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}
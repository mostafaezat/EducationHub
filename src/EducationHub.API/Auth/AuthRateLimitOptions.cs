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

    /// <summary>Password reset by mobile number (<c>Auth:RateLimit:PhoneReset</c>).</summary>
    public PhoneResetRateLimitOptions PhoneReset { get; set; } = new();

    /// <summary>Anonymous pre-registration requests (<c>Auth:RateLimit:PublicRegistration</c>).</summary>
    public WindowRateLimitOptions PublicRegistration { get; set; } = new() { PermitLimit = 5, Window = TimeSpan.FromHours(1) };
}

/// <summary>A fixed window: at most <see cref="PermitLimit"/> requests per <see cref="Window"/>.</summary>
public class WindowRateLimitOptions
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; }
}

/// <summary>
/// Limits for the two phone-reset endpoints. Each endpoint has its own budget per remote IP
/// (<see cref="WindowRateLimitOptions.PermitLimit"/>, higher because carrier NAT puts many
/// parents behind one address) and per mobile number (<see cref="PerPhonePermitLimit"/>), which
/// is what actually stops guessing a 6-digit code or flooding one parent with WhatsApp messages.
/// </summary>
public sealed class PhoneResetRateLimitOptions : WindowRateLimitOptions
{
    public PhoneResetRateLimitOptions()
    {
        PermitLimit = 10;
        Window = TimeSpan.FromMinutes(15);
    }

    public int PerPhonePermitLimit { get; set; } = 3;
}

using System.Globalization;
using System.Threading.RateLimiting;
using EducationHub.API.Localization;
using EducationHub.Application.Common.Helpers;
using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace EducationHub.API.Auth;

/// <summary>Names of the ASP.NET rate-limiter policies used with <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicies
{
    public const string PhoneReset = "auth-phone-reset";

    /// <summary>
    /// Token refresh, per remote IP. Separate from the credential limiter on purpose: a refresh
    /// token is a 256-bit random value, not a guessable credential, and every signed-in user of a
    /// centre behind one router refreshes every 15 minutes - sharing the login budget logged
    /// them out (QA-01).
    /// </summary>
    public const string Refresh = "auth-refresh";

    public const string PublicRegistration = "public-registration";

    /// <summary>
    /// Login / forgot-password / resend-confirmation, per remote IP: only the loose backstop
    /// (<c>Auth:RateLimit:PerIp</c>). The real budget is per IP + login name
    /// (<see cref="CredentialRateLimiter"/>), so colleagues behind one router do not block each other.
    /// </summary>
    public const string Credential = "auth-credential";
}

/// <summary>Writes the 429 every limiter in the API answers with.</summary>
internal static class RateLimitResponses
{
    /// <summary>
    /// The Retry-After value, in whole seconds, from the limiter's own lease (the time left in the
    /// current window), falling back to <paramref name="fallback"/> when the lease carries none.
    /// </summary>
    public static int RetryAfterSeconds(RateLimitLease lease, TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var retry = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : fallback;

        return Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds));
    }

    /// <summary>The credential 429 ("too many attempts").</summary>
    public static Task WriteAsync(HttpContext httpContext, int retryAfterSeconds, CancellationToken cancellationToken)
        => WriteAsync(
            httpContext,
            retryAfterSeconds,
            LocalizationKeys.RateLimited,
            "Too many credential attempts from this address. Try again shortly.",
            cancellationToken);

    /// <summary>
    /// A 429 carrying <paramref name="messageKey"/>; a <c>{0}</c> in the message is replaced with
    /// the wait in whole minutes.
    /// </summary>
    public static async Task WriteAsync(
        HttpContext httpContext,
        int retryAfterSeconds,
        string messageKey,
        string fallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        // The ASP.NET limiter runs before UseAuthentication, so no request culture has been
        // applied yet and there is no token to read a language preference from. The catalog is
        // read directly with Accept-Language (falling back to Arabic) instead.
        var store = httpContext.RequestServices.GetRequiredService<JsonResourceStore>();
        var template = BoundaryMessages.Resolve(store, httpContext, messageKey, fallback);
        var minutes = Math.Max(1, (int)Math.Ceiling(retryAfterSeconds / 60.0));

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,

                // Kept as a fixed string: the 429 is identified by status and Retry-After,
                // and this title is not part of what carries the translation.
                Title = "Too many attempts",
                Detail = string.Format(CultureInfo.InvariantCulture, template, minutes)
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A fixed-window budget per key, for limits whose key (a mobile number, a login name) is in the
/// request body: the ASP.NET rate limiter partitions on the raw request before model binding and
/// cannot see it, so these run as action filters on the bound argument instead. In-memory and per
/// instance, like the IP limiters.
/// </summary>
public abstract class KeyedWindowRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly TimeSpan _window;

    protected KeyedWindowRateLimiter(int permitLimit, TimeSpan window)
    {
        _window = window;
        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = Math.Max(1, permitLimit),
                QueueLimit = 0
            }));
    }

    /// <summary>Takes one permit for <paramref name="key"/>; false, with the wait, when the budget is spent.</summary>
    protected bool TryAcquireKey(string key, out int retryAfterSeconds)
    {
        using var lease = _limiter.AttemptAcquire(key);
        retryAfterSeconds = lease.IsAcquired ? 0 : RateLimitResponses.RetryAfterSeconds(lease, _window);

        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _limiter.Dispose();
        GC.SuppressFinalize(this);
    }

    protected static AuthRateLimitOptions Limits(IOptions<AuthRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Value;
    }
}

/// <summary>The per-mobile-number budget of the phone password-reset endpoints.</summary>
public sealed class PhoneResetRateLimiter(IOptions<AuthRateLimitOptions> options)
    : KeyedWindowRateLimiter(Limits(options).PhoneReset.PerPhonePermitLimit, Limits(options).PhoneReset.Window)
{
    /// <summary>Takes one permit for the endpoint + number.</summary>
    public bool TryAcquire(string endpoint, string canonicalPhone, out int retryAfterSeconds)
        => TryAcquireKey($"{endpoint}:{canonicalPhone}", out retryAfterSeconds);
}

/// <summary>
/// The per IP + login name budget of the credential endpoints (<c>Auth:RateLimit:PermitLimit</c>
/// per <c>Window</c>). Keyed on both, so one person's typos do not lock out a colleague behind the
/// same router, while guessing one account's password from one address stays capped. The loose
/// per-IP backstop against spraying many names is the <see cref="RateLimitPolicies.Credential"/> policy.
/// </summary>
public sealed class CredentialRateLimiter(IOptions<AuthRateLimitOptions> options)
    : KeyedWindowRateLimiter(Limits(options).PermitLimit, Limits(options).Window)
{
    /// <summary>Takes one permit for the endpoint + remote IP + login name.</summary>
    public bool TryAcquire(string endpoint, string remoteIp, string loginName, out int retryAfterSeconds)
        => TryAcquireKey($"{endpoint}|{remoteIp}|{loginName}", out retryAfterSeconds);

    /// <summary>
    /// One key per account whatever its spelling: a mobile number in any accepted format maps to
    /// its canonical form, anything else is trimmed and case-folded.
    /// </summary>
    public static string NormalizeLoginName(string? value)
        => PhoneNumberFormat.ToCanonical(value) ?? (value ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>
/// Applies <see cref="PhoneResetRateLimiter"/> to an action whose body has a <c>Phone</c>
/// property. A missing or malformed number passes through so validation can reject it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PhoneRateLimitAttribute(string endpoint) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var phone = context.ActionArguments.Values
            .Select(static v => v?.GetType().GetProperty("Phone")?.GetValue(v) as string)
            .FirstOrDefault(static p => p is not null);

        var canonical = PhoneNumberFormat.ToCanonical(phone);

        if (canonical is not null)
        {
            var limiter = context.HttpContext.RequestServices.GetRequiredService<PhoneResetRateLimiter>();

            if (!limiter.TryAcquire(endpoint, canonical, out var retryAfter))
            {
                await RateLimitResponses
                    .WriteAsync(context.HttpContext, retryAfter, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                context.Result = new EmptyResult();
                return;
            }
        }

        await next().ConfigureAwait(false);
    }
}

/// <summary>
/// Applies <see cref="CredentialRateLimiter"/> to a credential action whose body has an
/// <c>Email</c> property (an e-mail address or, for login, a mobile number).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CredentialRateLimitAttribute(string endpoint) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var login = context.ActionArguments.Values
            .Select(static v => v?.GetType().GetProperty("Email")?.GetValue(v) as string)
            .FirstOrDefault(static p => p is not null);

        var limiter = context.HttpContext.RequestServices.GetRequiredService<CredentialRateLimiter>();
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!limiter.TryAcquire(endpoint, ip, CredentialRateLimiter.NormalizeLoginName(login), out var retryAfter))
        {
            await RateLimitResponses
                .WriteAsync(context.HttpContext, retryAfter, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            context.Result = new EmptyResult();
            return;
        }

        await next().ConfigureAwait(false);
    }
}

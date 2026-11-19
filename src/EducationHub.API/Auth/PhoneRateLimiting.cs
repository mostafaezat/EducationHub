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
}

/// <summary>Writes the 429 every limiter in the API answers with.</summary>
internal static class RateLimitResponses
{
    public const string RetryAfterSeconds = "60";

    public static async Task WriteAsync(HttpContext httpContext, string retryAfter, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.Headers.RetryAfter = retryAfter;

        // The ASP.NET limiter runs before UseAuthentication, so no request culture has been
        // applied yet and there is no token to read a language preference from. The catalog is
        // read directly with Accept-Language (falling back to Arabic) instead.
        var store = httpContext.RequestServices.GetRequiredService<JsonResourceStore>();

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,

                // Kept as a fixed string: the 429 is identified by status and Retry-After,
                // and this title is not part of what carries the translation.
                Title = "Too many attempts",
                Detail = BoundaryMessages.Resolve(
                    store,
                    httpContext,
                    LocalizationKeys.RateLimited,
                    "Too many credential attempts from this address. Try again shortly.")
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The per-mobile-number budget of the phone password-reset endpoints. The number is in the
/// request body, which the ASP.NET rate limiter (it partitions on the raw request, before model
/// binding) cannot see; this limiter runs as an action filter on the bound argument instead.
/// In-memory and per instance, like the IP limiter.
/// </summary>
public sealed class PhoneResetRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly TimeSpan _window;

    public PhoneResetRateLimiter(IOptions<AuthRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var limits = options.Value.PhoneReset;
        _window = limits.Window;

        _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                Window = limits.Window,
                PermitLimit = Math.Max(1, limits.PerPhonePermitLimit),
                QueueLimit = 0
            }));
    }

    /// <summary>The Retry-After value, in whole seconds.</summary>
    public string RetryAfter => ((int)Math.Ceiling(_window.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Takes one permit for the endpoint + number; false when the budget is spent.</summary>
    public bool TryAcquire(string endpoint, string canonicalPhone)
    {
        using var lease = _limiter.AttemptAcquire($"{endpoint}:{canonicalPhone}");
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
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

            if (!limiter.TryAcquire(endpoint, canonical))
            {
                await RateLimitResponses
                    .WriteAsync(context.HttpContext, limiter.RetryAfter, context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                context.Result = new EmptyResult();
                return;
            }
        }

        await next().ConfigureAwait(false);
    }
}

using EducationHub.Shared.Common.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// Resolves a boundary-level message from the JSON catalog using the request's language,
/// falling back to the string already present in the code when the catalog has no entry.
/// </summary>
/// <remarks>
/// Boundary writers (the exception middleware, the 429 path and the problem writers) read the
/// catalog directly instead of going through <see cref="Microsoft.Extensions.Localization.IStringLocalizer{TResource}"/>.
/// for two reasons. The exception middleware sits <i>outside</i> <c>UseRequestLocalization</c>,
/// so by the time a caught exception reaches it that middleware's <c>finally</c> has already
/// unwound <c>CultureInfo.CurrentUICulture</c>; and the rate limiter runs before authentication,
/// so no claim is available to <i>set</i> a culture yet. Resolving the language straight from
/// the request keeps both paths correct without moving them in the pipeline or duplicating the
/// precedence rule - that stays in <see cref="RequestLanguageResolver"/>.
/// </remarks>
internal static class BoundaryMessages
{
    /// <param name="store">The catalog loader.</param>
    /// <param name="context">The request, used to pick the language.</param>
    /// <param name="key">The lookup key - a <see cref="LocalizationKeys"/> value or a stable error code.</param>
    /// <param name="fallback">The message already in the code, used when the catalog lacks the key.</param>
    public static string Resolve(JsonResourceStore store, HttpContext context, string? key, string fallback)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var value = store.Find(RequestLanguageResolver.Resolve(context), key);

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
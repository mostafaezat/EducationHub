using EducationHub.Shared.Common.Constants;
using EducationHub.Shared.Common.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// Picks the request language: an explicit supported <c>Accept-Language</c> value, then the
/// token's stored preference, then Arabic.
/// </summary>
/// <remarks>
/// The header is inspected before falling back rather than being normalized directly, because
/// <see cref="SupportedLanguages.Normalize"/> maps anything unrecognized to the default.
/// Normalizing first would make a browser asking for <c>fr</c> pin every response to Arabic and
/// make the user's own stored preference unreachable.
/// </remarks>
internal static class RequestLanguageResolver
{
    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (TryGetSupportedAcceptLanguage(context, out var fromHeader))
        {
            return fromHeader;
        }

        var fromClaim = context.User.FindFirst(EducationHubClaimTypes.PreferredLanguage)?.Value;

        return SupportedLanguages.IsSupported(fromClaim)
            ? SupportedLanguages.Normalize(fromClaim)
            : SupportedLanguages.Default;
    }

    private static bool TryGetSupportedAcceptLanguage(HttpContext context, out string language)
    {
        language = SupportedLanguages.Default;

        var header = context.Request.Headers.AcceptLanguage.ToString();

        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        // Accept-Language can carry several weighted tags; the first supported one wins.
        foreach (var part in header.Split(','))
        {
            var tag = part.Split(';')[0].Trim();

            if (!SupportedLanguages.IsSupported(tag))
            {
                continue;
            }

            language = SupportedLanguages.Normalize(tag);

            return true;
        }

        return false;
    }
}

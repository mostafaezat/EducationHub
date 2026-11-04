using EducationHub.Shared.Common.Localization;
using Microsoft.AspNetCore.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// Sets the request culture from the request language already understood by the platform
/// (see <see cref="RequestLanguageResolver"/>).
/// </summary>
/// <remarks>
/// This is the single provider in the pipeline. The framework's Accept-Language provider is
/// deliberately not also registered: our resolver already applies the exact precedence the
/// product wants (header, then the user's stored preference, then Arabic), and running both
/// would let the framework re-map an unsupported tag onto the default and shadow the stored
/// preference.
/// </remarks>
public sealed class RequestLanguageCultureProvider : IRequestCultureProvider
{
    public Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var cultureName = SupportedLanguages.GetCultureName(RequestLanguageResolver.Resolve(httpContext));

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(cultureName, cultureName));
    }
}

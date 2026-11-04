using EducationHub.Shared.Common.Localization;
using Microsoft.Extensions.Localization;

namespace EducationHub.API.Localization;

/// <summary>Wires the JSON-backed localization services and request-culture options.</summary>
internal static class LocalizationSetup
{
    /// <summary>
    /// Registers localization, then replaces the default factory with the JSON one. The
    /// replacement is registered after <c>AddLocalization</c> so it wins when
    /// <c>IStringLocalizerFactory</c> is resolved, which in turn is what
    /// <c>IStringLocalizer&lt;T&gt;</c> is built from.
    /// </summary>
    public static IServiceCollection AddApiLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddSingleton<JsonResourceStore>();
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();

        return services;
    }

    public static RequestLocalizationOptions BuildRequestLocalizationOptions()
    {
        var cultures = SupportedLanguages.All
            .Select(SupportedLanguages.GetCultureName)
            .ToArray();

        var options = new RequestLocalizationOptions()
            .SetDefaultCulture(SupportedLanguages.GetCultureName(SupportedLanguages.Default))
            .AddSupportedCultures(cultures)
            .AddSupportedUICultures(cultures);

        options.RequestCultureProviders = [new RequestLanguageCultureProvider()];

        return options;
    }
}

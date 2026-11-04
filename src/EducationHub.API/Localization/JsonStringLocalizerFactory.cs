using Microsoft.Extensions.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// Supplies <see cref="JsonStringLocalizer"/> instances as the application's localizers.
/// </summary>
/// <remarks>
/// The resource type and location are ignored on purpose: every localizer reads the same two
/// catalogs. Registering this factory over the default one is what routes
/// <c>IStringLocalizer&lt;LocalizationResource&gt;</c> - used across the Application layer -
/// through the JSON files instead of a <c>.resx</c> scheme.
/// </remarks>
public sealed class JsonStringLocalizerFactory(JsonResourceStore store) : IStringLocalizerFactory
{
    private readonly JsonStringLocalizer _localizer = new(store);

    public IStringLocalizer Create(Type resourceSource) => _localizer;

    public IStringLocalizer Create(string baseName, string location) => _localizer;
}

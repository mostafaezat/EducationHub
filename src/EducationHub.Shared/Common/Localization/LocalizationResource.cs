namespace EducationHub.Shared.Common.Localization;

/// <summary>
/// Marker type for <c>IStringLocalizer&lt;LocalizationResource&gt;</c>.
/// </summary>
/// <remarks>
/// There is nothing to see here on purpose. The API supplies the actual strings through its
/// own JSON-backed <c>IStringLocalizerFactory</c>, which ignores the resource type; this type
/// exists only so Application and API code can agree on a single generic argument and live in
/// a project both already reference.
/// </remarks>
public sealed class LocalizationResource;

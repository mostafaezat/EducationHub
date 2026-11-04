using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using EducationHub.Shared.Common.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// Loads the JSON message catalogs embedded in the API assembly and resolves a key per culture.
/// </summary>
/// <remarks>
/// Catalogs live at <c>Localization/Resources/{ar,en}.json</c> and use flat dotted keys, so a
/// resource key is exactly the string the code looks up (<c>Auth.InvalidCredentials</c>). The
/// files are embedded resources, which keeps resolution identical under the test host, a local
/// run and a published deployment - there is no content-root or working-directory dependency.
/// </remarks>
public sealed class JsonResourceStore
{
    private const string Extension = ".json";

    private readonly Assembly _assembly = typeof(JsonResourceStore).Assembly;

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _byCulture =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All entries known for a culture, normalized to a supported code.</summary>
    public IReadOnlyDictionary<string, string> For(string? culture)
        => _byCulture.GetOrAdd(Normalize(culture), Load);

    /// <summary>
    /// Resolves a key for the culture, falling back to the default culture (Arabic) when the
    /// active one does not carry it.
    /// </summary>
    /// <returns>The message, or <see langword="null"/> when neither culture defines the key.</returns>
    public string? Find(string? culture, string key)
    {
        var normalized = Normalize(culture);

        if (For(normalized).TryGetValue(key, out var value))
        {
            return value;
        }

        if (!string.Equals(normalized, SupportedLanguages.Default, StringComparison.OrdinalIgnoreCase)
            && For(SupportedLanguages.Default).TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return null;
    }

    private static string Normalize(string? culture) => SupportedLanguages.Normalize(culture);

    private IReadOnlyDictionary<string, string> Load(string culture)
    {
        var resourceName = _assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith($".{culture}{Extension}", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = _assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? new Dictionary<string, string>();

        return new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }
}

using System.Globalization;
using Microsoft.Extensions.Localization;

namespace EducationHub.API.Localization;

/// <summary>
/// An <see cref="IStringLocalizer"/> backed by the JSON catalogs, resolving the culture from
/// <see cref="CultureInfo.CurrentUICulture"/> at lookup time.
/// </summary>
/// <remarks>
/// Resolving the culture per lookup (rather than capturing it at construction) is what lets a
/// single instance - or a singleton validator that holds it - serve concurrent requests in
/// different languages, because <c>UseRequestLocalization</c> sets the current UI culture per
/// request on the request's execution context.
/// </remarks>
public sealed class JsonStringLocalizer(JsonResourceStore store) : IStringLocalizer
{
    private readonly JsonResourceStore _store = store;

    public LocalizedString this[string name]
    {
        get
        {
            var value = _store.Find(CultureInfo.CurrentUICulture.Name, name);

            return new LocalizedString(name, value ?? name, resourceNotFound: value is null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var value = _store.Find(CultureInfo.CurrentUICulture.Name, name);

            return new LocalizedString(
                name,
                string.Format(CultureInfo.CurrentCulture, value ?? name, arguments),
                resourceNotFound: value is null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        => _store.For(CultureInfo.CurrentUICulture.Name)
            .Select(static entry => new LocalizedString(entry.Key, entry.Value, resourceNotFound: false));
}

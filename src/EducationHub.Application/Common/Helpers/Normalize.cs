namespace EducationHub.Application.Common.Helpers;

/// <summary>
/// Small string normalizers shared by handlers.
/// </summary>
/// <remarks>
/// Centralised because the handlers need "trim, and treat blank as null" applied
/// <b>consistently</b>. Applied field-by-field by hand, the same field ends up storing
/// <c>""</c> in one place and <c>null</c> in another, which then makes uniqueness and
/// equality checks behave differently for no visible reason.
/// </remarks>
public static class Normalize
{
    /// <summary>Trims; converts empty and whitespace-only input to <c>null</c>.</summary>
    public static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trims, falling back to <paramref name="fallback"/> when blank.</summary>
    public static string Required(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>Lower-cased and trimmed. Used for emails and normalized identity keys.</summary>
    public static string? Email(string? value) => Optional(value)?.ToLowerInvariant();

    /// <summary>
    /// Builds a URL-safe slug from arbitrary text by keeping only letters, digits, hyphens
    /// and underscores.
    /// </summary>
    public static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string([.. value.Trim().ToLowerInvariant()
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')]);
    }
}

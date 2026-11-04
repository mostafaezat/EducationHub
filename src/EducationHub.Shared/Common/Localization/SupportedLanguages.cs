namespace EducationHub.Shared.Common.Localization;

/// <summary>
/// ISO 639-1 codes the product ships. Arabic is the default and the RTL layout language
/// (spec section 26).
/// </summary>
public static class SupportedLanguages
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static readonly IReadOnlyList<string> All = [Arabic, English];

    /// <summary>The fallback used when a message key is missing from the active culture.</summary>
    public const string Default = Arabic;

    public static bool IsSupported(string? code)
        => code is not null
           && All.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps anything the client may send (<c>ar-EG</c>, <c>en-US</c>, <c>EN_us</c>) onto a
    /// supported code, returning <see langword="Default"/> for unknown input.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Default;
        }

        var primary = code.Trim().Split('-', '_')[0];
        return IsSupported(primary) ? primary.ToLowerInvariant() : Default;
    }

    public static string GetCultureName(string code)
        => code switch
        {
            Arabic => "ar-EG",
            English => "en-US",
            _ => "ar-EG"
        };

    public static string GetDirection(string code) => code == Arabic ? "rtl" : "ltr";

    /// <summary>NG-ZORRO locale ids, which are not the same as .NET culture names.</summary>
    public static string GetNgZorroLocale(string code) => code == Arabic ? "ar_EG" : "en_US";
}

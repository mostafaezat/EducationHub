namespace EducationHub.Application.Common.Helpers;

/// <summary>
/// Mobile-number handling for sign-in and WhatsApp. Numbers are typed in many shapes
/// ("010 1234 5678", "+20 10 1234 5678", "00201012345678"), so they are compared through a
/// canonical international form (<c>+201012345678</c>). Local numbers without a country code
/// are read as Egyptian (+20), the centres' home market.
/// </summary>
public static class PhoneNumberFormat
{
    public const string DefaultCountryCode = "20";

    /// <summary>
    /// Domain of the internal e-mail given to an account that signs in with a mobile number only
    /// (Identity requires a unique e-mail). <c>.invalid</c> is reserved and can never receive mail.
    /// </summary>
    public const string PlaceholderEmailDomain = "phone.educationhub.invalid";

    /// <summary>True when the text is a mobile number rather than an e-mail address.</summary>
    public static bool LooksLikePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed.All(static c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')')
            && trimmed.Count(char.IsAsciiDigit) >= 8;
    }

    /// <summary>The canonical "+&lt;country&gt;&lt;number&gt;" form, or null when it is not a phone number.</summary>
    public static string? ToCanonical(string? value)
    {
        if (!LooksLikePhone(value))
        {
            return null;
        }

        var trimmed = value!.Trim();
        var digits = new string([.. trimmed.Where(char.IsAsciiDigit)]);

        if (trimmed.StartsWith('+'))
        {
            return "+" + digits;
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return "+" + digits[2..];
        }

        // Egyptian mobile shapes: 01XXXXXXXXX (11 digits), 1XXXXXXXXX (10), 201XXXXXXXXX (12).
        if (digits.Length == 11 && digits.StartsWith('0'))
        {
            return "+" + DefaultCountryCode + digits[1..];
        }

        if (digits.Length == 10 && digits.StartsWith('1'))
        {
            return "+" + DefaultCountryCode + digits;
        }

        if (digits.Length == 12 && digits.StartsWith(DefaultCountryCode, StringComparison.Ordinal))
        {
            return "+" + digits;
        }

        return "+" + digits;
    }

    /// <summary>
    /// The stored shapes a number may have been saved in before canonical storage existed, so a
    /// sign-in by phone also finds older rows ("01012345678", "201012345678", "+201012345678").
    /// </summary>
    public static IReadOnlyList<string> LookupVariants(string? value)
    {
        var canonical = ToCanonical(value);

        if (canonical is null)
        {
            return [];
        }

        var variants = new HashSet<string>(StringComparer.Ordinal)
        {
            canonical,
            canonical[1..],
            "00" + canonical[1..],
            value!.Trim()
        };

        if (canonical.StartsWith("+" + DefaultCountryCode, StringComparison.Ordinal))
        {
            var national = canonical[(1 + DefaultCountryCode.Length)..];
            variants.Add("0" + national);
            variants.Add(national);
        }

        return [.. variants];
    }

    /// <summary>The internal e-mail of a phone-only account, e.g. <c>201012345678@phone.educationhub.invalid</c>.</summary>
    public static string PlaceholderEmail(string canonicalPhone)
        => $"{canonicalPhone.TrimStart('+')}@{PlaceholderEmailDomain}";

    /// <summary>True for the internal e-mail of a phone-only account.</summary>
    public static bool IsPlaceholderEmail(string? email)
        => email is not null && email.EndsWith("@" + PlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);
}

using System.Globalization;
using EducationHub.API.Localization;
using EducationHub.Application.Features.Auth.DTOs;
using EducationHub.Application.Features.Auth.Validators;
using EducationHub.Shared.Common.Localization;
using Microsoft.Extensions.Localization;

namespace EducationHub.Tests.Unit.Localization;

/// <summary>
/// Covers the localization resources: every key advertised by <see cref="LocalizationKeys"/>
/// must exist in both catalogs, and both the localizer and a real validator must return the
/// message for the active culture rather than the hardcoded English it replaced.
/// </summary>
public sealed class LocalizationResourceTests
{
    [Fact]
    public void Every_localization_key_exists_in_both_catalogs()
    {
        var store = new JsonResourceStore();

        var arabic = store.For(SupportedLanguages.Arabic);
        var english = store.For(SupportedLanguages.English);

        var keys = LocalizationKeyNames();

        Assert.NotEmpty(keys);

        var missingArabic = keys.Where(key => !arabic.ContainsKey(key)).ToList();
        var missingEnglish = keys.Where(key => !english.ContainsKey(key)).ToList();

        Assert.True(
            missingArabic.Count == 0,
            "ar.json is missing: " + string.Join(", ", missingArabic));

        Assert.True(
            missingEnglish.Count == 0,
            "en.json is missing: " + string.Join(", ", missingEnglish));
    }

    [Fact]
    public void Both_catalogs_carry_a_value_for_every_key()
    {
        var store = new JsonResourceStore();

        foreach (var culture in SupportedLanguages.All)
        {
            var entries = store.For(culture);

            var empty = entries.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).ToList();

            Assert.True(empty.Count == 0, $"{culture} has empty values for: {string.Join(", ", empty.Select(e => e.Key))}");
        }
    }

    [Theory]
    [InlineData("ar-EG", "البريد الإلكتروني مطلوب.")]
    [InlineData("en-US", "Email is required.")]
    public void Localizer_returns_the_message_for_the_current_ui_culture(string culture, string expected)
    {
        WithUICulture(culture, () =>
        {
            var localizer = new JsonStringLocalizer(new JsonResourceStore());

            Assert.Equal(expected, localizer[LocalizationKeys.EmailRequired].Value);
        });
    }

    [Fact]
    public void Unknown_key_falls_back_to_the_key_itself_and_is_flagged_not_found()
    {
        var localizer = new JsonStringLocalizer(new JsonResourceStore());

        var result = localizer["Does.Not.Exist"];

        Assert.True(result.ResourceNotFound);
        Assert.Equal("Does.Not.Exist", result.Value);
    }

    [Fact]
    public void Unsupported_culture_falls_back_to_the_arabic_default()
    {
        var store = new JsonResourceStore();

        Assert.Equal("البريد الإلكتروني مطلوب.", store.Find("fr-FR", LocalizationKeys.EmailRequired));
    }

    [Fact]
    public void A_validator_constructed_with_a_localizer_reports_arabic_messages()
    {
        WithUICulture("ar-EG", () =>
        {
            var validator = new LoginRequestValidator(AsGeneric(new JsonStringLocalizer(new JsonResourceStore())));

            var result = validator.Validate(new LoginRequest(string.Empty, string.Empty));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == "البريد الإلكتروني مطلوب.");
            Assert.Contains(result.Errors, e => e.ErrorMessage == "كلمة المرور مطلوبة.");
        });
    }

    [Fact]
    public void A_validator_constructed_with_a_localizer_reports_english_messages()
    {
        WithUICulture("en-US", () =>
        {
            var validator = new LoginRequestValidator(AsGeneric(new JsonStringLocalizer(new JsonResourceStore())));

            var result = validator.Validate(new LoginRequest(string.Empty, string.Empty));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is required.");
            Assert.Contains(result.Errors, e => e.ErrorMessage == "Password is required.");
        });
    }

    [Fact]
    public void A_validator_constructed_without_a_localizer_keeps_the_english_fallback()
    {
        // Guards the direct-construction path unit tests rely on: no container, so the
        // validator must still produce the literal the message came from rather than a key.
        var validator = new LoginRequestValidator();

        var result = validator.Validate(new LoginRequest(string.Empty, string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is required.");
    }

    private static List<string> LocalizationKeyNames()
        => typeof(LocalizationKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(static field => field.GetValue(null) as string)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();

    private static void WithUICulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// The JSON localizer implements the non-generic interface; the generic one is what
    /// validators ask for. They differ only in type, so an adapter is all that is needed.
    /// </summary>
    private static IStringLocalizer<LocalizationResource> AsGeneric(IStringLocalizer inner)
        => new GenericAdapter(inner);

    private sealed class GenericAdapter(IStringLocalizer inner) : IStringLocalizer<LocalizationResource>
    {
        public LocalizedString this[string name] => inner[name];

        public LocalizedString this[string name, params object[] arguments] => inner[name, arguments];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => inner.GetAllStrings(includeParentCultures);
    }
}
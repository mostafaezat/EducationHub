using EducationHub.Application.Common.Helpers;

namespace EducationHub.Tests.Unit.Common;

/// <summary>Mobile numbers typed in different shapes must compare as the same number.</summary>
public sealed class PhoneNumberFormatTests
{
    [Theory]
    [InlineData("01012345678")]
    [InlineData("010 1234 5678")]
    [InlineData("+20 10 1234 5678")]
    [InlineData("00201012345678")]
    [InlineData("201012345678")]
    [InlineData("1012345678")]
    public void Egyptian_shapes_share_one_canonical_form(string input)
        => Assert.Equal("+201012345678", PhoneNumberFormat.ToCanonical(input));

    [Theory]
    [InlineData("parent@example.com")]
    [InlineData("12345")]
    [InlineData("abc12345678")]
    [InlineData("")]
    public void Non_phone_input_is_not_a_phone(string input)
    {
        Assert.False(PhoneNumberFormat.LooksLikePhone(input));
        Assert.Null(PhoneNumberFormat.ToCanonical(input));
    }

    [Fact]
    public void Lookup_variants_cover_the_older_stored_shapes()
    {
        var variants = PhoneNumberFormat.LookupVariants("+201012345678");

        Assert.Contains("+201012345678", variants);
        Assert.Contains("201012345678", variants);
        Assert.Contains("01012345678", variants);
        Assert.Contains("00201012345678", variants);
    }

    [Fact]
    public void Placeholder_email_is_recognised_and_never_deliverable()
    {
        var email = PhoneNumberFormat.PlaceholderEmail("+201012345678");

        Assert.Equal("201012345678@phone.educationhub.invalid", email);
        Assert.True(PhoneNumberFormat.IsPlaceholderEmail(email));
        Assert.False(PhoneNumberFormat.IsPlaceholderEmail("parent@example.com"));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Endpoint-level coverage for request localization: the same malformed request must be
/// reported in Arabic by default and in English when the caller asks for it, while the
/// machine-readable <c>title</c>/<c>code</c> pair stays byte-identical either way.
/// </summary>
/// <remarks>
/// The two assertions below exercise different code paths on purpose. <c>detail</c> is written
/// by <c>ExceptionHandlingMiddleware</c>, which sits outside the localization middleware, and
/// the field <c>message</c> values are produced by the validator inside it - so a pass here
/// proves both the boundary lookup and the localizer-driven validation agree on the language.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class LocalizationEndpointTests(ApiTestFixture fixture)
{
    private const string ArabicValidationDetail = "حدثت أخطاء في التحقق من البيانات.";
    private const string EnglishValidationDetail = "One or more validation errors occurred.";

    private HttpClient Client => fixture.Client;

    [Theory]
    [InlineData(null, ArabicValidationDetail)]
    [InlineData("ar", ArabicValidationDetail)]
    [InlineData("en", EnglishValidationDetail)]
    public async Task Validation_problem_detail_is_returned_in_the_requested_language(
        string? acceptLanguage,
        string expectedDetail)
    {
        using var response = await PostLoginAsync(new { email = "", password = "" }, acceptLanguage);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The code a client branches on must not move when only the language does.
        Assert.Equal("Errors.Validation", body.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Field_messages_are_returned_in_arabic_by_default()
    {
        using var response = await PostLoginAsync(new { email = "", password = "" }, acceptLanguage: null);

        var messages = await ReadFieldMessagesAsync(response);

        Assert.Contains("البريد الإلكتروني مطلوب.", messages);
        Assert.Contains("كلمة المرور مطلوبة.", messages);
    }

    [Fact]
    public async Task Field_messages_are_returned_in_english_when_requested()
    {
        using var response = await PostLoginAsync(
            new { email = "", password = "" },
            acceptLanguage: "en");

        var messages = await ReadFieldMessagesAsync(response);

        Assert.Contains("Email is required.", messages);
        Assert.Contains("Password is required.", messages);
    }

    private async Task<HttpResponseMessage> PostLoginAsync(object body, string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(body)
        };

        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await Client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<List<string>> ReadFieldMessagesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

        return body.GetProperty("errors")
            .EnumerateArray()
            .Select(static error => error.GetProperty("message").GetString())
            .Where(static message => message is not null)
            .Cast<string>()
            .ToList();
    }
}
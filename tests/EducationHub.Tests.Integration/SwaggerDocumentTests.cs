using System.Net;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Regression coverage for audit defect P0-2: the Auth and Users features both defined a DTO
/// named <c>ResetPasswordRequest</c>, so Swashbuckle emitted a duplicate schema id and the
/// whole OpenAPI document failed to generate (HTTP 500).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SwaggerDocumentTests(ApiTestFixture fixture)
{
    [Fact]
    public async Task Swagger_document_is_served_with_200()
    {
        // The test host runs in Development, where Program.cs enables Swagger.
        using var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.ReadJsonAsync();

        Assert.True(document.TryGetProperty("openapi", out _));
        Assert.True(document.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
    }
}

using System.Net;

namespace EducationHub.Tests.Integration;

/// <summary>
/// G3: creating a user under the platform scope used to crash with HTTP 409
/// ("Sequence contains more than one element"). Identity's stock UserStore resolves a role
/// by normalised name with a bare SingleOrDefault; role names are unique per tenant in this
/// schema, and the platform scope sees every tenant's rows - so "TenantAdmin" matched one
/// row in every tenant and every AddToRole call threw. The store replacement scopes the
/// lookup to the new user's tenant, and role-name resolution refuses tenant roles for
/// platform-scope users (who carry Guid.Empty).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformUserCreationTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Platform_admin_creates_a_user_with_the_platform_role()
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var email = ApiScenarios.NewEmail("platform-create-ok");

        using var response = await Client.PostJsonAsync(
            "/api/users",
            CreateUserBody(email, roles: ["PlatformAdmin"]),
            platform);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");
        var roles = data.GetProperty("roles")
            .EnumerateArray()
            .Select(static r => r.GetString())
            .ToArray();

        Assert.Contains("PlatformAdmin", roles);

        // No confirmation email was requested, so the account is confirmed on creation.
        Assert.True(data.GetProperty("emailConfirmed").GetBoolean());

        // Clean up: the fixture's database persists across runs.
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{data.GetProperty("id").GetGuid()}");
        delete.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", platform);
        using var deleted = await Client.SendAsync(delete);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task Platform_scope_rejects_a_tenant_role_instead_of_crashing()
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var email = ApiScenarios.NewEmail("platform-create-tenant-role");

        using var response = await Client.PostJsonAsync(
            "/api/users",
            CreateUserBody(email, roles: ["TenantAdmin"]),
            platform);

        // Before the fix this request reached AddToRolesAsync and died with HTTP 409
        // "Sequence contains more than one element"; resolution now refuses the role up
        // front with the endpoint's stable code.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Users.UnknownRole", await response.ReadProblemTitleAsync());
    }

    private static object CreateUserBody(string email, string[] roles) => new
    {
        fullName = "Platform Scope Probe",
        email,
        password = "Conf1rm#Pass1",
        phoneNumber = (string?)null,
        branchId = (Guid?)null,
        preferredLanguage = "ar",
        mustChangePassword = false,
        roles,
        sendEmailConfirmation = false
    };
}

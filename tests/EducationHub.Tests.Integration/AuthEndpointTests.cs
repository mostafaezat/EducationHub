using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Endpoint-level coverage for the Phase 0 authentication fixes: refresh rotation and reuse
/// detection, logout, lockout, rate limiting, password change, the email flows and the
/// must-change-password gate.
/// </summary>
/// <remarks>
/// These are the durable successors to the manual E2E smoke checks: same assertions, but run
/// by the test suite against a real host and database.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class AuthEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_revokes_the_family()
    {
        var (tenant, _) = await NewTenantAsync("rotate");
        var session = await ApiScenarios.LoginAsync(Client, tenant.Email, tenant.Password);

        using var rotation = await Client.PostJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);

        var rotated = (await rotation.ReadJsonAsync()).GetProperty("data").GetProperty("refreshToken").GetString();

        Assert.NotEqual(session.RefreshToken, rotated);

        // Replaying the already-rotated token is treated as theft and kills the whole chain.
        using var replay = await Client.PostJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // The legitimate descendant is collateral damage, by design.
        using var descendant = await Client.PostJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = rotated });

        Assert.Equal(HttpStatusCode.BadRequest, descendant.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_presented_refresh_token()
    {
        var (tenant, _) = await NewTenantAsync("logout");
        var session = await ApiScenarios.LoginAsync(Client, tenant.Email, tenant.Password);

        using var logout = await Client.PostJsonAsync(
            "/api/auth/logout",
            new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        using var afterLogout = await Client.PostJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.BadRequest, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Five_failed_logins_lock_the_account_even_against_the_right_password()
    {
        var (tenant, token) = await NewTenantAsync("lockout");
        var email = ApiScenarios.NewEmail("lock-user");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Lock User",
            email,
            "Corr3ct#Pass1",
            roles: ["TenantAdmin"]);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await Client.PostJsonAsync(
                "/api/auth/login",
                new { email, password = "Wr0ng#Pass1" });

            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var locked = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email, password = "Corr3ct#Pass1" });

        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("Auth.AccountLocked", await locked.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Rate_limiter_returns_429_with_retry_after()
    {
        var (factory, client) = fixture.CreateRateLimitedHost(permitLimit: 3);

        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var permitted = await client.PostJsonAsync(
                    "/api/auth/login",
                    new { email = ApiScenarios.NewEmail("nobody"), password = "Wh4tever#Pass" });

                Assert.Equal(HttpStatusCode.Unauthorized, permitted.StatusCode);
            }

            using var rejected = await client.PostJsonAsync(
                "/api/auth/login",
                new { email = ApiScenarios.NewEmail("nobody"), password = "Wh4tever#Pass" });

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal("60", rejected.Headers.GetValues("Retry-After").Single());
        }
        finally
        {
            client.Dispose();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task Change_password_revokes_sessions_and_replaces_the_credential()
    {
        var (tenant, token) = await NewTenantAsync("change");
        var email = ApiScenarios.NewEmail("change-user");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Change User",
            email,
            "Start#Pass1",
            roles: ["TenantAdmin"]);

        var session = await ApiScenarios.LoginAsync(Client, email, "Start#Pass1");

        using var change = await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Start#Pass1", newPassword = "Finish#Pass1", confirmPassword = "Finish#Pass1" },
            session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        using var oldRefresh = await Client.PostJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.BadRequest, oldRefresh.StatusCode);

        using var oldLogin = await Client.PostJsonAsync("/api/auth/login", new { email, password = "Start#Pass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        using var newLogin = await Client.PostJsonAsync("/api/auth/login", new { email, password = "Finish#Pass1" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // The pre-change session was killed; only the one created by the final login is live.
        Assert.Equal(1, await ApiScenarios.CountLiveRefreshTokensAsync(fixture, email));
    }

    [Fact]
    public async Task Unconfirmed_account_is_refused_then_admitted_after_confirmation()
    {
        var (tenant, token) = await NewTenantAsync("confirm");
        var email = ApiScenarios.NewEmail("unconfirmed");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Unconfirmed User",
            email,
            "Conf1rm#Pass1",
            roles: ["TenantAdmin"],
            sendEmailConfirmation: true);

        using var blocked = await Client.PostJsonAsync("/api/auth/login", new { email, password = "Conf1rm#Pass1" });

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("Account.EmailNotConfirmed", await blocked.ReadProblemTitleAsync());

        using var resend = await Client.PostJsonAsync("/api/auth/resend-confirmation", new { email });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);

        var confirmationToken = await fixture.GenerateEmailConfirmationTokenAsync(email);

        using var confirm = await Client.PostJsonAsync(
            "/api/auth/confirm-email",
            new { email, token = confirmationToken });

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var admitted = await Client.PostJsonAsync("/api/auth/login", new { email, password = "Conf1rm#Pass1" });
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
    }

    [Fact]
    public async Task Bogus_confirmation_token_is_rejected_with_a_stable_code()
    {
        var (tenant, token) = await NewTenantAsync("bad-confirm");
        var email = ApiScenarios.NewEmail("bad-confirm");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Bad Confirm",
            email,
            "Conf1rm#Pass1",
            roles: ["TenantAdmin"],
            sendEmailConfirmation: true);

        using var response = await Client.PostJsonAsync(
            "/api/auth/confirm-email",
            new { email, token = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.InvalidEmailToken", await response.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Password_reset_flow_replaces_the_credential_and_hides_unknown_addresses()
    {
        var (tenant, token) = await NewTenantAsync("reset");
        var email = ApiScenarios.NewEmail("reset-user");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Reset User",
            email,
            "Before#Pass1",
            roles: ["TenantAdmin"]);

        using var known = await Client.PostJsonAsync("/api/auth/forgot-password", new { email });
        using var unknown = await Client.PostJsonAsync("/api/auth/forgot-password", new { email = ApiScenarios.NewEmail("ghost") });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);

        // One message for both, so the endpoint never reveals whether an address is registered.
        Assert.Equal(await ReadMessageAsync(known), await ReadMessageAsync(unknown));

        using var bogus = await Client.PostJsonAsync(
            "/api/auth/reset-password",
            new { email, resetToken = "bogus", newPassword = "After#Pass1", confirmPassword = "After#Pass1" });

        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
        Assert.Equal("Auth.InvalidResetToken", await bogus.ReadProblemTitleAsync());

        var resetToken = await fixture.GeneratePasswordResetTokenAsync(email);

        using var reset = await Client.PostJsonAsync(
            "/api/auth/reset-password",
            new { email, resetToken, newPassword = "After#Pass1", confirmPassword = "After#Pass1" });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var newLogin = await Client.PostJsonAsync("/api/auth/login", new { email, password = "After#Pass1" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        using var oldLogin = await Client.PostJsonAsync("/api/auth/login", new { email, password = "Before#Pass1" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [Fact]
    public async Task Must_change_password_blocks_everything_but_the_allowlist()
    {
        var (tenant, token) = await NewTenantAsync("forced");
        var email = ApiScenarios.NewEmail("forced-user");

        await ApiScenarios.CreateUserAsync(
            Client,
            token,
            "Forced User",
            email,
            "Forced#Pass1",
            roles: ["TenantAdmin"],
            mustChangePassword: true);

        var session = await ApiScenarios.LoginAsync(Client, email, "Forced#Pass1");

        Assert.True(session.Data.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        using var blocked = await Client.GetAsync("/api/users", session.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("Auth.MustChangePassword", await blocked.ReadProblemTitleAsync());

        using var me = await Client.GetAsync("/api/auth/me", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        using var permissions = await Client.GetAsync("/api/auth/permissions", session.AccessToken);
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);

        using var change = await Client.PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "Forced#Pass1", newPassword = "Final#Pass1", confirmPassword = "Final#Pass1" },
            session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // Changing the password revokes the pre-change session, so the caller signs in again -
        // and the new token no longer carries the obligation.
        var reLogin = await ApiScenarios.LoginAsync(Client, email, "Final#Pass1");

        Assert.False(reLogin.Data.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        using var allowed = await Client.GetAsync("/api/users", reLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Login_with_empty_credentials_is_rejected_with_400()
    {
        using var response = await Client.PostJsonAsync(
            "/api/auth/login",
            new { email = string.Empty, password = string.Empty });

        // Regression for audit defect P0-1 (validation never ran) and P0-3 (it surfaced as 500).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.ReadJsonAsync();

        Assert.Equal("Errors.Validation", body.GetProperty("title").GetString());

        var codes = body.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString())
            .ToArray();

        // Errors are reported against the DTO field the client sent, not the internal command.
        Assert.Contains("Email", codes);
        Assert.Contains("Password", codes);
        Assert.DoesNotContain("Request.Email", codes);
    }

    private async Task<(TenantAdmin Tenant, string Token)> NewTenantAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        return (tenant, token);
    }

    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response)
    {
        var body = await response.ReadJsonAsync();

        return body.TryGetProperty("message", out var message) ? message.GetString() : null;
    }
}
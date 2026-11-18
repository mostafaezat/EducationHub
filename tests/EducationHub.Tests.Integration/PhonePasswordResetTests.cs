using System.Net;
using EducationHub.Application.Common.Helpers;
using EducationHub.Application.Common.Interfaces;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Password reset by mobile number for phone-only parents: the code goes out by WhatsApp (the
/// test host records it), the answer never reveals whether a number is registered, and both
/// endpoints are limited per number.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PhonePasswordResetTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task A_phone_only_parent_resets_the_password_with_the_whatsapp_code()
    {
        var (phone, temporary) = await NewPhoneOnlyParentAsync("reset-ok");
        var before = await ApiScenarios.LoginAsync(Client, phone, temporary);

        using var forgot = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var code = LastCode(phone);
        Assert.Matches("^[0-9]{6}$", code);

        // Typed in another shape: the number is canonicalised exactly like sign-in.
        using var reset = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone = "+2" + phone,
            code,
            newPassword = "N3w#phonepass",
            confirmPassword = "N3w#phonepass"
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // The new password works and no longer has to be changed; the old one does not.
        var after = await ApiScenarios.LoginAsync(Client, phone, "N3w#phonepass");
        Assert.False(after.Data.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        using var oldPassword = await Client.PostJsonAsync("/api/auth/login", new { email = phone, password = temporary });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        // Sessions from before the reset are gone.
        using var refresh = await Client.PostJsonAsync("/api/auth/refresh", new { refreshToken = before.RefreshToken });
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);

        // The code works once: the reset rotated the security stamp it is bound to.
        using var replay = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone,
            code,
            newPassword = "An0ther#pass",
            confirmPassword = "An0ther#pass"
        });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("Auth.InvalidResetCode", await replay.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task A_wrong_code_is_rejected_and_the_password_is_unchanged()
    {
        var (phone, temporary) = await NewPhoneOnlyParentAsync("reset-bad");

        using var forgot = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var code = LastCode(phone);
        var wrong = code == "000000" ? "111111" : "000000";

        using var reset = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone,
            code = wrong,
            newPassword = "N3w#phonepass",
            confirmPassword = "N3w#phonepass"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal("Auth.InvalidResetCode", await reset.ReadProblemTitleAsync());

        await ApiScenarios.LoginAsync(Client, phone, temporary);

        // Malformed input is a validation error, not a code check.
        using var malformed = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone,
            code = "12ab",
            newPassword = "short",
            confirmPassword = "different"
        });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task An_unknown_number_gets_the_same_answer_and_nothing_is_sent()
    {
        var (known, _) = await NewPhoneOnlyParentAsync("reset-enum");
        var unknown = RandomMobile();

        using var knownResponse = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone = known });
        using var unknownResponse = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone = unknown });

        Assert.Equal(HttpStatusCode.OK, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);

        var knownBody = await knownResponse.ReadJsonAsync();
        var unknownBody = await unknownResponse.ReadJsonAsync();
        Assert.Equal(knownBody.GetProperty("message").GetString(), unknownBody.GetProperty("message").GetString());

        Assert.Empty(fixture.WhatsApp.To(PhoneNumberFormat.ToCanonical(unknown)!));

        // Resetting an unknown number answers like a wrong code.
        using var reset = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone = unknown,
            code = "123456",
            newPassword = "N3w#phonepass",
            confirmPassword = "N3w#phonepass"
        });
        Assert.Equal("Auth.InvalidResetCode", await reset.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Each_number_gets_three_requests_per_window()
    {
        // Unregistered on purpose: the limit applies whether or not the number exists.
        var phone = RandomMobile();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var permitted = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone });
            Assert.Equal(HttpStatusCode.OK, permitted.StatusCode);
        }

        // Same number in another shape is the same number.
        using var rejected = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone = "+2" + phone });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));

        // Another number is unaffected, and the reset endpoint keeps its own budget.
        using var other = await Client.PostJsonAsync("/api/auth/forgot-password/phone", new { phone = RandomMobile() });
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var guess = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
            {
                phone,
                code = "123456",
                newPassword = "N3w#phonepass",
                confirmPassword = "N3w#phonepass"
            });
            Assert.Equal(HttpStatusCode.BadRequest, guess.StatusCode);
        }

        using var blockedGuess = await Client.PostJsonAsync("/api/auth/reset-password/phone", new
        {
            phone,
            code = "123456",
            newPassword = "N3w#phonepass",
            confirmPassword = "N3w#phonepass"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedGuess.StatusCode);
    }

    private string LastCode(string phone)
    {
        var message = fixture.WhatsApp.To(PhoneNumberFormat.ToCanonical(phone)!)
            .Last(m => m.Kind == WhatsAppMessageKind.PasswordReset);

        return Assert.Single(message.Parameters);
    }

    private async Task<(string Phone, string TemporaryPassword)> NewPhoneOnlyParentAsync(string prefix)
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, prefix);
        var phone = RandomMobile();

        var student = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new
        {
            fullName = "Reset Child",
            guardian = new { fullName = "Reset Parent", phoneNumber = phone, relationship = "Father", createAccount = true }
        }, token);

        var temporary = student.GetProperty("newGuardian").GetProperty("temporaryPassword").GetString()!;
        return (phone, temporary);
    }

    /// <summary>A unique Egyptian-style mobile number (01XXXXXXXXX) per call.</summary>
    internal static string RandomMobile()
        => "011" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

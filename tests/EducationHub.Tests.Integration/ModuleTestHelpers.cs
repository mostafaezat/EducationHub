using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Shared helpers for the module endpoint tests added in Mission Phases 2.5b-6: provision a
/// tenant and sign in as its admin, and call an endpoint expecting 200 + a <c>data</c> payload.
/// </summary>
internal static class ModuleTestHelpers
{
    public static async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(HttpClient client, string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(client).ConfigureAwait(false);
        var tenant = await ApiScenarios.ProvisionTenantAsync(client, platform, prefix).ConfigureAwait(false);
        var token = await ApiScenarios.LoginTokenAsync(client, tenant.Email, tenant.Password).ConfigureAwait(false);

        return (tenant, token);
    }

    /// <summary>POST JSON, assert 200, return the <c>data</c> element.</summary>
    public static async Task<JsonElement> PostDataAsync(HttpClient client, string path, object body, string token)
    {
        using var response = await client.PostJsonAsync(path, body, token).ConfigureAwait(false);
        return await DataAsync(response, $"POST {path}").ConfigureAwait(false);
    }

    /// <summary>PUT JSON, assert 200, return the <c>data</c> element.</summary>
    public static async Task<JsonElement> PutDataAsync(HttpClient client, string path, object body, string token)
    {
        using var response = await client.PutJsonAsync(path, body, token).ConfigureAwait(false);
        return await DataAsync(response, $"PUT {path}").ConfigureAwait(false);
    }

    /// <summary>GET, assert 200, return the <c>data</c> element.</summary>
    public static async Task<JsonElement> GetDataAsync(HttpClient client, string path, string token)
    {
        using var response = await client.GetAsync(path, token).ConfigureAwait(false);
        return await DataAsync(response, $"GET {path}").ConfigureAwait(false);
    }

    public static Guid Id(this JsonElement element) => element.GetProperty("id").GetGuid();

    public static decimal Dec(this JsonElement element, string property) => element.GetProperty(property).GetDecimal();

    /// <summary>Uploads a small file through the multipart endpoint.</summary>
    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string token,
        string fileName,
        string contentType,
        byte[] bytes,
        IReadOnlyDictionary<string, string> fields)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);

        foreach (var (key, value) in fields)
        {
            form.Add(new StringContent(value), key);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response, string what)
    {
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{what} returned {(int)response.StatusCode}: {text}");

        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
    }
}

/// <summary>Builds the common arrangement for scheduling / finance / learning tests.</summary>
internal static class SchoolArrangement
{
    /// <summary>A subject, a group with a Sunday 16:00-17:00 slot, and <paramref name="students"/> enrolled students.</summary>
    public static async Task<(Guid GroupId, Guid SubjectId, List<Guid> StudentIds)> GroupWithStudentsAsync(
        HttpClient client,
        string token,
        int students,
        int? capacity = null,
        string name = "Math A")
    {
        var code = "S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var subject = await ModuleTestHelpers.PostDataAsync(client, "/api/subjects", new { code, name = "Subject " + code, sortOrder = 1 }, token).ConfigureAwait(false);

        var group = await ModuleTestHelpers.PostDataAsync(
            client, "/api/groups", new { name, subjectId = subject.Id(), capacity }, token).ConfigureAwait(false);

        await ModuleTestHelpers.PostDataAsync(
            client, $"/api/groups/{group.Id()}/slots",
            new { dayOfWeek = "Sunday", startTime = "16:00:00", endTime = "17:00:00", room = "R1" }, token).ConfigureAwait(false);

        var ids = new List<Guid>();

        for (var i = 0; i < students; i++)
        {
            var student = await ModuleTestHelpers.PostDataAsync(client, "/api/students", new { fullName = $"Student {i + 1} of {name}" }, token).ConfigureAwait(false);
            await ModuleTestHelpers.PostDataAsync(
                client, $"/api/groups/{group.Id()}/enrollments",
                new { studentProfileId = student.Id(), enrolledOn = "2026-09-01" }, token).ConfigureAwait(false);
            ids.Add(student.Id());
        }

        return (group.Id(), subject.Id(), ids);
    }
}

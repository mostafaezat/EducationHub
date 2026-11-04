using System.Net;
using System.Text;

namespace EducationHub.Tests.Integration;

/// <summary>Mission Phase 2.5b: file upload / list / download / delete (BL-005).</summary>
[Collection(ApiCollection.Name)]
public sealed class FilesEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Student_document_round_trips_and_is_tenant_isolated()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "files-a");
        var (_, otherToken) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "files-b");

        var student = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "File Owner" }, token);
        var bytes = Encoding.UTF8.GetBytes("report card");

        using var upload = await ModuleTestHelpers.UploadAsync(
            Client, token, "report.txt", "text/plain", bytes,
            new Dictionary<string, string>
            {
                ["Category"] = "StudentDocument",
                ["OwnerEntityName"] = "Student",
                ["OwnerEntityId"] = student.Id().ToString()
            });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var fileId = (await upload.ReadJsonAsync()).GetProperty("data").Id();

        var list = await ModuleTestHelpers.GetDataAsync(Client, $"/api/files?ownerEntityName=Student&ownerEntityId={student.Id()}", token);
        Assert.Contains(list.EnumerateArray(), f => f.Id() == fileId);

        using var download = await Client.GetAsync($"/api/files/{fileId}/content", token);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

        using var foreign = await Client.GetAsync($"/api/files/{fileId}/content", otherToken);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var delete = await Client.DeleteAsync($"/api/files/{fileId}", token);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var gone = await Client.GetAsync($"/api/files/{fileId}/content", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Disallowed_type_and_foreign_owner_are_rejected()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "files-bad");
        var (_, otherToken) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "files-bad-b");

        using var exe = await ModuleTestHelpers.UploadAsync(
            Client, token, "run.exe", "application/x-msdownload", [1, 2, 3],
            new Dictionary<string, string> { ["Category"] = "StudentDocument" });
        Assert.Equal(HttpStatusCode.BadRequest, exe.StatusCode);
        Assert.Equal("Files.TypeNotAllowed", await exe.ReadProblemTitleAsync());

        var foreignStudent = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Not Yours" }, otherToken);

        using var attach = await ModuleTestHelpers.UploadAsync(
            Client, token, "x.txt", "text/plain", [65],
            new Dictionary<string, string>
            {
                ["Category"] = "StudentDocument",
                ["OwnerEntityName"] = "Student",
                ["OwnerEntityId"] = foreignStudent.Id().ToString()
            });
        Assert.Equal(HttpStatusCode.NotFound, attach.StatusCode);
    }
}

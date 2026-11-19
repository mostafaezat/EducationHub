using System.Net;
using System.Text.Json;
using EducationHub.Shared.Common.Constants;

namespace EducationHub.Tests.Integration;

/// <summary>
/// QA-06: deletes reach the audit trail. Hard deletes used to write no row at all and soft
/// deletes were logged as plain updates.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditDeleteTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Deleting_a_custom_role_writes_a_Deleted_audit_row_with_its_key()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "audit-del-role");

        var role = await ModuleTestHelpers.PostDataAsync(
            Client,
            "/api/roles",
            new { name = "Front desk", description = "Audit test", permissions = new[] { PermissionConstants.StudentsView } },
            token);
        var roleId = role.Id();

        using (var deleted = await Client.DeleteAsync($"/api/roles/{roleId}", token))
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        var row = await SingleDeleteRowAsync(token, "ApplicationRole", roleId);

        // Hard delete: the last known state is the old value, there is no new value.
        // (The API omits null properties, so "no new value" is an absent or null newValue.)
        Assert.True(!row.TryGetProperty("newValue", out var newValue) || newValue.ValueKind == JsonValueKind.Null);
        var old = JsonDocument.Parse(row.GetProperty("oldValue").GetString()!).RootElement;
        Assert.Equal(roleId, old.GetProperty("Id").GetGuid());
        Assert.Equal("Front desk", old.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Soft_deleting_a_student_is_audited_as_Deleted_not_Updated()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "audit-del-soft");

        var student = await ModuleTestHelpers.PostDataAsync(
            Client,
            "/api/students",
            new { fullName = "Audit Soft Delete", enrollmentDate = "2026-11-18" },
            token);
        var studentId = student.Id();

        using (var deleted = await Client.DeleteAsync($"/api/students/{studentId}", token))
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        }

        var row = await SingleDeleteRowAsync(token, "StudentProfile", studentId);

        var newValue = JsonDocument.Parse(row.GetProperty("newValue").GetString()!).RootElement;
        Assert.Equal(studentId, newValue.GetProperty("Id").GetGuid());
        Assert.True(newValue.GetProperty("IsDeleted").GetBoolean());

        // The delete is not also reported as an update of the same row.
        var updates = await ModuleTestHelpers.GetDataAsync(
            Client,
            $"/api/audit/logs?action=Updated&entityName=StudentProfile&entityId={studentId}&pageSize=50",
            token);
        Assert.Empty(updates.GetProperty("items").EnumerateArray());
    }

    private async Task<JsonElement> SingleDeleteRowAsync(string token, string entityName, Guid entityId)
    {
        var logs = await ModuleTestHelpers.GetDataAsync(
            Client,
            $"/api/audit/logs?action=Deleted&entityName={entityName}&entityId={entityId}&pageSize=50",
            token);

        var row = Assert.Single(logs.GetProperty("items").EnumerateArray());
        Assert.Equal("Deleted", row.GetProperty("action").GetString());
        Assert.Equal(entityId.ToString(), row.GetProperty("entityId").GetString());

        return row;
    }
}

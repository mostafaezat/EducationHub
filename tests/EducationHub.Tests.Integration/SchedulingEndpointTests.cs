using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>Mission Phase 3: groups, enrollments, slots, session materialization and attendance.</summary>
[Collection(ApiCollection.Name)]
public sealed class SchedulingEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Sessions_are_materialized_once_and_attendance_is_marked_for_enrolled_students()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sched");
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 2);

        var group = await ModuleTestHelpers.GetDataAsync(Client, $"/api/groups/{groupId}", token);
        Assert.Equal(2, group.GetProperty("activeEnrollments").GetInt32());
        Assert.Single(group.GetProperty("slots").EnumerateArray());

        // 2026-11-01 .. 2026-11-14 contains two Sundays (Nov 1 and Nov 8).
        var range = new { from = "2026-11-01", to = "2026-11-14", studyGroupId = groupId };
        var first = await ModuleTestHelpers.PostDataAsync(Client, "/api/sessions/generate", range, token);
        Assert.Equal(2, first.GetProperty("created").GetInt32());

        var again = await ModuleTestHelpers.PostDataAsync(Client, "/api/sessions/generate", range, token);
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(2, again.GetProperty("skipped").GetInt32());

        var sessions = await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions?studyGroupId={groupId}&from=2026-11-01&to=2026-11-14", token);
        var sessionId = sessions.GetProperty("items")[0].Id();

        var roster = await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions/{sessionId}/attendance", token);
        Assert.Equal(2, roster.GetArrayLength());
        Assert.All(roster.EnumerateArray(), r => Assert.False(r.TryGetProperty("status", out var s) && s.ValueKind != JsonValueKind.Null));

        var marked = await ModuleTestHelpers.PutDataAsync(
            Client, $"/api/sessions/{sessionId}/attendance",
            new
            {
                records = new object[]
                {
                    new { studentProfileId = studentIds[0], status = "Present" },
                    new { studentProfileId = studentIds[1], status = "Absent", notes = "sick" }
                }
            },
            token);
        Assert.Contains(marked.EnumerateArray(), r => r.GetProperty("status").GetString() == "Absent");

        var session = await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions/{sessionId}", token);
        Assert.Equal(2, session.GetProperty("markedCount").GetInt32());
        Assert.Equal(1, session.GetProperty("presentCount").GetInt32());

        // A student who is not enrolled cannot be marked.
        var outsider = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Not Enrolled" }, token);
        using var bad = await Client.PutJsonAsync(
            $"/api/sessions/{sessionId}/attendance",
            new { records = new object[] { new { studentProfileId = outsider.Id(), status = "Present" } } },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("Attendance.StudentNotEnrolled", await bad.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Capacity_duplicates_and_overlapping_slots_are_rejected()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sched-rules");
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 1, capacity: 1);

        using var duplicate = await Client.PostJsonAsync($"/api/groups/{groupId}/enrollments", new { studentProfileId = studentIds[0] }, token);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var extra = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Late Joiner" }, token);
        using var full = await Client.PostJsonAsync($"/api/groups/{groupId}/enrollments", new { studentProfileId = extra.Id() }, token);
        Assert.Equal("Groups.Full", await full.ReadProblemTitleAsync());

        using var overlap = await Client.PostJsonAsync(
            $"/api/groups/{groupId}/slots",
            new { dayOfWeek = "Sunday", startTime = "16:30:00", endTime = "17:30:00" },
            token);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        using var backwards = await Client.PostJsonAsync(
            $"/api/groups/{groupId}/slots",
            new { dayOfWeek = "Monday", startTime = "17:00:00", endTime = "16:00:00" },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        // A group with active enrollments cannot be deleted.
        using var delete = await Client.DeleteAsync($"/api/groups/{groupId}", token);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Groups_and_sessions_are_tenant_isolated()
    {
        var (_, tokenA) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sched-iso-a");
        var (_, tokenB) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sched-iso-b");
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, tokenA, 1);

        using var foreignGroup = await Client.GetAsync($"/api/groups/{groupId}", tokenB);
        Assert.Equal(HttpStatusCode.NotFound, foreignGroup.StatusCode);

        using var foreignGenerate = await Client.PostJsonAsync(
            "/api/sessions/generate", new { from = "2026-11-01", to = "2026-11-07", studyGroupId = groupId }, tokenB);
        Assert.Equal(HttpStatusCode.NotFound, foreignGenerate.StatusCode);

        // Tenant B cannot enroll tenant A's student into its own group either.
        var (groupB, _, _) = await SchoolArrangement.GroupWithStudentsAsync(Client, tokenB, 0, name: "B group");
        using var crossEnroll = await Client.PostJsonAsync($"/api/groups/{groupB}/enrollments", new { studentProfileId = studentIds[0] }, tokenB);
        Assert.Equal(HttpStatusCode.NotFound, crossEnroll.StatusCode);
    }
}

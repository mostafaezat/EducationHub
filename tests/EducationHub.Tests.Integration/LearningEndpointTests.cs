using System.Net;

namespace EducationHub.Tests.Integration;

/// <summary>Mission Phase 6: homework, exams and in-app notifications.</summary>
[Collection(ApiCollection.Name)]
public sealed class LearningEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Homework_notifies_student_and_parent_accounts_and_submissions_are_graded()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "learn-hw");
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 1);

        // An enrolled student with an account, linked to a parent with an account.
        var studentEmail = ApiScenarios.NewEmail("hwstu");
        var parentEmail = ApiScenarios.NewEmail("hwpar");
        var student = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/students", new { fullName = "Notified Student", accountEmail = studentEmail, accountPassword = "Stud3ntPass" }, token);
        var parent = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/parents", new { fullName = "Notified Parent", email = parentEmail, accountPassword = "Par3ntPass" }, token);
        await ModuleTestHelpers.PutDataAsync(
            Client, $"/api/students/{student.Id()}/guardians",
            new { guardians = new object[] { new { parentProfileId = parent.Id(), relationship = "Mother", isPrimary = true } } }, token);
        await ModuleTestHelpers.PostDataAsync(
            Client, $"/api/groups/{groupId}/enrollments", new { studentProfileId = student.Id(), enrolledOn = "2026-09-01" }, token);

        var homework = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/homework",
            new { studyGroupId = groupId, title = "Chapter 3", dueDate = "2026-11-20", maxGrade = 10 }, token);
        Assert.Equal(2, homework.GetProperty("rosterSize").GetInt32());

        var studentToken = await ApiScenarios.LoginTokenAsync(Client, studentEmail, "Stud3ntPass");
        var parentToken = await ApiScenarios.LoginTokenAsync(Client, parentEmail, "Par3ntPass");

        foreach (var t in new[] { studentToken, parentToken })
        {
            var feed = await ModuleTestHelpers.GetDataAsync(Client, "/api/notifications", t);
            Assert.Equal(1, feed.GetProperty("unreadCount").GetInt32());
            Assert.Contains("Chapter 3", feed.GetProperty("items")[0].GetProperty("title").GetString());
        }

        // Marking read only touches the caller's own notifications.
        await ModuleTestHelpers.PostDataAsync(Client, "/api/notifications/read-all", new { }, studentToken);
        Assert.Equal(0, (await ModuleTestHelpers.GetDataAsync(Client, "/api/notifications", studentToken)).GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, (await ModuleTestHelpers.GetDataAsync(Client, "/api/notifications", parentToken)).GetProperty("unreadCount").GetInt32());

        // Students and parents cannot manage homework.
        using var forbidden = await Client.GetAsync("/api/homework", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var rows = await ModuleTestHelpers.PutDataAsync(
            Client, $"/api/homework/{homework.Id()}/submissions",
            new
            {
                submissions = new object[]
                {
                    new { studentProfileId = student.Id(), status = "Graded", grade = 9, feedback = "Good" },
                    new { studentProfileId = studentIds[0], status = "Missing" }
                }
            },
            token);
        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("status").GetString() == "Graded" && r.Dec("grade") == 9m);

        using var tooHigh = await Client.PutJsonAsync(
            $"/api/homework/{homework.Id()}/submissions",
            new { submissions = new object[] { new { studentProfileId = student.Id(), status = "Graded", grade = 11 } } },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);

        var list = await ModuleTestHelpers.GetDataAsync(Client, $"/api/homework?studyGroupId={groupId}", token);
        Assert.Equal(1, list[0].GetProperty("graded").GetInt32());
    }

    [Fact]
    public async Task Exam_results_are_recorded_bounded_and_averaged()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "learn-ex");
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 3);

        var exam = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/exams", new { studyGroupId = groupId, title = "Midterm", examDate = "2026-11-15", maxScore = 50 }, token);

        var results = await ModuleTestHelpers.PutDataAsync(
            Client, $"/api/exams/{exam.Id()}/results",
            new
            {
                results = new object[]
                {
                    new { studentProfileId = studentIds[0], score = 40 },
                    new { studentProfileId = studentIds[1], score = 30 },
                    new { studentProfileId = studentIds[2], isAbsent = true }
                }
            },
            token);
        Assert.Equal(3, results.GetArrayLength());
        Assert.All(results.EnumerateArray(), r => Assert.True(r.GetProperty("recorded").GetBoolean()));

        var exams = await ModuleTestHelpers.GetDataAsync(Client, $"/api/exams?studyGroupId={groupId}", token);
        Assert.Equal(3, exams[0].GetProperty("resultsRecorded").GetInt32());
        Assert.Equal(35m, exams[0].Dec("averageScore"));

        using var above = await Client.PutJsonAsync(
            $"/api/exams/{exam.Id()}/results",
            new { results = new object[] { new { studentProfileId = studentIds[0], score = 51 } } },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, above.StatusCode);

        // Another tenant cannot see or grade the exam.
        var (_, otherToken) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "learn-ex-b");
        using var foreign = await Client.GetAsync($"/api/exams/{exam.Id()}/results", otherToken);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var missingGroup = await Client.PostJsonAsync(
            "/api/exams", new { studyGroupId = groupId, title = "X", examDate = "2026-11-15", maxScore = 10 }, otherToken);
        Assert.Equal(HttpStatusCode.NotFound, missingGroup.StatusCode);
    }
}

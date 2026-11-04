using System.Net;
using EducationHub.Shared.Common.Constants;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Mission Phase 8 security matrix for the endpoints added in Phases 2.5b-6: 401 without a
/// token, 403 for roles without the permission, the Accountant's exact grants, and 404 for
/// another tenant's ids on reads, updates and deletes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OperationsSecurityTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    /// <summary>Read endpoints of every new module (all need some permission except notifications).</summary>
    private static readonly string[] ProtectedReads =
    [
        "/api/groups",
        "/api/sessions",
        "/api/fee-rules",
        "/api/charges",
        "/api/payments",
        "/api/finance/cash-accounts",
        "/api/finance/transactions",
        "/api/finance/expense-categories",
        "/api/finance/expenses",
        "/api/finance/teacher-payments",
        "/api/finance/summary?from=2026-11-01&to=2026-11-30",
        "/api/homework",
        "/api/exams",
        "/api/files"
    ];

    [Fact]
    public async Task Every_new_endpoint_rejects_anonymous_callers_with_401()
    {
        foreach (var path in ProtectedReads.Append("/api/notifications").Append("/api/dashboard/kpis"))
        {
            using var response = await Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"GET {path} returned {(int)response.StatusCode} without a token.");
        }

        using var post = await Client.PostJsonAsync("/api/payments", new { studentProfileId = Guid.NewGuid(), amount = 1m, method = "Cash" });
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task Student_and_parent_accounts_get_403_on_staff_endpoints_but_can_read_their_notifications()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sec-stu");
        var studentEmail = ApiScenarios.NewEmail("sec-stu");
        var parentEmail = ApiScenarios.NewEmail("sec-par");
        await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Locked Student", accountEmail = studentEmail, accountPassword = "Stud3ntPass" }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/parents", new { fullName = "Locked Parent", email = parentEmail, accountPassword = "Par3ntPass" }, token);

        foreach (var (email, password) in new[] { (studentEmail, "Stud3ntPass"), (parentEmail, "Par3ntPass") })
        {
            var caller = await ApiScenarios.LoginTokenAsync(Client, email, password);

            foreach (var path in ProtectedReads)
            {
                using var response = await Client.GetAsync(path, caller);
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{email}: GET {path} returned {(int)response.StatusCode}.");
            }

            using var write = await Client.PostJsonAsync("/api/finance/expense-categories", new { name = "Sneaky" }, caller);
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);

            using var feed = await Client.GetAsync("/api/notifications", caller);
            Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        }
    }

    [Fact]
    public async Task Accountant_can_run_fees_and_settlements_but_not_manage_people_or_teaching()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sec-acc");
        var email = ApiScenarios.NewEmail("accountant");
        await ApiScenarios.CreateUserAsync(Client, token, "Ali Accountant", email, "Acc0untantPass", [RoleNames.Accountant]);
        var accountant = await ApiScenarios.LoginTokenAsync(Client, email, "Acc0untantPass");

        foreach (var path in new[]
                 {
                     "/api/teachers", "/api/grades", "/api/students", "/api/groups", "/api/fee-rules", "/api/charges",
                     "/api/payments", "/api/finance/cash-accounts", "/api/finance/summary?from=2026-11-01&to=2026-11-30"
                 })
        {
            using var allowed = await Client.GetAsync(path, accountant);
            Assert.True(allowed.StatusCode == HttpStatusCode.OK, $"Accountant GET {path} returned {(int)allowed.StatusCode}.");
        }

        foreach (var path in new[] { "/api/homework", "/api/exams", "/api/sessions" })
        {
            using var denied = await Client.GetAsync(path, accountant);
            Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, $"Accountant GET {path} returned {(int)denied.StatusCode}.");
        }

        using var createGroup = await Client.PostJsonAsync("/api/groups", new { name = "X", subjectId = Guid.NewGuid() }, accountant);
        Assert.Equal(HttpStatusCode.Forbidden, createGroup.StatusCode);
        using var createStudent = await Client.PostJsonAsync("/api/students", new { fullName = "X" }, accountant);
        Assert.Equal(HttpStatusCode.Forbidden, createStudent.StatusCode);
        using var createTeacher = await Client.PostJsonAsync("/api/teachers", new { email = ApiScenarios.NewEmail("x"), password = "Teach3rPass", fullName = "X" }, accountant);
        Assert.Equal(HttpStatusCode.Forbidden, createTeacher.StatusCode);
        using var createGrade = await Client.PostJsonAsync("/api/grades", new { code = "GX", name = "X", order = 77 }, accountant);
        Assert.Equal(HttpStatusCode.Forbidden, createGrade.StatusCode);

        // The grants the accountant does have are real.
        var category = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expense-categories", new { name = "Utilities" }, accountant);
        Assert.Equal("Utilities", category.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Teacher_and_assistant_have_teaching_access_only()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sec-tch");
        var (groupId, _, _) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 1);

        var teacherEmail = ApiScenarios.NewEmail("tch");
        await ModuleTestHelpers.PostDataAsync(Client, "/api/teachers", new { email = teacherEmail, password = "Teach3rPass", fullName = "Teaching Only" }, token);
        var teacher = await ApiScenarios.LoginTokenAsync(Client, teacherEmail, "Teach3rPass");

        var assistantEmail = ApiScenarios.NewEmail("asst");
        await ApiScenarios.CreateUserAsync(Client, token, "Assistant", assistantEmail, "Ass1stantPass", [RoleNames.Assistant]);
        var assistant = await ApiScenarios.LoginTokenAsync(Client, assistantEmail, "Ass1stantPass");

        // Teachers manage homework; assistants only read it.
        var homework = await ModuleTestHelpers.PostDataAsync(Client, "/api/homework", new { studyGroupId = groupId, title = "T1", dueDate = "2026-11-20" }, teacher);
        using var assistantCreate = await Client.PostJsonAsync("/api/homework", new { studyGroupId = groupId, title = "A1", dueDate = "2026-11-20" }, assistant);
        Assert.Equal(HttpStatusCode.Forbidden, assistantCreate.StatusCode);
        using var assistantRead = await Client.GetAsync($"/api/homework/{homework.Id()}/submissions", assistant);
        Assert.Equal(HttpStatusCode.OK, assistantRead.StatusCode);

        foreach (var caller in new[] { teacher, assistant })
        {
            foreach (var path in new[] { "/api/fee-rules", "/api/charges", "/api/payments", "/api/finance/cash-accounts", "/api/finance/summary?from=2026-11-01&to=2026-11-30" })
            {
                using var denied = await Client.GetAsync(path, caller);
                Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, $"GET {path} returned {(int)denied.StatusCode} for a teaching role.");
            }

            using var groupWrite = await Client.PostJsonAsync("/api/groups", new { name = "Nope", subjectId = Guid.NewGuid() }, caller);
            Assert.Equal(HttpStatusCode.Forbidden, groupWrite.StatusCode);
        }
    }

    [Fact]
    public async Task Another_tenants_ids_answer_404_for_reads_updates_and_deletes()
    {
        var (_, tokenA) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sec-iso-a");
        var (_, tokenB) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "sec-iso-b");

        // Tenant A owns one of everything.
        var (groupId, _, studentIds) = await SchoolArrangement.GroupWithStudentsAsync(Client, tokenA, 1);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/sessions/generate", new { from = "2026-11-01", to = "2026-11-07", studyGroupId = groupId }, tokenA);
        var sessionId = (await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions?studyGroupId={groupId}", tokenA)).GetProperty("items")[0].Id();
        var rule = await ModuleTestHelpers.PostDataAsync(Client, "/api/fee-rules", new { name = "A rule", amount = 100m, frequency = "Monthly" }, tokenA);
        var charge = await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = studentIds[0], description = "A", amount = 100m, dueDate = "2026-11-05" }, tokenA);
        var category = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expense-categories", new { name = "A cat" }, tokenA);
        var expense = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expenses", new { expenseCategoryId = category.Id(), amount = 10m, description = "A exp" }, tokenA);
        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", tokenA);
        var accountId = accounts[0].Id();
        var homework = await ModuleTestHelpers.PostDataAsync(Client, "/api/homework", new { studyGroupId = groupId, title = "A hw", dueDate = "2026-11-20" }, tokenA);
        var exam = await ModuleTestHelpers.PostDataAsync(Client, "/api/exams", new { studyGroupId = groupId, title = "A exam", examDate = "2026-11-20", maxScore = 10 }, tokenA);

        // Reads.
        foreach (var path in new[]
                 {
                     $"/api/groups/{groupId}", $"/api/groups/{groupId}/enrollments", $"/api/sessions/{sessionId}",
                     $"/api/sessions/{sessionId}/attendance", $"/api/students/{studentIds[0]}/balance",
                     $"/api/students/{studentIds[0]}/enrollments", $"/api/homework/{homework.Id()}/submissions",
                     $"/api/exams/{exam.Id()}/results"
                 })
        {
            using var response = await Client.GetAsync(path, tokenB);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Tenant B GET {path} returned {(int)response.StatusCode}.");
        }

        // Updates.
        var updates = new (string Path, object Body)[]
        {
            ($"/api/groups/{groupId}", new { name = "hijack" }),
            ($"/api/sessions/{sessionId}", new { status = "Cancelled" }),
            ($"/api/sessions/{sessionId}/attendance", new { records = new object[] { new { studentProfileId = studentIds[0], status = "Absent" } } }),
            ($"/api/fee-rules/{rule.Id()}", new { amount = 1m }),
            ($"/api/finance/cash-accounts/{accountId}", new { name = "hijack" }),
            ($"/api/homework/{homework.Id()}", new { title = "hijack" }),
            ($"/api/homework/{homework.Id()}/submissions", new { submissions = new object[] { new { studentProfileId = studentIds[0], status = "Missing" } } }),
            ($"/api/exams/{exam.Id()}", new { title = "hijack" }),
            ($"/api/exams/{exam.Id()}/results", new { results = new object[] { new { studentProfileId = studentIds[0], score = 1, isAbsent = false } } })
        };

        foreach (var (path, body) in updates)
        {
            using var response = await Client.PutJsonAsync(path, body, tokenB);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Tenant B PUT {path} returned {(int)response.StatusCode}.");
        }

        using (var cancel = await Client.PostJsonAsync($"/api/charges/{charge.Id()}/cancel", new { }, tokenB))
        {
            Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        }

        using (var enroll = await Client.PostJsonAsync($"/api/groups/{groupId}/enrollments", new { studentProfileId = studentIds[0] }, tokenB))
        {
            Assert.Equal(HttpStatusCode.NotFound, enroll.StatusCode);
        }

        using (var foreignAccount = await Client.PostJsonAsync("/api/finance/transactions", new { cashAccountId = accountId, direction = "In", amount = 5m }, tokenB))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignAccount.StatusCode);
        }

        using (var foreignCategory = await Client.PostJsonAsync("/api/finance/expenses", new { expenseCategoryId = category.Id(), amount = 5m, description = "x" }, tokenB))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignCategory.StatusCode);
        }

        // Deletes.
        foreach (var path in new[]
                 {
                     $"/api/groups/{groupId}", $"/api/fee-rules/{rule.Id()}", $"/api/finance/expenses/{expense.Id()}",
                     $"/api/homework/{homework.Id()}", $"/api/exams/{exam.Id()}"
                 })
        {
            using var response = await Client.DeleteAsync(path, tokenB);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"Tenant B DELETE {path} returned {(int)response.StatusCode}.");
        }

        // Lists of tenant B never contain tenant A's rows.
        foreach (var (path, id) in new[]
                 {
                     ("/api/groups", groupId), ("/api/sessions", sessionId), ("/api/charges", charge.Id()),
                     ("/api/finance/expenses", expense.Id())
                 })
        {
            var page = await ModuleTestHelpers.GetDataAsync(Client, path, tokenB);
            Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), i => i.Id() == id);
        }

        foreach (var (path, id) in new[]
                 {
                     ("/api/fee-rules", rule.Id()), ("/api/homework", homework.Id()), ("/api/exams", exam.Id()),
                     ("/api/finance/cash-accounts", accountId), ("/api/finance/expense-categories", category.Id())
                 })
        {
            var list = await ModuleTestHelpers.GetDataAsync(Client, path, tokenB);
            Assert.DoesNotContain(list.EnumerateArray(), i => i.Id() == id);
        }

        // Tenant A's data is untouched.
        var groupAfter = await ModuleTestHelpers.GetDataAsync(Client, $"/api/groups/{groupId}", tokenA);
        Assert.NotEqual("hijack", groupAfter.GetProperty("name").GetString());
        var examsAfter = await ModuleTestHelpers.GetDataAsync(Client, $"/api/exams?studyGroupId={groupId}", tokenA);
        Assert.Equal("A exam", examsAfter[0].GetProperty("title").GetString());
    }
}

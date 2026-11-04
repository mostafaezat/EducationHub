using System.Net;
using EducationHub.Shared.Common.Constants;
using Microsoft.EntityFrameworkCore;

namespace EducationHub.Tests.Integration;

/// <summary>
/// End-to-end coverage of the teachers slice: atomic teacher-user creation, linking,
/// subject assignment, compensation precedence over HTTP, tenant isolation and the
/// permission boundary.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TeachersEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Create_teacher_with_new_account_creates_profile_user_and_teacher_role()
    {
        var (admin, token) = await ProvisionAndLoginAsync("teach-new");
        var subjectId = await CreateSubjectAsync(token, $"SUB{Guid.NewGuid():N}"[..10]);

        using var response = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = "T-100",
                email = ApiScenarios.NewEmail("teacher"),
                password = "Teach#123",
                fullName = "New Account Teacher",
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = new[] { subjectId }
            },
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");
        var teacherId = data.GetProperty("id").GetGuid();
        var userId = data.GetProperty("userId").GetGuid();
        var email = data.GetProperty("email").GetString();

        Assert.Equal("T-100", data.GetProperty("employeeNumber").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Single(data.GetProperty("subjects").EnumerateArray());

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var user = await db.Users.IgnoreQueryFilters()
                .SingleAsync(u => u.Id == userId);

            Assert.Equal(admin.TenantId, user.TenantId);
            Assert.Equal(email, user.Email);
            Assert.True(user.EmailConfirmed);

            var roleIds = await db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var roleName = await db.Roles.IgnoreQueryFilters()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Name)
                .SingleAsync();

            Assert.Equal(RoleNames.Teacher, roleName);

            var profile = await db.TeacherProfiles.IgnoreQueryFilters()
                .SingleAsync(t => t.Id == teacherId);

            Assert.Equal(user.Id, profile.UserId);
            Assert.Equal(admin.TenantId, profile.TenantId);

            var links = await db.TeacherSubjects.IgnoreQueryFilters()
                .Where(t => t.TeacherProfileId == teacherId)
                .ToListAsync();

            Assert.Single(links);
            Assert.Equal(subjectId, links[0].SubjectId);
        });
    }

    [Fact]
    public async Task Teacher_can_be_read_listed_updated_and_deleted()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-crud");
        var (teacherId, _) = await CreateTeacherAsync(token, ApiScenarios.NewEmail("crud"));

        using var read = await Client.GetAsync($"/api/teachers/{teacherId}", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readData = (await read.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(teacherId, readData.GetProperty("id").GetGuid());

        using var list = await Client.GetAsync("/api/teachers", token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.ReadJsonAsync()).GetProperty("data").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), i => i.GetProperty("id").GetGuid() == teacherId);

        using var update = await Client.PutJsonAsync(
            $"/api/teachers/{teacherId}",
            new { employeeNumber = "T-UPD", hireDate = "2026-02-01", isActive = false, notes = "updated" },
            token);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.ReadJsonAsync()).GetProperty("data");
        Assert.Equal("T-UPD", updated.GetProperty("employeeNumber").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());

        using var search = await Client.GetAsync("/api/teachers?search=T-UPD", token);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchItems = (await search.ReadJsonAsync()).GetProperty("data").GetProperty("items");
        Assert.Single(searchItems.EnumerateArray());

        using var delete = await Client.DeleteAsync($"/api/teachers/{teacherId}", token);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var gone = await Client.GetAsync($"/api/teachers/{teacherId}", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Subject_assignment_replaces_the_previous_set()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-subj");
        var subjectA = await CreateSubjectAsync(token, $"SBA{Guid.NewGuid():N}"[..10]);
        var subjectB = await CreateSubjectAsync(token, $"SBB{Guid.NewGuid():N}"[..10]);
        var (teacherId, _) = await CreateTeacherAsync(token, ApiScenarios.NewEmail("subj"), subjects: [subjectA]);

        using var replace = await Client.PutJsonAsync(
            $"/api/teachers/{teacherId}/subjects",
            new { subjectIds = new[] { subjectB } },
            token);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var subjects = (await replace.ReadJsonAsync()).GetProperty("data").GetProperty("subjects");
        var replaced = subjects.EnumerateArray().ToArray();
        Assert.Single(replaced);
        Assert.Equal(subjectB, replaced[0].GetProperty("id").GetGuid());

        using var clear = await Client.PutJsonAsync(
            $"/api/teachers/{teacherId}/subjects",
            new { subjectIds = Array.Empty<Guid>() },
            token);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Empty((await clear.ReadJsonAsync()).GetProperty("data").GetProperty("subjects").EnumerateArray());

        using var unknown = await Client.PutJsonAsync(
            $"/api/teachers/{teacherId}/subjects",
            new { subjectIds = new[] { Guid.NewGuid() } },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Create_is_atomic_when_a_late_check_fails()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-atom");

        using var first = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = "T-ATOM",
                email = ApiScenarios.NewEmail("atom-one"),
                password = "Teach#123",
                fullName = "First Atomic Teacher",
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var rejectedEmail = ApiScenarios.NewEmail("atom-two");

        // The employee-number check runs AFTER the account would have been created, so a
        // 409 here proves the whole operation rolled back rather than leaving an orphan.
        using var second = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = "T-ATOM",
                email = rejectedEmail,
                password = "Teach#123",
                fullName = "Second Atomic Teacher",
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Teachers.EmployeeNumberTaken", await second.ReadProblemTitleAsync());

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var orphan = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == rejectedEmail);

            Assert.Null(orphan);
        });
    }

    [Fact]
    public async Task Linking_an_existing_user_is_add_only_and_allowed_only_once()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-link");
        var existingEmail = ApiScenarios.NewEmail("link-existing");
        var existingUserId = await ApiScenarios.CreateUserAsync(
            Client, token, "Linkable Teacher", existingEmail, "Orig#Pass123", [RoleNames.Teacher]);

        using var link = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = "T-LINK",
                email = (string?)null,
                password = (string?)null,
                fullName = (string?)null,
                phoneNumber = (string?)null,
                userId = existingUserId,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal(existingUserId, (await link.ReadJsonAsync()).GetProperty("data").GetProperty("userId").GetGuid());

        // Second link of the same account is a conflict, not a duplicate profile.
        using var again = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = "T-LINK2",
                email = (string?)null,
                password = (string?)null,
                fullName = (string?)null,
                phoneNumber = (string?)null,
                userId = existingUserId,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Teachers.ProfileAlreadyExists", await again.ReadProblemTitleAsync());

        // The account itself was never adopted or modified: identity data is unchanged.
        using var userRead = await Client.GetAsync($"/api/users/{existingUserId}", token);
        Assert.Equal(HttpStatusCode.OK, userRead.StatusCode);
        var userData = (await userRead.ReadJsonAsync()).GetProperty("data");
        Assert.Equal("Linkable Teacher", userData.GetProperty("fullName").GetString());

        var session = await ApiScenarios.LoginAsync(Client, existingEmail, "Orig#Pass123");
        Assert.False(string.IsNullOrEmpty(session.AccessToken));
    }

    [Fact]
    public async Task Link_mode_rejects_credentials_and_new_account_mode_requires_them()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-mode");
        var userId = await ApiScenarios.CreateUserAsync(
            Client, token, "Mode Teacher", ApiScenarios.NewEmail("mode"), "Orig#Pass123", [RoleNames.Teacher]);

        using var mixed = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = (string?)null,
                email = ApiScenarios.NewEmail("mixed"),
                password = "Teach#123",
                fullName = "Mixed Mode",
                phoneNumber = (string?)null,
                userId,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);

        using var missingCredentials = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = (string?)null,
                email = (string?)null,
                password = (string?)null,
                fullName = (string?)null,
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, missingCredentials.StatusCode);
    }

    [Fact]
    public async Task Teacher_role_may_view_but_not_create()
    {
        var (_, adminToken) = await ProvisionAndLoginAsync("teach-bound");
        var teacherEmail = ApiScenarios.NewEmail("bound");
        await ApiScenarios.CreateUserAsync(Client, adminToken, "Boundary Teacher", teacherEmail, "Teach#123", [RoleNames.Teacher]);
        var teacherToken = await ApiScenarios.LoginTokenAsync(Client, teacherEmail, "Teach#123");

        using var list = await Client.GetAsync("/api/teachers", teacherToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using var create = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber = (string?)null,
                email = ApiScenarios.NewEmail("denied"),
                password = "Teach#123",
                fullName = "Denied Teacher",
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = Array.Empty<Guid>()
            },
            teacherToken);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        using var rules = await Client.GetAsync("/api/compensation-rules", teacherToken);
        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);

        using var createRule = await Client.PostJsonAsync(
            "/api/compensation-rules",
            new { teacherProfileId = (Guid?)null, subjectId = (Guid?)null, studyGroupId = (Guid?)null, rate = 10, rateBasis = "PerSession" },
            teacherToken);
        Assert.Equal(HttpStatusCode.Forbidden, createRule.StatusCode);
    }

    [Fact]
    public async Task Student_role_is_denied_the_teachers_surface()
    {
        var (_, adminToken) = await ProvisionAndLoginAsync("teach-student");
        var studentEmail = ApiScenarios.NewEmail("student");
        await ApiScenarios.CreateUserAsync(Client, adminToken, "Student User", studentEmail, "Stud#12345", [RoleNames.Student]);
        var studentToken = await ApiScenarios.LoginTokenAsync(Client, studentEmail, "Stud#12345");

        using var list = await Client.GetAsync("/api/teachers", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var get = await Client.GetAsync($"/api/teachers/{Guid.NewGuid()}", studentToken);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task Teachers_are_isolated_per_tenant()
    {
        var (tenantA, tokenA) = await ProvisionAndLoginAsync("teach-iso");
        var (teacherId, _) = await CreateTeacherAsync(tokenA, ApiScenarios.NewEmail("iso"));

        var (_, tokenB) = await ProvisionAndLoginAsync("teach-iso");

        using var listB = await Client.GetAsync("/api/teachers", tokenB);
        Assert.Equal(HttpStatusCode.OK, listB.StatusCode);
        var itemsB = (await listB.ReadJsonAsync()).GetProperty("data").GetProperty("items");
        Assert.DoesNotContain(itemsB.EnumerateArray(), i => i.GetProperty("id").GetGuid() == teacherId);

        using var directB = await Client.GetAsync($"/api/teachers/{teacherId}", tokenB);
        Assert.Equal(HttpStatusCode.NotFound, directB.StatusCode);

        await fixture.WithPlatformScopeAsync(async db =>
        {
            var profile = await db.TeacherProfiles.IgnoreQueryFilters()
                .SingleAsync(t => t.Id == teacherId);

            Assert.Equal(tenantA.TenantId, profile.TenantId);
        });
    }

    [Fact]
    public async Task Compensation_resolution_follows_the_precedence_ladder()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-comp");
        var subjectMath = await CreateSubjectAsync(token, $"SCM{Guid.NewGuid():N}"[..10]);
        var subjectEng = await CreateSubjectAsync(token, $"SCE{Guid.NewGuid():N}"[..10]);
        var (teacherId, _) = await CreateTeacherAsync(token, ApiScenarios.NewEmail("comp"), subjects: [subjectMath]);

        // Levels 6, 5, 3 and 2 all present; level 1 (group) is rejected until groups exist.
        Assert.Equal(HttpStatusCode.OK, (await PostRuleAsync(token, null, null, null, 10m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostRuleAsync(token, null, subjectMath, null, 20m)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostRuleAsync(token, teacherId, null, null, 30m)).StatusCode);

        using var level2 = await PostRuleAsync(token, teacherId, subjectMath, null, 40m);
        Assert.Equal(HttpStatusCode.OK, level2.StatusCode);

        using var mathResolve = await Client.GetAsync(
            $"/api/teachers/{teacherId}/compensation?subjectId={subjectMath}", token);
        Assert.Equal(HttpStatusCode.OK, mathResolve.StatusCode);
        var mathData = (await mathResolve.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(2, mathData.GetProperty("rank").GetInt32());
        Assert.Equal(40m, mathData.GetProperty("rule").GetProperty("rate").GetDecimal());

        // Different subject: the teacher+subject rule drops out, teacher-only wins.
        using var engResolve = await Client.GetAsync(
            $"/api/teachers/{teacherId}/compensation?subjectId={subjectEng}", token);
        var engData = (await engResolve.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(3, engData.GetProperty("rank").GetInt32());
        Assert.Equal(30m, engData.GetProperty("rule").GetProperty("rate").GetDecimal());

        // No subject: teacher-only beats subject-only and the default.
        using var bareResolve = await Client.GetAsync($"/api/teachers/{teacherId}/compensation", token);
        var bareData = (await bareResolve.ReadJsonAsync()).GetProperty("data");
        Assert.Equal(3, bareData.GetProperty("rank").GetInt32());
        Assert.Equal(30m, bareData.GetProperty("rule").GetProperty("rate").GetDecimal());

        using var rulesList = await Client.GetAsync("/api/compensation-rules", token);
        Assert.Equal(HttpStatusCode.OK, rulesList.StatusCode);
        Assert.Equal(4, (await rulesList.ReadJsonAsync()).GetProperty("data").GetProperty("totalItems").GetInt32());

        using var unknownTeacher = await Client.GetAsync($"/api/teachers/{Guid.NewGuid()}/compensation", token);
        Assert.Equal(HttpStatusCode.NotFound, unknownTeacher.StatusCode);
    }

    [Fact]
    public async Task Compensation_rule_guards_reject_groups_duplicates_and_negative_rates()
    {
        var (_, token) = await ProvisionAndLoginAsync("teach-rules");

        // Since Mission Phase 3 a group scope is accepted, but only for a group of this tenant.
        using var group = await PostRuleAsync(token, null, null, Guid.NewGuid(), 10m);
        Assert.Equal(HttpStatusCode.NotFound, group.StatusCode);
        Assert.Equal("Groups.NotFound", await group.ReadProblemTitleAsync());

        using var first = await PostRuleAsync(token, null, null, null, 10m);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var duplicate = await PostRuleAsync(token, null, null, null, 20m);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Compensation.RuleAlreadyExists", await duplicate.ReadProblemTitleAsync());

        using var negative = await PostRuleAsync(token, null, null, null, -5m);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        using var missingTeacher = await PostRuleAsync(token, Guid.NewGuid(), null, null, 10m);
        Assert.Equal(HttpStatusCode.NotFound, missingTeacher.StatusCode);
    }

    private async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);
        return (tenant, token);
    }

    private async Task<Guid> CreateSubjectAsync(string token, string code)
    {
        using var response = await Client.PostJsonAsync(
            "/api/subjects",
            new { code, name = $"Subject {code}", nameAr = (string?)null, description = (string?)null, descriptionAr = (string?)null, sortOrder = 1 },
            token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        return body.GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<(Guid TeacherId, Guid UserId)> CreateTeacherAsync(
        string token,
        string email,
        string? employeeNumber = null,
        IReadOnlyList<Guid>? subjects = null)
    {
        using var response = await Client.PostJsonAsync(
            "/api/teachers",
            new
            {
                employeeNumber,
                email,
                password = "Teach#123",
                fullName = "Fixture Teacher",
                phoneNumber = (string?)null,
                userId = (Guid?)null,
                hireDate = (string?)null,
                subjectIds = subjects ?? Array.Empty<Guid>()
            },
            token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = (await response.ReadJsonAsync()).GetProperty("data");
        return (data.GetProperty("id").GetGuid(), data.GetProperty("userId").GetGuid());
    }

    private Task<HttpResponseMessage> PostRuleAsync(
        string token,
        Guid? teacherProfileId,
        Guid? subjectId,
        Guid? studyGroupId,
        decimal rate)
        => Client.PostJsonAsync(
            "/api/compensation-rules",
            new { teacherProfileId, subjectId, studyGroupId, rate, rateBasis = "PerSession" },
            token);
}

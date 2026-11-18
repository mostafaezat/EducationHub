using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Mission Phase 2.5 endpoint coverage: students, parents and guardian links, including the
/// deferred ownership requirement P1-4 (a parent sees only their own children), tenant
/// isolation and permission denial.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StudentsEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Student_without_account_can_be_created_read_searched_updated_and_deleted()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-crud");

        var student = await CreateStudentAsync(token, new { fullName = "Omar Hassan", studentNumber = "s-001" });
        var id = student.GetProperty("id").GetGuid();

        Assert.Equal("S-001", student.GetProperty("studentNumber").GetString());
        Assert.Equal("Active", student.GetProperty("status").GetString());
        Assert.False(student.TryGetProperty("userId", out var userId) && userId.ValueKind != JsonValueKind.Null);

        using var get = await Client.GetAsync($"/api/students/{id}", token);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        using var search = await Client.GetAsync("/api/students?search=Omar", token);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var items = (await search.ReadJsonAsync()).GetProperty("data").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);

        using var update = await Client.PutJsonAsync(
            $"/api/students/{id}",
            new { status = "Graduated", notes = "Top of class" },
            token);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.ReadJsonAsync()).GetProperty("data");
        Assert.Equal("Graduated", updated.GetProperty("status").GetString());
        Assert.Equal("Top of class", updated.GetProperty("notes").GetString());

        using var delete = await Client.DeleteAsync($"/api/students/{id}", token);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var gone = await Client.GetAsync($"/api/students/{id}", token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Duplicate_student_number_is_a_conflict()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-dup");

        await CreateStudentAsync(token, new { fullName = "First Student", studentNumber = "DUP-1" });

        using var second = await Client.PostJsonAsync(
            "/api/students",
            new { fullName = "Second Student", studentNumber = "dup-1" },
            token);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Students.StudentNumberTaken", await second.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Missing_name_is_rejected_with_400()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-val");

        using var response = await Client.PostJsonAsync("/api/students", new { studentNumber = "X1" }, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Student_created_with_an_account_can_sign_in()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-acct");
        var email = ApiScenarios.NewEmail("student");

        var student = await CreateStudentAsync(
            token,
            new { fullName = "Account Student", accountEmail = email, accountPassword = "Stud3ntPass" });

        Assert.Equal(email, student.GetProperty("accountEmail").GetString());

        var session = await ApiScenarios.LoginAsync(Client, email, "Stud3ntPass");
        var roles = session.Data.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString());
        Assert.Contains("Student", roles);
    }

    [Fact]
    public async Task Guardians_are_set_replaced_and_show_on_both_sides()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-guard");

        var student = await CreateStudentAsync(token, new { fullName = "Linked Student" });
        var studentId = student.GetProperty("id").GetGuid();
        var mother = (await CreateParentAsync(token, new { fullName = "Mona Ali", phoneNumber = "0100" })).GetProperty("id").GetGuid();
        var father = (await CreateParentAsync(token, new { fullName = "Ali Hassan" })).GetProperty("id").GetGuid();

        using var set = await Client.PutJsonAsync(
            $"/api/students/{studentId}/guardians",
            new
            {
                guardians = new object[]
                {
                    new { parentProfileId = mother, relationship = "Mother", isPrimary = true },
                    new { parentProfileId = father, relationship = "Father", isPrimary = false }
                }
            },
            token);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var guardians = (await set.ReadJsonAsync()).GetProperty("data").GetProperty("guardians");
        Assert.Equal(2, guardians.GetArrayLength());
        Assert.Equal(mother, guardians[0].GetProperty("parentProfileId").GetGuid());
        Assert.True(guardians[0].GetProperty("isPrimary").GetBoolean());

        using var parent = await Client.GetAsync($"/api/parents/{mother}", token);
        var children = (await parent.ReadJsonAsync()).GetProperty("data").GetProperty("children");
        Assert.Equal(studentId, Assert.Single(children.EnumerateArray()).GetProperty("studentProfileId").GetGuid());

        // Replace with just the father: the mother link is gone.
        using var replace = await Client.PutJsonAsync(
            $"/api/students/{studentId}/guardians",
            new { guardians = new object[] { new { parentProfileId = father, relationship = "Father", isPrimary = true } } },
            token);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        using var motherAfter = await Client.GetAsync($"/api/parents/{mother}", token);
        Assert.Equal(0, (await motherAfter.ReadJsonAsync()).GetProperty("data").GetProperty("children").GetArrayLength());
    }

    [Fact]
    public async Task Two_primary_guardians_or_an_unknown_parent_are_rejected()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-guard-bad");

        var studentId = (await CreateStudentAsync(token, new { fullName = "Guarded Student" })).GetProperty("id").GetGuid();
        var a = (await CreateParentAsync(token, new { fullName = "Parent A" })).GetProperty("id").GetGuid();
        var b = (await CreateParentAsync(token, new { fullName = "Parent B" })).GetProperty("id").GetGuid();

        using var twoPrimary = await Client.PutJsonAsync(
            $"/api/students/{studentId}/guardians",
            new
            {
                guardians = new object[]
                {
                    new { parentProfileId = a, relationship = "Mother", isPrimary = true },
                    new { parentProfileId = b, relationship = "Father", isPrimary = true }
                }
            },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, twoPrimary.StatusCode);

        using var unknown = await Client.PutJsonAsync(
            $"/api/students/{studentId}/guardians",
            new { guardians = new object[] { new { parentProfileId = Guid.NewGuid(), relationship = "Other", isPrimary = false } } },
            token);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Students.UnknownParent", await unknown.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Parent_sees_only_their_own_children()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-own");

        var emailA = ApiScenarios.NewEmail("parent-a");
        var emailB = ApiScenarios.NewEmail("parent-b");

        var parentA = (await CreateParentAsync(token, new { fullName = "Parent One", email = emailA, accountPassword = "Par3ntPass" })).GetProperty("id").GetGuid();
        var parentB = (await CreateParentAsync(token, new { fullName = "Parent Two", email = emailB, accountPassword = "Par3ntPass" })).GetProperty("id").GetGuid();

        var childA = (await CreateStudentAsync(token, new { fullName = "Child of One" })).GetProperty("id").GetGuid();
        var childB = (await CreateStudentAsync(token, new { fullName = "Child of Two" })).GetProperty("id").GetGuid();

        await LinkAsync(token, childA, parentA);
        await LinkAsync(token, childB, parentB);

        var parentToken = await ApiScenarios.LoginTokenAsync(Client, emailA, "Par3ntPass");

        using var mine = await Client.GetAsync("/api/parents/me/children", parentToken);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var children = (await mine.ReadJsonAsync()).GetProperty("data");
        var only = Assert.Single(children.EnumerateArray());
        Assert.Equal(childA, only.GetProperty("studentProfileId").GetGuid());

        // The parent role has no Students.View / Parents.View: the admin lists stay closed,
        // so the only path to a child's data is the account-scoped endpoint above.
        using var list = await Client.GetAsync("/api/students", parentToken);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using var other = await Client.GetAsync($"/api/students/{childB}", parentToken);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);

        using var otherParent = await Client.GetAsync($"/api/parents/{parentB}", parentToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherParent.StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_cannot_see_another_tenants_students()
    {
        var (_, tokenA) = await ProvisionAndLoginAsync("stu-iso-a");
        var (_, tokenB) = await ProvisionAndLoginAsync("stu-iso-b");

        var studentA = (await CreateStudentAsync(tokenA, new { fullName = "Isolated Student" })).GetProperty("id").GetGuid();
        var parentA = (await CreateParentAsync(tokenA, new { fullName = "Isolated Parent" })).GetProperty("id").GetGuid();

        using var direct = await Client.GetAsync($"/api/students/{studentA}", tokenB);
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);

        using var parent = await Client.GetAsync($"/api/parents/{parentA}", tokenB);
        Assert.Equal(HttpStatusCode.NotFound, parent.StatusCode);

        using var list = await Client.GetAsync("/api/students", tokenB);
        var items = (await list.ReadJsonAsync()).GetProperty("data").GetProperty("items");
        Assert.DoesNotContain(items.EnumerateArray(), s => s.GetProperty("id").GetGuid() == studentA);

        // Tenant B cannot link tenant A's parent to its own student either.
        var studentB = (await CreateStudentAsync(tokenB, new { fullName = "Tenant B Student" })).GetProperty("id").GetGuid();

        using var crossLink = await Client.PutJsonAsync(
            $"/api/students/{studentB}/guardians",
            new { guardians = new object[] { new { parentProfileId = parentA, relationship = "Other", isPrimary = true } } },
            tokenB);
        Assert.Equal(HttpStatusCode.BadRequest, crossLink.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_parent_removes_their_links()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-pdel");

        var studentId = (await CreateStudentAsync(token, new { fullName = "Orphan Check" })).GetProperty("id").GetGuid();
        var parentId = (await CreateParentAsync(token, new { fullName = "Leaving Parent" })).GetProperty("id").GetGuid();

        await LinkAsync(token, studentId, parentId);

        using var delete = await Client.DeleteAsync($"/api/parents/{parentId}", token);
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var student = await Client.GetAsync($"/api/students/{studentId}", token);
        Assert.Equal(0, (await student.ReadJsonAsync()).GetProperty("data").GetProperty("guardians").GetArrayLength());
    }

    [Fact]
    public async Task Registering_a_student_with_a_guardian_creates_the_parent_account_with_a_temporary_password()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-guard");
        var parentEmail = ApiScenarios.NewEmail("guard");
        var parentPhone = RandomMobile();

        var student = await CreateStudentAsync(token, new
        {
            fullName = "Yassin Adel",
            guardian = new { fullName = "Adel Mahmoud", phoneNumber = parentPhone, email = parentEmail, relationship = "Father", createAccount = true }
        });

        var guardian = student.GetProperty("newGuardian");
        Assert.True(guardian.GetProperty("accountCreated").GetBoolean());
        Assert.False(guardian.GetProperty("linkedExistingParent").GetBoolean());
        var temporary = guardian.GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(10, temporary.Length);

        var link = Assert.Single(student.GetProperty("guardians").EnumerateArray());
        Assert.Equal("Father", link.GetProperty("relationship").GetString());

        // The parent signs in with the temporary password and is told to change it - with the
        // e-mail, or with the mobile number typed in any common shape.
        var session = await ApiScenarios.LoginAsync(Client, parentEmail, temporary);
        Assert.True(session.Data.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        await ApiScenarios.LoginAsync(Client, parentPhone, temporary);
        await ApiScenarios.LoginAsync(Client, "+2" + parentPhone, temporary);

        // The development WhatsApp provider (Log) accepted the sign-in details.
        Assert.True(guardian.GetProperty("whatsAppSent").GetBoolean());

        // A second child with the same guardian e-mail reuses the parent instead of duplicating it.
        var sibling = await CreateStudentAsync(token, new
        {
            fullName = "Malak Adel",
            guardian = new { fullName = "Adel Mahmoud", email = parentEmail, relationship = "Father", createAccount = true }
        });
        var reused = sibling.GetProperty("newGuardian");
        Assert.True(reused.GetProperty("linkedExistingParent").GetBoolean());
        Assert.False(reused.GetProperty("accountCreated").GetBoolean());
        Assert.Equal(guardian.GetProperty("parentProfileId").GetGuid(), reused.GetProperty("parentProfileId").GetGuid());

        // Asking for an account without an e-mail is rejected and nothing is created.
        using var noEmail = await Client.PostJsonAsync("/api/students", new
        {
            fullName = "Nour Adel",
            guardian = new { fullName = "Someone Else", relationship = "Mother", createAccount = true }
        }, token);
        Assert.Equal(HttpStatusCode.BadRequest, noEmail.StatusCode);
    }

    [Fact]
    public async Task A_guardian_without_e_mail_gets_an_account_that_signs_in_with_the_mobile_number()
    {
        var (_, token) = await ProvisionAndLoginAsync("stu-phone");
        var phone = RandomMobile();

        var student = await CreateStudentAsync(token, new
        {
            fullName = "Hana Samir",
            guardian = new { fullName = "Samir Fathy", phoneNumber = phone, relationship = "Father", createAccount = true }
        });

        var guardian = student.GetProperty("newGuardian");
        Assert.True(guardian.GetProperty("accountCreated").GetBoolean());
        Assert.Equal("+2" + phone, guardian.GetProperty("loginName").GetString());

        var session = await ApiScenarios.LoginAsync(Client, phone, guardian.GetProperty("temporaryPassword").GetString()!);
        Assert.Contains("Parent", session.Data.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // A second child of the same father (same mobile, no e-mail) is linked to him.
        var sibling = await CreateStudentAsync(token, new
        {
            fullName = "Omar Samir",
            guardian = new { fullName = "Samir Fathy", phoneNumber = phone, relationship = "Father", createAccount = true }
        });
        Assert.True(sibling.GetProperty("newGuardian").GetProperty("linkedExistingParent").GetBoolean());
    }

    /// <summary>A unique Egyptian-style mobile number (01XXXXXXXXX) per call; the test database persists across runs.</summary>
    private static string RandomMobile()
        => "010" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(TenantAdmin Tenant, string Token)> ProvisionAndLoginAsync(string prefix)
    {
        var platform = await ApiScenarios.PlatformAdminTokenAsync(Client);
        var tenant = await ApiScenarios.ProvisionTenantAsync(Client, platform, prefix);
        var token = await ApiScenarios.LoginTokenAsync(Client, tenant.Email, tenant.Password);

        return (tenant, token);
    }

    private async Task<JsonElement> CreateStudentAsync(string token, object body)
    {
        using var response = await Client.PostJsonAsync("/api/students", body, token);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create student returned {(int)response.StatusCode}: {text}");

        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<JsonElement> CreateParentAsync(string token, object body)
    {
        using var response = await Client.PostJsonAsync("/api/parents", body, token);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create parent returned {(int)response.StatusCode}: {text}");

        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task LinkAsync(string token, Guid studentId, Guid parentId)
    {
        using var response = await Client.PutJsonAsync(
            $"/api/students/{studentId}/guardians",
            new { guardians = new object[] { new { parentProfileId = parentId, relationship = "Guardian", isPrimary = true } } },
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

using System.Net;
using EducationHub.Application.Common.Helpers;
using EducationHub.API.Seed;
using EducationHub.Application.Common.Interfaces;
using EducationHub.Shared.Common.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The public pre-registration form and the staff review of its requests: anonymous submission,
/// duplicates, unknown centres, approval through the student registration path (guardian reuse
/// by mobile number included), rejection, permissions and tenant isolation.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RegistrationRequestsEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task An_anonymous_parent_submits_a_request_that_staff_see_and_are_notified_of()
    {
        var centre = await NewCentreAsync("reg-submit");
        var grade = await ModuleTestHelpers.PostDataAsync(Client, "/api/grades", new { code = "G1", name = "Grade 1", nameAr = "الصف الأول", order = 1 }, centre.Token);
        var subject = await ModuleTestHelpers.PostDataAsync(Client, "/api/subjects", new { code = "MATH", name = "Mathematics", nameAr = "رياضيات", sortOrder = 1 }, centre.Token);

        // Staff get the slug for the shareable link.
        var link = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests/link", centre.Token);
        Assert.Equal(centre.Slug, link.GetProperty("tenantSlug").GetString());

        // The form needs no token.
        using var form = await Client.GetAsync($"/api/public/registration/{centre.Slug}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var formData = (await form.ReadJsonAsync()).GetProperty("data");
        Assert.Equal("Isolation Academy", formData.GetProperty("centreName").GetString());
        Assert.Contains(formData.GetProperty("gradeLevels").EnumerateArray(), g => g.Id() == grade.Id());
        Assert.Contains(formData.GetProperty("subjects").EnumerateArray(), s => s.Id() == subject.Id());

        var phone = PhonePasswordResetTests.RandomMobile();

        using var submit = await Client.PostJsonAsync($"/api/public/registration/{centre.Slug}", new
        {
            guardianName = "Hany Fouad",
            guardianPhone = phone,
            relationship = "Father",
            studentName = "Laila Hany",
            birthDate = "2016-05-01",
            gradeLevelId = grade.Id(),
            school = "Future School",
            subjectIds = new[] { subject.Id() },
            notes = "Evenings please"
        });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        var list = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests?status=Pending", centre.Token);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Laila Hany", item.GetProperty("studentName").GetString());
        Assert.Equal(PhoneNumberFormat.ToCanonical(phone), item.GetProperty("guardianPhone").GetString());
        Assert.Equal("رياضيات", item.GetProperty("preferredSubjects").GetString());
        Assert.Equal(grade.Id(), item.GetProperty("gradeLevelId").GetGuid());

        // Search by the number in another shape.
        var found = await ModuleTestHelpers.GetDataAsync(Client, $"/api/registration-requests?search={Uri.EscapeDataString("+2" + phone)}", centre.Token);
        Assert.Equal(1, found.GetProperty("totalItems").GetInt32());

        // The centre's admin was notified.
        var feed = await ModuleTestHelpers.GetDataAsync(Client, "/api/notifications", centre.Token);
        Assert.Contains(feed.GetProperty("items").EnumerateArray(), n => n.GetProperty("link").GetString() == "/registration-requests");

        // A bot filling the hidden field gets the same answer and nothing is stored.
        using var bot = await Client.PostJsonAsync($"/api/public/registration/{centre.Slug}", new
        {
            guardianName = "Bot",
            guardianPhone = PhonePasswordResetTests.RandomMobile(),
            relationship = "Father",
            studentName = "Bot Child",
            website = "http://spam.example"
        });
        Assert.Equal(HttpStatusCode.OK, bot.StatusCode);
        var after = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests", centre.Token);
        Assert.Equal(1, after.GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task A_duplicate_pending_request_is_a_conflict_and_an_unknown_centre_is_not_found()
    {
        var centre = await NewCentreAsync("reg-dup");
        var phone = PhonePasswordResetTests.RandomMobile();

        await SubmitAsync(centre.Slug, phone, "Omar Tarek");

        // Same child, same number typed differently.
        using var duplicate = await Client.PostJsonAsync($"/api/public/registration/{centre.Slug}", new
        {
            guardianName = "Tarek",
            guardianPhone = "+2" + phone,
            relationship = "Father",
            studentName = "Omar Tarek"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("RegistrationRequests.AlreadyExists", await duplicate.ReadProblemTitleAsync());

        // A sibling from the same number is fine.
        await SubmitAsync(centre.Slug, phone, "Nada Tarek");

        using var unknownForm = await Client.GetAsync("/api/public/registration/no-such-centre-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(HttpStatusCode.NotFound, unknownForm.StatusCode);

        using var unknownSubmit = await Client.PostJsonAsync("/api/public/registration/no-such-centre", new
        {
            guardianName = "X",
            guardianPhone = phone,
            relationship = "Father",
            studentName = "Y"
        });
        Assert.Equal(HttpStatusCode.NotFound, unknownSubmit.StatusCode);

        // Invalid input is rejected by validation.
        using var invalid = await Client.PostJsonAsync($"/api/public/registration/{centre.Slug}", new
        {
            guardianName = "",
            guardianPhone = "abc",
            relationship = "Father",
            studentName = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Approval_creates_the_student_links_the_existing_parent_by_phone_and_enrolls()
    {
        var centre = await NewCentreAsync("reg-approve");
        var phone = PhonePasswordResetTests.RandomMobile();

        // The father already has a child and an account at the centre.
        var first = await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new
        {
            fullName = "Older Sibling",
            guardian = new { fullName = "Karim Nabil", phoneNumber = phone, relationship = "Father", createAccount = true }
        }, centre.Token);
        var parentId = first.GetProperty("newGuardian").GetProperty("parentProfileId").GetGuid();

        var (groupId, _, _) = await SchoolArrangement.GroupWithStudentsAsync(Client, centre.Token, students: 0);

        // He registers the younger child from the public page with the number in another shape.
        await SubmitAsync(centre.Slug, "+2" + phone, "Younger Sibling", guardianName: "Karim N.");
        var requestId = await SinglePendingIdAsync(centre.Token);

        var approved = await ModuleTestHelpers.PostDataAsync(Client, $"/api/registration-requests/{requestId}/approve", new { studyGroupId = groupId }, centre.Token);

        var guardian = approved.GetProperty("student").GetProperty("newGuardian");
        Assert.True(guardian.GetProperty("linkedExistingParent").GetBoolean());
        Assert.False(guardian.GetProperty("accountCreated").GetBoolean());
        Assert.Equal(parentId, guardian.GetProperty("parentProfileId").GetGuid());
        Assert.True(approved.GetProperty("enrolled").GetBoolean());

        var request = approved.GetProperty("request");
        Assert.Equal("Approved", request.GetProperty("status").GetString());
        var studentId = approved.GetProperty("student").Id();
        Assert.Equal(studentId, request.GetProperty("createdStudentProfileId").GetGuid());

        var student = await ModuleTestHelpers.GetDataAsync(Client, $"/api/students/{studentId}", centre.Token);
        Assert.Equal("Younger Sibling", student.GetProperty("fullName").GetString());
        Assert.Equal(parentId, Assert.Single(student.GetProperty("guardians").EnumerateArray()).GetProperty("parentProfileId").GetGuid());

        var roster = await ModuleTestHelpers.GetDataAsync(Client, $"/api/groups/{groupId}/enrollments?activeOnly=true", centre.Token);
        Assert.Contains(roster.EnumerateArray(), e => e.GetProperty("studentProfileId").GetGuid() == studentId);

        // A reviewed request cannot be reviewed again.
        using var again = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/approve", new { }, centre.Token);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("RegistrationRequests.ReviewConflict", await again.ReadProblemTitleAsync());

        using var reject = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/reject", new { reason = "late" }, centre.Token);
        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
    }

    [Fact]
    public async Task Approval_of_a_new_guardian_creates_a_phone_only_account_and_sends_it_by_whatsapp()
    {
        var centre = await NewCentreAsync("reg-new");
        var phone = PhonePasswordResetTests.RandomMobile();

        await SubmitAsync(centre.Slug, phone, "Mariam Sherif", guardianName: "Sherif Ali");
        var requestId = await SinglePendingIdAsync(centre.Token);

        // The staff member corrects the spelling before approving.
        var approved = await ModuleTestHelpers.PostDataAsync(
            Client, $"/api/registration-requests/{requestId}/approve", new { studentName = "Mariam Sherif Ali" }, centre.Token);

        Assert.Equal("Mariam Sherif Ali", approved.GetProperty("student").GetProperty("fullName").GetString());
        Assert.False(approved.GetProperty("enrolled").GetBoolean());

        var guardian = approved.GetProperty("student").GetProperty("newGuardian");
        Assert.True(guardian.GetProperty("accountCreated").GetBoolean());
        Assert.True(guardian.GetProperty("whatsAppSent").GetBoolean());
        var temporary = guardian.GetProperty("temporaryPassword").GetString()!;

        var canonical = PhoneNumberFormat.ToCanonical(phone)!;
        Assert.Contains(fixture.WhatsApp.To(canonical), m => m.Kind == WhatsAppMessageKind.Credentials);

        var session = await ApiScenarios.LoginAsync(Client, phone, temporary);
        Assert.Contains("Parent", session.Data.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task A_request_is_rejected_with_a_reason()
    {
        var centre = await NewCentreAsync("reg-reject");
        await SubmitAsync(centre.Slug, PhonePasswordResetTests.RandomMobile(), "Rejected Child");
        var requestId = await SinglePendingIdAsync(centre.Token);

        using var noReason = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/reject", new { reason = "" }, centre.Token);
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await ModuleTestHelpers.PostDataAsync(Client, $"/api/registration-requests/{requestId}/reject", new { reason = "No places left" }, centre.Token);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("No places left", rejected.GetProperty("rejectionReason").GetString());

        var pending = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests?status=Pending", centre.Token);
        Assert.Equal(0, pending.GetProperty("totalItems").GetInt32());

        using var approve = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/approve", new { }, centre.Token);
        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);

        // An id that does not exist is a 404.
        using var missing = await Client.PostJsonAsync($"/api/registration-requests/{Guid.NewGuid()}/reject", new { reason = "x" }, centre.Token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Teachers_and_parents_cannot_see_or_review_requests()
    {
        var centre = await NewCentreAsync("reg-perm");
        await SubmitAsync(centre.Slug, PhonePasswordResetTests.RandomMobile(), "Perm Child");
        var requestId = await SinglePendingIdAsync(centre.Token);

        foreach (var role in new[] { RoleNames.Teacher, RoleNames.Parent })
        {
            var email = ApiScenarios.NewEmail("reg-" + role.ToLowerInvariant());
            await ApiScenarios.CreateUserAsync(Client, centre.Token, role + " User", email, "R0lePassw0rd", [role]);
            var token = await ApiScenarios.LoginTokenAsync(Client, email, "R0lePassw0rd");

            using var list = await Client.GetAsync("/api/registration-requests", token);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

            using var approve = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/approve", new { }, token);
            Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        }

        // The assistant role holds the permissions by default.
        var assistantEmail = ApiScenarios.NewEmail("reg-assistant");
        await ApiScenarios.CreateUserAsync(Client, centre.Token, "Assistant User", assistantEmail, "R0lePassw0rd", [RoleNames.Assistant]);
        var assistant = await ApiScenarios.LoginTokenAsync(Client, assistantEmail, "R0lePassw0rd");
        var visible = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests", assistant);
        Assert.Equal(1, visible.GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task One_centre_never_sees_or_reviews_another_centres_requests()
    {
        var a = await NewCentreAsync("reg-iso-a");
        var b = await NewCentreAsync("reg-iso-b");

        await SubmitAsync(a.Slug, PhonePasswordResetTests.RandomMobile(), "Centre A Child");
        var requestId = await SinglePendingIdAsync(a.Token);

        var seenByB = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests", b.Token);
        Assert.Equal(0, seenByB.GetProperty("totalItems").GetInt32());

        using var approve = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/approve", new { }, b.Token);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);

        using var reject = await Client.PostJsonAsync($"/api/registration-requests/{requestId}/reject", new { reason = "not ours" }, b.Token);
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);

        // Still pending in A.
        Assert.Equal(requestId, await SinglePendingIdAsync(a.Token));
    }

    [Fact]
    public async Task The_demo_seeder_adds_three_pending_requests_once()
    {
        var centre = await NewCentreAsync("reg-seed");
        async Task<int> SeedAsync()
        {
            using var scope = fixture.CreateScope();
            using var _ = EducationHub.Infrastructure.Services.PlatformScopeOverride.Begin();
            return await scope.ServiceProvider.GetRequiredService<DemoRegistrationRequestSeeder>().SeedAsync(centre.Slug);
        }

        Assert.Equal(3, await SeedAsync());
        Assert.Equal(0, await SeedAsync());

        var pending = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests?status=Pending", centre.Token);
        Assert.Equal(3, pending.GetProperty("totalItems").GetInt32());
    }

    private async Task SubmitAsync(string slug, string phone, string studentName, string guardianName = "Guardian")
    {
        using var response = await Client.PostJsonAsync($"/api/public/registration/{slug}", new
        {
            guardianName,
            guardianPhone = phone,
            relationship = "Father",
            studentName
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SinglePendingIdAsync(string token)
    {
        var list = await ModuleTestHelpers.GetDataAsync(Client, "/api/registration-requests?status=Pending", token);
        return Assert.Single(list.GetProperty("items").EnumerateArray()).Id();
    }

    private async Task<(string Slug, string Token)> NewCentreAsync(string prefix)
    {
        var (tenant, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, prefix);
        var slug = string.Empty;

        await fixture.WithPlatformScopeAsync(async db =>
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenant.TenantId).Select(static t => t.Slug).SingleAsync());

        return (slug, token);
    }

}

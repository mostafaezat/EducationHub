using EducationHub.API.Seed;
using EducationHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EducationHub.Tests.Integration;

/// <summary>
/// The development demo seeder for groups, sessions, money and learning: it fills a tenant with
/// consistent data that the real endpoints read back correctly, and a second run writes nothing.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DemoOperationsSeedingTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Seeds_consistent_operational_data_once()
    {
        var (tenant, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "demo-ops");
        await ModuleTestHelpers.PostDataAsync(Client, "/api/subjects", new { code = "MATH", name = "Mathematics", sortOrder = 1 }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/subjects", new { code = "PHYS", name = "Physics", sortOrder = 2 }, token);
        var teacher = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/teachers", new { email = ApiScenarios.NewEmail("demo-ops-t"), password = "Teach3rPass", fullName = "Demo Ops Teacher" }, token);

        string slug = string.Empty;
        await fixture.WithPlatformScopeAsync(async db =>
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenant.TenantId).Select(static t => t.Slug).SingleAsync());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await RunAsync(slug, today);

        var groups = await ModuleTestHelpers.GetDataAsync(Client, "/api/groups", token);
        Assert.Equal(3, groups.GetProperty("totalItems").GetInt32());
        Assert.All(groups.GetProperty("items").EnumerateArray(), g =>
        {
            Assert.True(g.GetProperty("activeEnrollments").GetInt32() > 0);
            Assert.NotEmpty(g.GetProperty("slots").EnumerateArray());
        });

        var students = await ModuleTestHelpers.GetDataAsync(Client, "/api/students?pageSize=100", token);
        Assert.Equal(12, students.GetProperty("totalItems").GetInt32());

        var sessions = await ModuleTestHelpers.GetDataAsync(
            Client, $"/api/sessions?from={today.AddDays(-28):yyyy-MM-dd}&to={today.AddDays(14):yyyy-MM-dd}&pageSize=100", token);
        var sessionItems = sessions.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(sessionItems, s => s.GetProperty("status").GetString() == "Completed" && s.GetProperty("markedCount").GetInt32() > 0);
        Assert.Contains(sessionItems, s => s.GetProperty("status").GetString() == "Scheduled");

        // Money: charges, payments and cash agree with each other.
        var charges = await ModuleTestHelpers.GetDataAsync(Client, "/api/charges?pageSize=100", token);
        var chargeItems = charges.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(chargeItems);
        Assert.All(chargeItems, c => Assert.True(c.Dec("amountPaid") <= c.Dec("amount")));

        var payments = await ModuleTestHelpers.GetDataAsync(Client, "/api/payments?pageSize=100", token);
        var paymentItems = payments.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(paymentItems);
        Assert.Equal(chargeItems.Sum(c => c.Dec("amountPaid")), paymentItems.Sum(p => p.Dec("allocated")));
        Assert.All(paymentItems, p => Assert.Equal(0m, p.Dec("unallocated")));

        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        var ledger = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/transactions?pageSize=100", token);
        var rows = ledger.GetProperty("items").EnumerateArray().ToList();

        foreach (var account in accounts.EnumerateArray())
        {
            var mine = rows.Where(r => r.GetProperty("cashAccountId").GetGuid() == account.Id()).ToList();
            var expected = account.Dec("openingBalance")
                + mine.Where(r => r.GetProperty("direction").GetString() == "In").Sum(r => r.Dec("amount"))
                - mine.Where(r => r.GetProperty("direction").GetString() == "Out").Sum(r => r.Dec("amount"));
            Assert.Equal(expected, account.Dec("balance"));
        }

        Assert.Equal(paymentItems.Count, rows.Count(r => r.GetProperty("sourceType").GetString() == "Payment"));

        var expenses = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/expenses?pageSize=100", token);
        Assert.Equal(5, expenses.GetProperty("totalItems").GetInt32());

        // Teacher compensation exists, and what is settled matches the payment.
        var settlement = await ModuleTestHelpers.GetDataAsync(Client, $"/api/finance/teachers/{teacher.Id()}/settlement", token);
        var items = settlement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        var teacherPayments = await ModuleTestHelpers.GetDataAsync(Client, $"/api/finance/teacher-payments?teacherProfileId={teacher.Id()}", token);
        // The settlement endpoint lists only what is still unpaid, so the settled rows are read
        // from the database: every compensation tied to a payment must add up to the payments.
        Assert.All(items, i => Assert.False(i.TryGetProperty("teacherPaymentId", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String));
        var teacherId = teacher.Id();
        decimal settledTotal = 0m;
        await fixture.WithPlatformScopeAsync(async db =>
            settledTotal = await db.SessionCompensations.IgnoreQueryFilters()
                .Where(c => c.TeacherProfileId == teacherId && c.TeacherPaymentId != null)
                .SumAsync(static c => (decimal?)c.Amount) ?? 0m);
        Assert.Equal(teacherPayments.GetProperty("items").EnumerateArray().Sum(p => p.Dec("amount")), settledTotal);

        // Learning.
        var homework = await ModuleTestHelpers.GetDataAsync(Client, "/api/homework", token);
        Assert.Equal(6, homework.GetArrayLength());
        Assert.Contains(homework.EnumerateArray(), h => h.GetProperty("graded").GetInt32() > 0);
        var exams = await ModuleTestHelpers.GetDataAsync(Client, "/api/exams", token);
        Assert.Equal(6, exams.GetArrayLength());
        Assert.Contains(exams.EnumerateArray(), e => e.TryGetProperty("averageScore", out var avg) && avg.ValueKind == System.Text.Json.JsonValueKind.Number);

        // A second run writes nothing.
        await RunAsync(slug, today);
        Assert.Equal(3, (await ModuleTestHelpers.GetDataAsync(Client, "/api/groups", token)).GetProperty("totalItems").GetInt32());
        Assert.Equal(chargeItems.Count, (await ModuleTestHelpers.GetDataAsync(Client, "/api/charges?pageSize=100", token)).GetProperty("totalItems").GetInt32());
        Assert.Equal(rows.Count, (await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/transactions?pageSize=100", token)).GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task Skips_a_tenant_without_subjects_or_an_unknown_slug()
    {
        var (tenant, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "demo-ops-empty");

        string slug = string.Empty;
        await fixture.WithPlatformScopeAsync(async db =>
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenant.TenantId).Select(static t => t.Slug).SingleAsync());

        await RunAsync(slug, DateOnly.FromDateTime(DateTime.UtcNow));
        await RunAsync("no-such-tenant-" + Guid.NewGuid().ToString("N")[..6], DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal(0, (await ModuleTestHelpers.GetDataAsync(Client, "/api/groups", token)).GetProperty("totalItems").GetInt32());
        Assert.Equal(0, (await ModuleTestHelpers.GetDataAsync(Client, "/api/students", token)).GetProperty("totalItems").GetInt32());
    }

    private async Task RunAsync(string slug, DateOnly today)
    {
        using var scope = fixture.CreateScope();
        using var _ = PlatformScopeOverride.Begin();
        await scope.ServiceProvider.GetRequiredService<DemoOperationsSeeder>().SeedAsync(slug, today);
    }
}

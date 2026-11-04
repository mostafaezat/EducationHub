using System.Net;

namespace EducationHub.Tests.Integration;

/// <summary>Mission Phases 4-5: fees, payments, cash, expenses, teacher settlements, reports, KPIs.</summary>
[Collection(ApiCollection.Name)]
public sealed class FinanceEndpointTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Monthly_charges_follow_rule_precedence_and_are_generated_once()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "fees");
        var (groupId, _, students) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 2);

        await ModuleTestHelpers.PostDataAsync(Client, "/api/fee-rules", new { name = "Default", amount = 300m, frequency = "Monthly" }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/fee-rules", new { name = "Math A", amount = 500m, frequency = "Monthly", studyGroupId = groupId }, token);

        var first = await ModuleTestHelpers.PostDataAsync(Client, "/api/charges/generate", new { period = "2026-11" }, token);
        Assert.Equal(2, first.GetProperty("created").GetInt32());

        var again = await ModuleTestHelpers.PostDataAsync(Client, "/api/charges/generate", new { period = "2026-11" }, token);
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(2, again.GetProperty("skipped").GetInt32());

        var charges = await ModuleTestHelpers.GetDataAsync(Client, $"/api/charges?studentProfileId={students[0]}", token);
        var charge = Assert.Single(charges.GetProperty("items").EnumerateArray());
        Assert.Equal(500m, charge.Dec("amount"));
        Assert.Equal("2026-11-05", charge.GetProperty("dueDate").GetString());

        using var badPeriod = await Client.PostJsonAsync("/api/charges/generate", new { period = "November" }, token);
        Assert.Equal("Charges.InvalidPeriod", await badPeriod.ReadProblemTitleAsync());
    }

    [Fact]
    public async Task Payment_allocates_fifo_posts_cash_and_void_reverses_everything()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "pay");
        var student = (await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Paying Student" }, token)).Id();

        var older = await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Sept", amount = 400m, dueDate = "2026-09-05" }, token);
        var newer = await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Oct", amount = 400m, dueDate = "2026-10-05" }, token);

        var payment = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/payments", new { studentProfileId = student, amount = 1000m, paidOn = "2026-10-10", method = "Cash" }, token);

        Assert.Equal(800m, payment.Dec("allocated"));
        Assert.Equal(200m, payment.Dec("unallocated"));
        Assert.Equal(2, payment.GetProperty("allocations").GetArrayLength());

        var balance = await ModuleTestHelpers.GetDataAsync(Client, $"/api/students/{student}/balance", token);
        Assert.Equal(0m, balance.Dec("outstanding"));
        Assert.Equal(200m, balance.Dec("credit"));

        // The payment landed in the auto-created default cash box.
        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        Assert.Equal(1000m, Assert.Single(accounts.EnumerateArray()).Dec("balance"));

        // A paid charge cannot be cancelled until the payment is voided.
        using var cancelPaid = await Client.PostJsonAsync($"/api/charges/{older.Id()}/cancel", new { }, token);
        Assert.Equal(HttpStatusCode.Conflict, cancelPaid.StatusCode);

        var voided = await ModuleTestHelpers.PostDataAsync(Client, $"/api/payments/{payment.Id()}/void", new { reason = "bounced" }, token);
        Assert.True(voided.GetProperty("isVoided").GetBoolean());

        var after = await ModuleTestHelpers.GetDataAsync(Client, $"/api/students/{student}/balance", token);
        Assert.Equal(800m, after.Dec("outstanding"));
        Assert.Equal(0m, after.Dec("totalPaid"));

        var accountsAfter = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        Assert.Equal(0m, Assert.Single(accountsAfter.EnumerateArray()).Dec("balance"));

        using var voidAgain = await Client.PostJsonAsync($"/api/payments/{payment.Id()}/void", new { }, token);
        Assert.Equal(HttpStatusCode.Conflict, voidAgain.StatusCode);

        var cancelled = await ModuleTestHelpers.PostDataAsync(Client, $"/api/charges/{newer.Id()}/cancel", new { }, token);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Expenses_teacher_settlement_summary_and_kpis()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "fin");
        var (groupId, _, _) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 1);

        var teacher = await ModuleTestHelpers.PostDataAsync(
            Client, "/api/teachers",
            new { email = ApiScenarios.NewEmail("teacher"), password = "Teach3rPass", fullName = "Paid Teacher" }, token);
        await ModuleTestHelpers.PutDataAsync(Client, $"/api/groups/{groupId}", new { teacherProfileId = teacher.Id() }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/compensation-rules", new { rate = 150m, rateBasis = "PerSession" }, token);

        await ModuleTestHelpers.PostDataAsync(Client, "/api/sessions/generate", new { from = "2026-11-01", to = "2026-11-14", studyGroupId = groupId }, token);
        var sessions = await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions?studyGroupId={groupId}", token);
        var sessionId = sessions.GetProperty("items")[0].Id();
        await ModuleTestHelpers.PutDataAsync(Client, $"/api/sessions/{sessionId}", new { status = "Completed" }, token);

        var computed = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/compensations/compute", new { from = "2026-11-01", to = "2026-11-30" }, token);
        Assert.Equal(1, computed.GetProperty("created").GetInt32());

        var recomputed = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/compensations/compute", new { from = "2026-11-01", to = "2026-11-30" }, token);
        Assert.Equal(1, recomputed.GetProperty("alreadyPriced").GetInt32());

        var settlement = await ModuleTestHelpers.GetDataAsync(Client, $"/api/finance/teachers/{teacher.Id()}/settlement", token);
        Assert.Equal(150m, settlement.Dec("unsettled"));

        var account = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/cash-accounts", new { name = "Safe", type = "Cash", openingBalance = 1000m, isDefault = true }, token);
        var paid = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/teacher-payments", new { teacherProfileId = teacher.Id(), paidOn = "2026-11-20" }, token);
        Assert.Equal(150m, paid.Dec("amount"));
        Assert.Equal(1, paid.GetProperty("settledSessions").GetInt32());

        var category = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expense-categories", new { name = "Rent" }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expenses", new { expenseCategoryId = category.Id(), amount = 250m, spentOn = "2026-11-21", description = "November rent" }, token);

        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        var safe = accounts.EnumerateArray().Single(a => a.Id() == account.Id());
        Assert.Equal(600m, safe.Dec("balance"));

        var summary = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/summary?from=2026-11-01&to=2026-11-30", token);
        Assert.Equal(250m, summary.Dec("expenses"));
        Assert.Equal(150m, summary.Dec("teacherPayments"));
        Assert.Equal(-400m, summary.Dec("net"));

        var kpis = await ModuleTestHelpers.GetDataAsync(Client, "/api/dashboard/kpis", token);
        Assert.Equal(1, kpis.GetProperty("activeGroups").GetInt32());
        Assert.True(kpis.TryGetProperty("outstandingFees", out _));
    }

    [Fact]
    public async Task Finance_is_tenant_isolated_and_closed_to_teachers()
    {
        var (_, tokenA) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "fin-iso-a");
        var (_, tokenB) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "fin-iso-b");

        var student = (await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Isolated Payer" }, tokenA)).Id();
        var payment = await ModuleTestHelpers.PostDataAsync(Client, "/api/payments", new { studentProfileId = student, amount = 50m, method = "Cash" }, tokenA);

        using var foreign = await Client.GetAsync($"/api/payments/{payment.Id()}", tokenB);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var foreignPay = await Client.PostJsonAsync("/api/payments", new { studentProfileId = student, amount = 50m, method = "Cash" }, tokenB);
        Assert.Equal(HttpStatusCode.NotFound, foreignPay.StatusCode);

        var teacherEmail = ApiScenarios.NewEmail("t");
        await ModuleTestHelpers.PostDataAsync(Client, "/api/teachers", new { email = teacherEmail, password = "Teach3rPass", fullName = "No Money" }, tokenA);
        var teacherToken = await ApiScenarios.LoginTokenAsync(Client, teacherEmail, "Teach3rPass");

        using var teacherPayments = await Client.GetAsync("/api/payments", teacherToken);
        Assert.Equal(HttpStatusCode.Forbidden, teacherPayments.StatusCode);

        // Teachers see the dashboard, but without money figures.
        var kpis = await ModuleTestHelpers.GetDataAsync(Client, "/api/dashboard/kpis", teacherToken);
        Assert.False(kpis.TryGetProperty("outstandingFees", out var fees) && fees.ValueKind != System.Text.Json.JsonValueKind.Null);
    }
}

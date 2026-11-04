using System.Net;
using System.Text.Json;

namespace EducationHub.Tests.Integration;

/// <summary>
/// Mission Phase 8 money invariants: allocations never exceed what was paid or owed, voids
/// leave no trace in reports, cash balances equal their ledger, failed or repeated requests
/// write nothing, and racing requests cannot double-apply money.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FinancialIntegrityTests(ApiTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Fifo_allocation_partial_payments_and_void_keep_charges_balance_cash_and_report_consistent()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "integ-fifo");
        var student = (await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Ledger Student" }, token)).Id();

        var sep = (await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Sep", amount = 300m, dueDate = "2026-09-05" }, token)).Id();
        var oct = (await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Oct", amount = 300m, dueDate = "2026-10-05" }, token)).Id();
        var nov = (await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Nov", amount = 300m, dueDate = "2026-11-05" }, token)).Id();

        var first = await ModuleTestHelpers.PostDataAsync(Client, "/api/payments", new { studentProfileId = student, amount = 450m, paidOn = "2026-11-02", method = "Cash" }, token);
        var second = await ModuleTestHelpers.PostDataAsync(Client, "/api/payments", new { studentProfileId = student, amount = 200m, paidOn = "2026-11-03", method = "Card" }, token);

        var charges = await ChargesAsync(student, token);
        Assert.Equal(300m, charges[sep].Dec("amountPaid"));
        Assert.Equal("Paid", charges[sep].GetProperty("status").GetString());
        Assert.Equal(300m, charges[oct].Dec("amountPaid"));
        Assert.Equal(50m, charges[nov].Dec("amountPaid"));
        Assert.Equal("PartiallyPaid", charges[nov].GetProperty("status").GetString());

        // Every allocated pound is accounted for exactly once.
        var allocated = first.Dec("allocated") + second.Dec("allocated");
        Assert.Equal(charges.Values.Sum(c => c.Dec("amountPaid")), allocated);
        Assert.Equal(650m, allocated);

        var balance = await ModuleTestHelpers.GetDataAsync(Client, $"/api/students/{student}/balance", token);
        Assert.Equal(250m, balance.Dec("outstanding"));
        Assert.Equal(0m, balance.Dec("credit"));

        await ModuleTestHelpers.PostDataAsync(Client, $"/api/payments/{first.Id()}/void", new { reason = "bounced" }, token);

        charges = await ChargesAsync(student, token);
        Assert.Equal(650m - 450m, charges.Values.Sum(c => c.Dec("amountPaid")));
        Assert.All(charges.Values, c => Assert.True(c.Dec("amountPaid") <= c.Dec("amount")));

        balance = await ModuleTestHelpers.GetDataAsync(Client, $"/api/students/{student}/balance", token);
        Assert.Equal(700m, balance.Dec("outstanding"));
        Assert.Equal(200m, balance.Dec("totalPaid"));

        // The report counts only the surviving payment.
        var summary = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/summary?from=2026-11-01&to=2026-11-30", token);
        Assert.Equal(200m, summary.Dec("income"));
        Assert.Equal(700m, summary.Dec("outstandingFees"));

        // The cash box equals its own ledger: opening + in - out.
        await AssertCashMatchesLedgerAsync(token);
        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        Assert.Equal(200m, accounts.EnumerateArray().Sum(a => a.Dec("balance")));
    }

    [Fact]
    public async Task Rejected_payments_and_teacher_payments_write_nothing()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "integ-atomic");
        var student = (await ModuleTestHelpers.PostDataAsync(Client, "/api/students", new { fullName = "Atomic Student" }, token)).Id();
        var charge = (await ModuleTestHelpers.PostDataAsync(Client, "/api/charges", new { studentProfileId = student, description = "Sep", amount = 100m, dueDate = "2026-09-05" }, token)).Id();
        await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/cash-accounts", new { name = "Box", type = "Cash", openingBalance = 0m, isDefault = true }, token);

        // Allocation above what the charge owes: refused, no payment, no cash, charge untouched.
        using (var over = await Client.PostJsonAsync(
                   "/api/payments",
                   new { studentProfileId = student, amount = 500m, method = "Cash", allocations = new[] { new { chargeId = charge, amount = 150m } } },
                   token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
            Assert.Equal("Payments.OverAllocated", await over.ReadProblemTitleAsync());
        }

        // Allocations above the payment amount: refused too.
        using (var overPayment = await Client.PostJsonAsync(
                   "/api/payments",
                   new { studentProfileId = student, amount = 50m, method = "Cash", allocations = new[] { new { chargeId = charge, amount = 80m } } },
                   token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, overPayment.StatusCode);
        }

        // A foreign / unknown cash account: 404 and nothing written.
        using (var unknownAccount = await Client.PostJsonAsync(
                   "/api/payments",
                   new { studentProfileId = student, amount = 10m, method = "Cash", cashAccountId = Guid.NewGuid() },
                   token))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownAccount.StatusCode);
        }

        var payments = await ModuleTestHelpers.GetDataAsync(Client, $"/api/payments?studentProfileId={student}&includeVoided=true", token);
        Assert.Equal(0, payments.GetProperty("totalItems").GetInt32());
        var transactions = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/transactions", token);
        Assert.Equal(0, transactions.GetProperty("totalItems").GetInt32());
        Assert.Equal(0m, (await ChargesAsync(student, token))[charge].Dec("amountPaid"));

        // Teacher payments must match the unsettled compensation exactly and cannot settle twice.
        var (groupId, _, _) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 1);
        var teacher = await ModuleTestHelpers.PostDataAsync(Client, "/api/teachers", new { email = ApiScenarios.NewEmail("integ-t"), password = "Teach3rPass", fullName = "Settled Teacher" }, token);
        await ModuleTestHelpers.PutDataAsync(Client, $"/api/groups/{groupId}", new { teacherProfileId = teacher.Id() }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/compensation-rules", new { rate = 100m, rateBasis = "PerSession" }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/sessions/generate", new { from = "2026-11-01", to = "2026-11-14", studyGroupId = groupId }, token);
        var sessions = await ModuleTestHelpers.GetDataAsync(Client, $"/api/sessions?studyGroupId={groupId}", token);
        foreach (var s in sessions.GetProperty("items").EnumerateArray())
        {
            await ModuleTestHelpers.PutDataAsync(Client, $"/api/sessions/{s.Id()}", new { status = "Completed" }, token);
        }

        await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/compensations/compute", new { from = "2026-11-01", to = "2026-11-30" }, token);

        foreach (var wrong in new[] { 150m, 250m })
        {
            using var mismatch = await Client.PostJsonAsync("/api/finance/teacher-payments", new { teacherProfileId = teacher.Id(), amount = wrong }, token);
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            Assert.Equal("TeacherPayments.AmountMismatch", await mismatch.ReadProblemTitleAsync());
        }

        var settlement = await ModuleTestHelpers.GetDataAsync(Client, $"/api/finance/teachers/{teacher.Id()}/settlement", token);
        Assert.Equal(200m, settlement.Dec("unsettled"));
        Assert.Equal(0, (await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/transactions", token)).GetProperty("totalItems").GetInt32());

        var paid = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/teacher-payments", new { teacherProfileId = teacher.Id(), amount = 200m }, token);
        Assert.Equal(2, paid.GetProperty("settledSessions").GetInt32());

        using (var again = await Client.PostJsonAsync("/api/finance/teacher-payments", new { teacherProfileId = teacher.Id() }, token))
        {
            Assert.Equal("TeacherPayments.NothingToPay", await again.ReadProblemTitleAsync());
        }

        await AssertCashMatchesLedgerAsync(token);
    }

    [Fact]
    public async Task Racing_requests_cannot_double_apply_money()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "integ-race");
        var (groupId, _, students) = await SchoolArrangement.GroupWithStudentsAsync(Client, token, 2);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/fee-rules", new { name = "Default", amount = 400m, frequency = "Monthly" }, token);

        // Monthly generation fired five times at once still bills each enrollment once.
        var generations = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Client.PostJsonAsync("/api/charges/generate", new { period = "2026-12", studyGroupId = groupId }, token)));
        foreach (var r in generations)
        {
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"generate returned {(int)r.StatusCode}");
            r.Dispose();
        }

        var billed = await ModuleTestHelpers.GetDataAsync(Client, "/api/charges?periodKey=2026-12", token);
        Assert.Equal(2, billed.GetProperty("totalItems").GetInt32());

        // Five payments racing for the same single charge never over-pay it.
        var student = students[0];
        var payments = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Client.PostJsonAsync("/api/payments", new { studentProfileId = student, amount = 400m, method = "Cash" }, token)));
        foreach (var r in payments)
        {
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"payment returned {(int)r.StatusCode}");
            r.Dispose();
        }

        var charge = (await ChargesAsync(student, token)).Values.Single();
        Assert.Equal(400m, charge.Dec("amountPaid"));

        var list = await ModuleTestHelpers.GetDataAsync(Client, $"/api/payments?studentProfileId={student}", token);
        var allocatedTotal = list.GetProperty("items").EnumerateArray().Sum(p => p.Dec("allocated"));
        Assert.Equal(400m, allocatedTotal);

        // Five voids of the same payment reverse it once.
        var paymentId = list.GetProperty("items").EnumerateArray().First(p => p.Dec("allocated") > 0).Id();
        var voids = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Client.PostJsonAsync($"/api/payments/{paymentId}/void", new { reason = "race" }, token)));
        Assert.Equal(1, voids.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(voids, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"void returned {(int)r.StatusCode}"));
        foreach (var r in voids)
        {
            r.Dispose();
        }

        Assert.Equal(0m, (await ChargesAsync(student, token)).Values.Single().Dec("amountPaid"));
        await AssertCashMatchesLedgerAsync(token);
    }

    [Fact]
    public async Task Expense_delete_reverses_its_cash_and_reports_reject_inverted_ranges()
    {
        var (_, token) = await ModuleTestHelpers.ProvisionAndLoginAsync(Client, "integ-exp");
        var category = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expense-categories", new { name = "Rent" }, token);
        var account = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/cash-accounts", new { name = "Safe", type = "Cash", openingBalance = 1000m, isDefault = true }, token);
        await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/transactions", new { cashAccountId = account.Id(), direction = "In", amount = 50m, occurredOn = "2026-11-02", description = "Donation" }, token);
        var expense = await ModuleTestHelpers.PostDataAsync(Client, "/api/finance/expenses", new { expenseCategoryId = category.Id(), amount = 300m, spentOn = "2026-11-03", description = "Rent" }, token);

        Assert.Equal(750m, await BalanceAsync(account.Id(), token));

        using (var delete = await Client.DeleteAsync($"/api/finance/expenses/{expense.Id()}", token))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        Assert.Equal(1050m, await BalanceAsync(account.Id(), token));
        var summary = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/summary?from=2026-11-01&to=2026-11-30", token);
        Assert.Equal(0m, summary.Dec("expenses"));
        await AssertCashMatchesLedgerAsync(token);

        using var inverted = await Client.GetAsync("/api/finance/summary?from=2026-11-30&to=2026-11-01", token);
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    private async Task<Dictionary<Guid, JsonElement>> ChargesAsync(Guid student, string token)
    {
        var page = await ModuleTestHelpers.GetDataAsync(Client, $"/api/charges?studentProfileId={student}&pageSize=100", token);
        return page.GetProperty("items").EnumerateArray().ToDictionary(c => c.Id());
    }

    private async Task<decimal> BalanceAsync(Guid accountId, string token)
    {
        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        return accounts.EnumerateArray().Single(a => a.Id() == accountId).Dec("balance");
    }

    /// <summary>Every account's reported balance equals opening + its In rows - its Out rows.</summary>
    private async Task AssertCashMatchesLedgerAsync(string token)
    {
        var accounts = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/cash-accounts", token);
        var ledger = await ModuleTestHelpers.GetDataAsync(Client, "/api/finance/transactions?pageSize=100", token);
        var rows = ledger.GetProperty("items").EnumerateArray().ToList();

        foreach (var account in accounts.EnumerateArray())
        {
            var id = account.Id();
            var mine = rows.Where(r => r.GetProperty("cashAccountId").GetGuid() == id).ToList();
            var expected = account.Dec("openingBalance")
                + mine.Where(r => r.GetProperty("direction").GetString() == "In").Sum(r => r.Dec("amount"))
                - mine.Where(r => r.GetProperty("direction").GetString() == "Out").Sum(r => r.Dec("amount"));
            Assert.Equal(expected, account.Dec("balance"));
        }
    }
}

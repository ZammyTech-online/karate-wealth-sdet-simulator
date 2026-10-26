using System.Globalization;
using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

[Collection("nordvik")]
public class LedgerAndCashTests
{
    private readonly NordvikFixture _f;
    public LedgerAndCashTests(NordvikFixture f) => _f = f;

    private static string Entry(string reference, string a1, string dr, string a2, string cr) =>
        $"{{\"reference\":\"{reference}\",\"lines\":[{{\"account\":\"{a1}\",\"debit\":\"{dr}\"}},{{\"account\":\"{a2}\",\"credit\":\"{cr}\"}}]}}";

    [Fact]
    public async Task Double_entry_rules()
    {
        var cust = $"customer:{Guid.NewGuid()}:cash";
        var reference = "T-" + Guid.NewGuid().ToString("N")[..8];
        var ok = await _f.Post(Ports.Ledger, "/api/v1/journal-entries", Entry(reference, "bank:client-money", "100.00", cust, "100.00"));
        Assert.Equal(201, ok.Code);
        Assert.Equal("100.00", ok.Str("totalDebit"));
        (await _f.Post(Ports.Ledger, "/api/v1/journal-entries", Entry(reference, "bank:client-money", "100.00", cust, "100.00"))).ShouldBeProblem(409, "DUPLICATE_REFERENCE");
        (await _f.Post(Ports.Ledger, "/api/v1/journal-entries", Entry(reference + "b", "bank:client-money", "100.00", cust, "99.99"))).ShouldBeProblem(422, "UNBALANCED_ENTRY");
        var bad = await _f.Post(Ports.Ledger, "/api/v1/journal-entries", "{\"reference\":\"x\",\"lines\":[{\"account\":\"caja\",\"debit\":\"1.001\",\"credit\":\"1\"}]}");
        bad.ShouldBeProblem(400, "VALIDATION_FAILED");
        Assert.Equal(3, bad.Json.GetProperty("errors").GetArrayLength());
        Assert.Equal("100.00", await Flows.Balance(_f, cust));
        Assert.Equal("LIABILITY", (await _f.Get(Ports.Ledger, $"/api/v1/accounts/{cust}/balance")).Str("type"));
        (await _f.Send(HttpMethod.Delete, Ports.Ledger, $"/api/v1/journal-entries/{ok.Str("id")}")).ShouldBeProblem(405, "ENTRY_IS_IMMUTABLE");
    }

    [Fact]
    public async Task Reversals_create_the_opposite_entry_once()
    {
        var cust = $"customer:{Guid.NewGuid()}:cash";
        var e = await _f.Post(Ports.Ledger, "/api/v1/journal-entries", Entry("R-" + Guid.NewGuid().ToString("N")[..8], cust, "40.00", "fees:income", "40.00"));
        var rev = await _f.Post(Ports.Ledger, $"/api/v1/journal-entries/{e.Str("id")}/reverse", "{\"reason\":\"error\"}");
        Assert.Equal(201, rev.Code);
        Assert.Equal(e.Str("id"), rev.Str("reversalOf"));
        Assert.Equal("40.00", rev.Json.GetProperty("lines")[0].GetProperty("credit").GetString());
        Assert.Equal(rev.Str("id"), (await _f.Get(Ports.Ledger, $"/api/v1/journal-entries/{e.Str("id")}")).Str("reversedBy"));
        Assert.Equal("0.00", await Flows.Balance(_f, cust));
        (await _f.Post(Ports.Ledger, $"/api/v1/journal-entries/{e.Str("id")}/reverse", "{}")).ShouldBeProblem(409, "ALREADY_REVERSED");
        (await _f.Post(Ports.Ledger, $"/api/v1/journal-entries/{rev.Str("id")}/reverse", "{}")).ShouldBeProblem(422, "CANNOT_REVERSE_A_REVERSAL");
        var tb = await _f.Get(Ports.Ledger, "/api/v1/trial-balance");
        Assert.True(tb.Json.GetProperty("balanced").GetBoolean());
        Assert.Equal(tb.Str("totalDebits"), tb.Str("totalCredits"));
    }

    [Fact]
    public async Task Holds_reserve_cash_idempotently()
    {
        var (customerId, _, _) = await Flows.FundedCustomer(_f, "1000.00");
        var reference = "ref-" + Guid.NewGuid().ToString("N")[..8];
        var h = await _f.Post(Ports.Cash, "/api/v1/holds", $"{{\"customerId\":\"{customerId}\",\"amount\":\"600.00\",\"reference\":\"{reference}\"}}");
        Assert.Equal(201, h.Code);
        Assert.Equal(200, (await _f.Post(Ports.Cash, "/api/v1/holds", $"{{\"customerId\":\"{customerId}\",\"amount\":\"600.00\",\"reference\":\"{reference}\"}}")).Code);
        var cash = await Flows.Cash(_f, customerId);
        Assert.Equal("1000.00", cash.GetProperty("booked").GetString());
        Assert.Equal("400.00", cash.GetProperty("available").GetString());
        (await _f.Post(Ports.Cash, "/api/v1/holds", $"{{\"customerId\":\"{customerId}\",\"amount\":\"400.01\",\"reference\":\"x-{reference}\"}}")).ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        Assert.Equal("RELEASED", (await _f.Post(Ports.Cash, $"/api/v1/holds/{h.Str("id")}/release")).Str("status"));
        (await _f.Post(Ports.Cash, $"/api/v1/holds/{h.Str("id")}/capture")).ShouldBeProblem(409, "HOLD_CLOSED");
        Assert.Equal("1000.00", (await Flows.Cash(_f, customerId)).GetProperty("available").GetString());
    }

    [Fact]
    public async Task Customers_see_only_their_cash()
    {
        Assert.Equal(200, (await _f.Get(Ports.Cash, $"/api/v1/customers/{SeedIds.CustomerAna}/cash", NordvikFixture.Bearer(await _f.Ana()))).Code);
        (await _f.Get(Ports.Cash, $"/api/v1/customers/{SeedIds.CustomerAna}/cash", NordvikFixture.Bearer(await _f.Lars()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Get(Ports.Cash, $"/api/v1/customers/{SeedIds.CustomerAna}/cash")).ShouldBeProblem(401, "AUTHENTICATION_REQUIRED");
    }
}

[Collection("nordvik")]
public class DepositTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public DepositTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.ResetClock();
    public Task DisposeAsync() => _f.ResetClock();

    [Fact]
    public async Task Deposit_with_ocr_reference_is_credited_and_notified()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString()!;
        var dep = await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{id}\",\"expectedAmount\":\"2500.00\"}}", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Equal(201, dep.Code);
        Assert.Equal("AWAITING_FUNDS", dep.Str("status"));
        var ocr = dep.Str("reference");
        Assert.Matches(@"^7\d{9}$", ocr);
        Assert.Equal(NationalId.LuhnDigit(ocr[..9]), ocr[9] - '0');
        Assert.Equal(IbanMath.Print(IbanMath.ClientMoneyAccount), dep.Str("toIban"));
        await _f.Post(Ports.NordPaySim, "/__admin/incoming-credits", $"{{\"debtorIban\":\"{TestData.Iban("1234")}\",\"amount\":\"2500.00\",\"reference\":\"{ocr}\"}}");
        var done = await Eventually.Until(async () => await _f.Get(Ports.Payments, $"/api/v1/payments/{dep.Str("id")}", NordvikFixture.Bearer(await _f.Olof())), r => r.Str("status") == "RECEIVED", 10000, "ingreso");
        Assert.False(done.Json.GetProperty("amountMismatch").GetBoolean());
        Assert.Equal("2500.00", await Flows.Balance(_f, $"customer:{id}:cash"));
        var mail = await Eventually.Until(() => _f.Get(Ports.Notification, $"/api/v1/mailbox?to={c.GetProperty("email").GetString()}"), r => r.Int("count") >= 1, 8000, "aviso de ingreso");
        Assert.Contains("2500.00 SEK", mail.Json.GetProperty("items")[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Different_amount_is_flagged_and_unknown_references_go_to_suspense()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString()!;
        var dep = await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{id}\",\"expectedAmount\":\"100.00\"}}", NordvikFixture.Bearer(await _f.Olof()));
        await _f.Post(Ports.NordPaySim, "/__admin/incoming-credits", $"{{\"amount\":\"120.00\",\"reference\":\"{dep.Str("reference")}\"}}");
        var done = await Eventually.Until(async () => await _f.Get(Ports.Payments, $"/api/v1/payments/{dep.Str("id")}", NordvikFixture.Bearer(await _f.Olof())), r => r.Str("status") == "RECEIVED", 10000, "ingreso");
        Assert.True(done.Json.GetProperty("amountMismatch").GetBoolean());
        Assert.Equal("120.00", done.Str("amount"));

        var unknown = "9" + Random.Shared.Next(100000000, 999999999);
        await _f.Post(Ports.NordPaySim, "/__admin/incoming-credits", $"{{\"amount\":\"75.50\",\"reference\":\"{unknown}\"}}");
        var olof = await _f.Olof();
        var list = await Eventually.Until(() => _f.Get(Ports.Payments, "/api/v1/unmatched-credits", NordvikFixture.Bearer(olof)),
            r => r.Json.EnumerateArray().Any(u => u.GetProperty("reference").GetString() == unknown), 10000, "abono sin referencia");
        var u = list.Json.EnumerateArray().First(x => x.GetProperty("reference").GetString() == unknown);
        var assigned = await _f.Send(HttpMethod.Post, Ports.Payments, $"/api/v1/unmatched-credits/{u.GetProperty("id").GetString()}/assign", $"{{\"customerId\":\"{id}\"}}", NordvikFixture.Bearer(olof));
        Assert.Equal("ASSIGNED", assigned.Str("status"));
        Assert.Equal("195.50", await Flows.Balance(_f, $"customer:{id}:cash"));
        (await _f.Send(HttpMethod.Post, Ports.Payments, $"/api/v1/unmatched-credits/{u.GetProperty("id").GetString()}/assign", $"{{\"customerId\":\"{id}\"}}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "ALREADY_ASSIGNED");
        (await _f.Get(Ports.Payments, "/api/v1/unmatched-credits", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(403, "ROLE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Deposit_intents_expire_after_seven_days()
    {
        var c = await _f.NewCustomer();
        var dep = await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{c.GetProperty("id").GetString()}\"}}", NordvikFixture.Bearer(await _f.Olof()));
        await _f.Post(Ports.Clock, "/api/v1/clock/travel", "{\"offsetSeconds\":604801}");
        Assert.Equal("EXPIRED", (await _f.Get(Ports.Payments, $"/api/v1/payments/{dep.Str("id")}", NordvikFixture.Bearer(await _f.Olof()))).Str("status"));
    }

    [Fact]
    public async Task Customer_cannot_open_deposits_for_others_and_advisors_cannot_move_money()
    {
        (await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{SeedIds.CustomerLars}\"}}", NordvikFixture.Bearer(await _f.Ana()))).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
        (await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{SeedIds.CustomerAna}\"}}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "SCOPE_MISSING");
        Assert.Equal(201, (await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{SeedIds.CustomerAna}\"}}", NordvikFixture.Bearer(await _f.Ana()))).Code);
    }
}

[Collection("nordvik")]
public class BankAccountAndWithdrawalTests : IAsyncLifetime
{
    private readonly NordvikFixture _f;
    public BankAccountAndWithdrawalTests(NordvikFixture f) => _f = f;
    public Task InitializeAsync() => _f.Freeze($"{TestData.Tuesday}T11:00:00");
    public async Task DisposeAsync()
    {
        await _f.ResetClock();
        var admin = await _f.Admin();
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/payments-gateway/dailyWithdrawalLimit", "{\"value\":\"50000.00\"}", NordvikFixture.Bearer(admin));
    }

    private async Task<Reply> AddAccount(string customerId, string ending, string? token = null) =>
        await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/customers/{customerId}/bank-accounts", $"{{\"iban\":\"{IbanMath.Print(TestData.Iban(ending))}\",\"label\":\"Mi banco\"}}",
            NordvikFixture.Bearer(token ?? await _f.Olof()));

    private async Task<Reply> Withdraw(string customerId, string amount, string bankAccountId, string? token = null, string? key = null) =>
        await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/withdrawals", $"{{\"customerId\":\"{customerId}\",\"amount\":\"{amount}\",\"bankAccountId\":\"{bankAccountId}\"}}",
            Flows.With(token ?? await _f.Olof(), key ?? Flows.Key()));

    [Theory]
    [InlineData("1234", "VERIFIED", "MATCH")]
    [InlineData("0009", "REJECTED", "NO_MATCH")]
    [InlineData("0004", "REJECTED", "ACCOUNT_CLOSED")]
    [InlineData("0008", "NEEDS_REVIEW", "CLOSE_MATCH")]
    public async Task Bank_account_ownership_is_checked_with_the_bank(string ending, string status, string bankResult)
    {
        var c = await _f.NewCustomer();
        var r = await AddAccount(c.GetProperty("id").GetString()!, ending);
        Assert.Equal(201, r.Code);
        Assert.Equal(status, r.Str("status"));
        Assert.Equal(bankResult, r.Str("bankResult"));
        Assert.Matches(@"^SE\d{2} \*{4} \*{4} \*{4} \d{4}$", r.Str("maskedIban"));
        Assert.DoesNotContain("iban\"", r.Body.Replace("maskedIban\"", ""));
    }

    [Fact]
    public async Task Review_invalid_duplicate_and_roles()
    {
        var c = await _f.NewCustomer();
        var id = c.GetProperty("id").GetString()!;
        var review = await AddAccount(id, "0008");
        var olof = await _f.Olof();
        Assert.Equal("VERIFIED", (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/bank-accounts/{review.Str("id")}/review", "{\"decision\":\"APPROVE\",\"comment\":\"DNI comprobado\"}", NordvikFixture.Bearer(olof))).Str("status"));
        (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/bank-accounts/{review.Str("id")}/review", "{\"decision\":\"APPROVE\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "NOT_UNDER_REVIEW");
        (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/customers/{id}/bank-accounts", "{\"iban\":\"SE00 0000 0000 0000 0000 0000\"}", NordvikFixture.Bearer(olof))).ShouldBeProblem(422, "INVALID_IBAN");
        var iban = TestData.Iban("1234");
        Assert.Equal(201, (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/customers/{id}/bank-accounts", $"{{\"iban\":\"{iban}\"}}", NordvikFixture.Bearer(olof))).Code);
        (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/customers/{id}/bank-accounts", $"{{\"iban\":\"{iban}\"}}", NordvikFixture.Bearer(olof))).ShouldBeProblem(409, "BANK_ACCOUNT_ALREADY_ADDED");
        (await _f.Send(HttpMethod.Post, Ports.BankAccounts, $"/api/v1/customers/{id}/bank-accounts", $"{{\"iban\":\"{TestData.Iban("1234")}\"}}", NordvikFixture.Bearer(await _f.Erik()))).ShouldBeProblem(403, "ACCESS_DENIED");
        Assert.Equal(2, (await _f.Get(Ports.BankAccounts, $"/api/v1/customers/{id}/bank-accounts", NordvikFixture.Bearer(await _f.Erik()))).Json.GetArrayLength());
    }

    [Fact]
    public async Task Customers_need_step_up_to_add_an_account()
    {
        var ana = await _f.Ana();
        (await AddAccount(SeedIds.CustomerAna, "1234", ana)).ShouldBeProblem(403, "STEP_UP_REQUIRED");
        var s = await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", "{\"reason\":\"nueva cuenta\"}", NordvikFixture.Bearer(ana));
        var done = await Eventually.Until(() => _f.Get(Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}", NordvikFixture.Bearer(ana)), r => r.Str("status") != "PENDING", 8000, "step-up");
        var high = done.Str("accessToken");
        Assert.Equal("VERIFIED", (await AddAccount(SeedIds.CustomerAna, "1234", high)).Str("status"));
    }

    [Fact]
    public async Task Withdrawal_completes_asynchronously_and_is_idempotent()
    {
        var (id, email, _) = await Flows.FundedCustomer(_f, "3000.00");
        var acc = await AddAccount(id, "1234");
        var key = Flows.Key();
        var w = await Withdraw(id, "1200.00", acc.Str("id"), key: key);
        Assert.Equal(201, w.Code);
        Assert.Equal("PROCESSING", w.Str("status"));
        Assert.Equal("1800.00", (await Flows.Cash(_f, id)).GetProperty("available").GetString());
        var again = await Withdraw(id, "1200.00", acc.Str("id"), key: key);
        Assert.Equal(201, again.Code);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(w.Str("id"), again.Str("id"));
        (await Withdraw(id, "1300.00", acc.Str("id"), key: key)).ShouldBeProblem(422, "IDEMPOTENCY_KEY_REUSED");
        var done = await Eventually.Until(async () => await _f.Get(Ports.Payments, $"/api/v1/payments/{w.Str("id")}", NordvikFixture.Bearer(await _f.Olof())), r => r.Str("status") != "PROCESSING", 10000, "retirada");
        Assert.Equal("COMPLETED", done.Str("status"));
        Assert.Equal("1800.00", await Flows.Balance(_f, $"customer:{id}:cash"));
        var cash = await Flows.Cash(_f, id);
        Assert.Equal("0.00", cash.GetProperty("reserved").GetString());
        var payments = await _f.Get(Ports.Payments, $"/api/v1/customers/{id}/payments?type=WITHDRAWAL", NordvikFixture.Bearer(await _f.Olof()));
        Assert.Single(payments.Json.EnumerateArray());
        var instr = await _f.Get(Ports.Instructions, $"/api/v1/instructions?paymentId={w.Str("id")}");
        Assert.Equal("SETTLED", instr.Json[0].GetProperty("status").GetString());
        Assert.StartsWith("NVK-20261020-PAY-", instr.Json[0].GetProperty("msgId").GetString());
        await Eventually.Until(() => _f.Get(Ports.Notification, $"/api/v1/mailbox?to={email}"), r => r.Body.Contains("1200.00"), 8000, "aviso de retirada");
    }

    [Fact]
    public async Task Bank_rejections_release_the_money_and_are_recorded_as_returns()
    {
        var (id, _, _) = await Flows.FundedCustomer(_f, "500.00");
        var acc = await AddAccount(id, "0006");
        var w = await Withdraw(id, "200.00", acc.Str("id"));
        var done = await Eventually.Until(async () => await _f.Get(Ports.Payments, $"/api/v1/payments/{w.Str("id")}", NordvikFixture.Bearer(await _f.Olof())), r => r.Str("status") != "PROCESSING", 10000, "rechazo");
        Assert.Equal("REJECTED", done.Str("status"));
        Assert.Equal("AC06", done.Str("reason"));
        var cash = await Flows.Cash(_f, id);
        Assert.Equal("500.00", cash.GetProperty("available").GetString());
        Assert.Equal("500.00", cash.GetProperty("booked").GetString());
        var returns = await Eventually.Until(() => _f.Get(Ports.Refunds, $"/api/v1/returns?customerId={id}"), r => r.Json.GetArrayLength() == 1, 8000, "devolución registrada");
        Assert.Equal("Cuenta bloqueada", returns.Json[0].GetProperty("reasonText").GetString());
    }

    [Fact]
    public async Task Withdrawal_rules()
    {
        var (id, _, _) = await Flows.FundedCustomer(_f, "60000.00");
        var acc = await AddAccount(id, "1234");
        var bad = await AddAccount(id, "0009");
        (await _f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/withdrawals", $"{{\"customerId\":\"{id}\",\"amount\":\"10.00\",\"bankAccountId\":\"{acc.Str("id")}\"}}", NordvikFixture.Bearer(await _f.Olof())))
            .ShouldBeProblem(400, "IDEMPOTENCY_KEY_REQUIRED");
        (await Withdraw(id, "10.00", bad.Str("id"))).ShouldBeProblem(422, "BANK_ACCOUNT_NOT_VERIFIED");
        (await Withdraw(id, "10.001", acc.Str("id"))).ShouldBeProblem(400, "VALIDATION_FAILED");
        (await Withdraw(id, "60000.01", acc.Str("id"))).ShouldBeProblem(422, "DAILY_LIMIT_EXCEEDED");
        Assert.Equal(201, (await Withdraw(id, "45000.00", acc.Str("id"))).Code);
        (await Withdraw(id, "5000.01", acc.Str("id"))).ShouldBeProblem(422, "DAILY_LIMIT_EXCEEDED");
        var admin = await _f.Admin();
        await _f.Send(HttpMethod.Put, Ports.Config, "/api/v1/config/payments-gateway/dailyWithdrawalLimit", "{\"value\":\"1000000.00\"}", NordvikFixture.Bearer(admin));
        (await Withdraw(id, "15000.01", acc.Str("id"))).ShouldBeProblem(422, "INSUFFICIENT_FUNDS");
        (await Withdraw(id, "1.00", "ba_no_existe")).ShouldBeProblem(404, "BANK_ACCOUNT_NOT_FOUND");
    }

    [Fact]
    public async Task Customers_need_step_up_above_ten_thousand()
    {
        var ana = await _f.Ana();
        await Flows.Deposit(_f, SeedIds.CustomerAna, "20000.00");
        var acc = await AddAccount(SeedIds.CustomerAna, "1234");
        (await Withdraw(SeedIds.CustomerAna, "10000.01", acc.Str("id"), ana)).ShouldBeProblem(403, "STEP_UP_REQUIRED");
        Assert.Equal(201, (await Withdraw(SeedIds.CustomerAna, "10000.00", acc.Str("id"), ana)).Code);
        var s = await _f.Send(HttpMethod.Post, Ports.StepUp, "/api/v1/step-up", "{}", NordvikFixture.Bearer(ana));
        var high = (await Eventually.Until(() => _f.Get(Ports.StepUp, $"/api/v1/step-up/{s.Str("id")}", NordvikFixture.Bearer(ana)), r => r.Str("status") != "PENDING", 8000, "step-up")).Str("accessToken");
        Assert.Equal(201, (await Withdraw(SeedIds.CustomerAna, "10000.00", acc.Str("id"), high)).Code);
        (await Withdraw(SeedIds.CustomerLars, "1.00", acc.Str("id"), ana)).ShouldBeProblem(404, "CUSTOMER_NOT_FOUND");
    }
}

using System.Text.Json;
using Nordvik.Shared;
using Xunit;

namespace Nordvik.ApiTests;

/// <summary>Pasos de negocio reutilizables (lo mismo que harías con call read('...feature') en Karate).</summary>
public static class Flows
{
    public static Action<HttpRequestMessage> With(string token, string? idempotencyKey = null) => req =>
    {
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        if (idempotencyKey is not null) req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
    };

    public static string Key() => "k-" + Guid.NewGuid().ToString("N");

    /// <summary>Un cliente nuevo de asesor.erik con dinero ya ingresado (ingreso OCR + abono del banco simulado).</summary>
    public static async Task<(string CustomerId, string Email, string DepositId)> FundedCustomer(NordvikFixture f, string amount)
    {
        var c = await f.NewCustomer();
        var id = c.GetProperty("id").GetString()!;
        var dep = await Deposit(f, id, amount);
        return (id, c.GetProperty("email").GetString()!, dep);
    }

    public static async Task<string> Deposit(NordvikFixture f, string customerId, string amount)
    {
        var intent = await f.Send(HttpMethod.Post, Ports.Payments, "/api/v1/deposits", $"{{\"customerId\":\"{customerId}\",\"expectedAmount\":\"{amount}\"}}", NordvikFixture.Bearer(await f.Olof()));
        Assert.True(intent.Code == 201, intent.Body);
        await f.Post(Ports.NordPaySim, "/__admin/incoming-credits", $"{{\"debtorIban\":\"{TestData.Iban("1234")}\",\"debtorName\":\"Cliente\",\"amount\":\"{amount}\",\"reference\":\"{intent.Str("reference")}\"}}");
        var depId = intent.Str("id");
        await Eventually.Until(async () => await f.Get(Ports.Payments, $"/api/v1/payments/{depId}", NordvikFixture.Bearer(await f.Olof())), r => r.Str("status") == "RECEIVED", 10000, "ingreso recibido");
        return depId;
    }

    public static async Task<string> Portfolio(NordvikFixture f, string customerId, string allocation = "[{\"isin\":\"SE0000000003\",\"percent\":100}]")
    {
        var r = await f.Send(HttpMethod.Post, Ports.Portfolio, "/api/v1/portfolios",
            $"{{\"customerId\":\"{customerId}\",\"name\":\"Cartera {Guid.NewGuid().ToString("N")[..6]}\",\"type\":\"ISK\",\"targetAllocation\":{allocation}}}", NordvikFixture.Bearer(await f.Erik()));
        Assert.True(r.Code == 201, r.Body);
        return r.Str("id");
    }

    public static async Task<Reply> Buy(NordvikFixture f, string portfolioId, string isin, string amount, string? token = null, string? key = null) =>
        await f.Send(HttpMethod.Post, Ports.Orders, "/api/v1/orders", $"{{\"portfolioId\":\"{portfolioId}\",\"isin\":\"{isin}\",\"side\":\"BUY\",\"amount\":\"{amount}\"}}",
            With(token ?? await f.Erik(), key ?? Key()));

    public static async Task<Reply> Sell(NordvikFixture f, string portfolioId, string isin, string units, string? token = null, string? key = null) =>
        await f.Send(HttpMethod.Post, Ports.Orders, "/api/v1/orders", $"{{\"portfolioId\":\"{portfolioId}\",\"isin\":\"{isin}\",\"side\":\"SELL\",\"units\":\"{units}\"}}",
            With(token ?? await f.Erik(), key ?? Key()));

    public static async Task<Reply> WaitOrder(NordvikFixture f, string orderId, string status, int timeoutMs = 20000) =>
        await Eventually.Until(async () => await f.Get(Ports.Orders, $"/api/v1/orders/{orderId}", NordvikFixture.Bearer(await f.Erik())),
            r => r.Code != 200 ? throw new Xunit.Sdk.XunitException($"GET orden {orderId} → {r.Code}: {r.Body}") : r.Str("status") == status || r.Str("status") == "REJECTED", timeoutMs, $"orden {orderId} en {status}");

    /// <summary>Envía al custodio lo pendiente, pasa a las 18:05 del día de negociación y espera la ejecución.</summary>
    public static async Task<Reply> ExecuteAt1805(NordvikFixture f, string orderId, string tradeDate)
    {
        await f.Freeze($"{tradeDate}T15:30:00");
        await f.Post(Ports.Router, "/api/v1/batches/run");
        await f.Freeze($"{tradeDate}T18:05:00");
        return await WaitOrder(f, orderId, "EXECUTED");
    }

    public static async Task<JsonElement> Cash(NordvikFixture f, string customerId) =>
        (await f.Get(Ports.Cash, $"/api/v1/internal/customers/{customerId}/cash")).Json;

    public static async Task<string> Balance(NordvikFixture f, string account, string? asOf = null) =>
        (await f.Get(Ports.Ledger, $"/api/v1/accounts/{account}/balance" + (asOf is null ? "" : "?asOf=" + asOf))).Str("balance");
}

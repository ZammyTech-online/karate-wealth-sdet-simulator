using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TransferValuationRequest(string? CustomerId, string? ProviderCode, string? PolicyNumber);
public sealed record CompareRequest(string? Amount);

/// <summary>
/// transfer-valuation: valora una póliza de otra gestora antes de traspasarla. Pide la valoración por provider-network-adapter (con el número
/// nacional sacado de customer-registry: el cliente nunca lo escribe) y la refresca al consultarla: PENDING hasta que la gestora contesta
/// (1–3 días virtuales), luego READY con validez de 30 días. Compara el importe real del traspaso con la valoración: tolerancia del 2 %.
/// </summary>
public sealed class TransferValuation : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "transfer-valuation", "L-pensions", 7109, 3, "Pensiones",
        "Valoración previa de pólizas externas para traspasos: asíncrona (1–3 días virtuales), validez de 30 días y comparación con tolerancia del 2 %.",
        DependsOn: new[] { "provider-network-adapter", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "transfer-valuation.ready" });

    public const decimal TolerancePct = 2m;
    public const int ValidityDays = 30;

    /// <summary>Desviación en % del importe real respecto a la valoración, y si cae dentro de la tolerancia (|desviación| ≤ 2 %).</summary>
    public static (decimal DeviationPct, bool Within) Compare(decimal valued, decimal actual)
    {
        if (valued == 0) return (actual == 0 ? 0 : 100, actual == 0);
        var dev = Math.Round((actual - valued) / valued * 100, 2, MidpointRounding.ToEven);
        return (dev, Math.Abs(dev) <= TolerancePct);
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS valuations (id TEXT PRIMARY KEY, customer_id TEXT, provider TEXT, policy TEXT, external_id TEXT, status TEXT, value TEXT, ready_at TEXT, valued_at TEXT, valid_until TEXT, created_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, IClock clock)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Prov, string Pol, string Ext, string Status, string? Value, string? Ready, string? Valued, string? Until, string At)>(
            "SELECT id, customer_id, provider, policy, external_id, status, value, ready_at, valued_at, valid_until, created_at FROM valuations WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var status = r.Status == "READY" && r.Until is not null && DateTimeOffset.Parse(r.Until) < clock.UtcNow ? "EXPIRED" : r.Status;
        return new { id = r.Id, customerId = r.Cust, providerCode = r.Prov, policyNumber = r.Pol, externalId = r.Ext, status, value = r.Value is null ? null : new { amount = r.Value, currency = "SEK" },
                     readyAt = r.Ready, valuedAt = r.Valued, validUntil = r.Until, createdAt = r.At };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task Refresh(HttpContext http, string id)
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Ext, string Status)>("SELECT external_id, status FROM valuations WHERE id = @id", new { id });
            if (r.Status != "PENDING") return;
            var x = await Downstream.Get(http, "provider-network-adapter", $"/api/v1/internal/valuations/{r.Ext}", forwardAuth: false);
            if (!x.Ok || x.Str("status") != "READY") return;
            var valuedAt = DateTimeOffset.Parse(x.Str("valuedAt")!);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE valuations SET status = 'READY', value = @v, valued_at = @va, valid_until = @vu WHERE id = @id AND status = 'PENDING'",
                new { id, v = x.Str("value"), va = valuedAt.ToString("O"), vu = valuedAt.AddDays(ValidityDays).ToString("O") }, tx);
            Outbox.Add(db, http, clock, "transfer-valuation.ready", id, new { valuationId = id, value = x.Str("value") }, tx);
            tx.Commit();
        }

        app.MapPost("/api/v1/transfer-valuations", async (HttpContext http, TransferValuationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (b.CustomerId is null || string.IsNullOrWhiteSpace(b.ProviderCode) || string.IsNullOrWhiteSpace(b.PolicyNumber))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId, providerCode y policyNumber son obligatorios.", "Ejemplo: { \"customerId\": \"...\", \"providerCode\": \"FJORDLIV\", \"policyNumber\": \"P123456\" }");
            if (await CustomerGuard.Check(http, p, "portfolio:update", b.CustomerId) is { } g) return g;
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            var ext = await Downstream.Send(http, "provider-network-adapter", HttpMethod.Post, "/api/v1/internal/valuations",
                new { providerCode = b.ProviderCode, policyNumber = b.PolicyNumber, personalNumber = cust.Str("nationalId") }, forwardAuth: false);
            if (!ext.Ok) return Results.Content(ext.Raw, "application/problem+json", System.Text.Encoding.UTF8, ext.Status);
            var id = "tv_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO valuations VALUES (@id, @CustomerId, @prov, @PolicyNumber, @ext, 'PENDING', NULL, @ready, NULL, NULL, @at)",
                new { id, b.CustomerId, prov = b.ProviderCode.ToUpperInvariant(), b.PolicyNumber, ext = ext.Str("valuationId"), ready = ext.Str("readyAt"), at = Nordic.Iso(clock.LocalNow) });
            await Refresh(http, id);
            return Results.Created($"/api/v1/transfer-valuations/{id}", Load(db, id, clock));
        }).WithTags("Valoraciones").WithSummary("Pedir la valoración de una póliza externa (asíncrona)");

        app.MapGet("/api/v1/transfer-valuations/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            string? owner;
            using (var db0 = ctx.OpenDb()) owner = db0.ExecuteScalar<string?>("SELECT customer_id FROM valuations WHERE id = @id", new { id });
            if (owner is null) return Problems.NotFound(http, "VALUATION_NOT_FOUND", $"No existe la valoración {id}.", null);
            if (await CustomerGuard.Check(http, p, "portfolio:read", owner) is { } g) return g;
            await Refresh(http, id);
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id, clock));
        }).WithTags("Valoraciones").WithSummary("Estado de la valoración (PENDING → READY; EXPIRED a los 30 días)");

        app.MapGet("/api/v1/internal/transfer-valuations/{id}", async (HttpContext http, string id) =>
        {
            await Refresh(http, id);
            using var db = ctx.OpenDb();
            return Load(db, id, clock) is { } v ? Results.Ok(v) : Problems.NotFound(http, "VALUATION_NOT_FOUND", $"No existe la valoración {id}.", null);
        }).WithTags("Interno").WithSummary("(interno) Una valoración, refrescada");

        app.MapPost("/api/v1/internal/transfer-valuations/{id}/compare", (HttpContext http, string id, CompareRequest b) =>
        {
            using var db = ctx.OpenDb();
            var v = db.QuerySingleOrDefault<string?>("SELECT value FROM valuations WHERE id = @id AND status = 'READY'", new { id });
            if (v is null) return Problems.Conflict(http, "VALUATION_NOT_READY", "La valoración no está lista.", null);
            if (!Money.TryParse(b.Amount, out var amount)) return Problems.BadRequest(http, "VALIDATION_FAILED", "amount es un importe como texto.", null);
            var (dev, within) = Compare(decimal.Parse(v, Money.Inv), amount);
            return Results.Ok(new { valuationId = id, valued = v, actual = Money.Format(amount, 2), deviationPct = Money.Format(dev, 2), tolerancePct = Money.Format(TolerancePct, 2), withinTolerance = within });
        }).WithTags("Interno").WithSummary("(interno) Comparar el importe real con la valoración (±2 %)");
    }
}

using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TransferRequest(string? CustomerId, string? TargetPlanId, string? ProviderCode, string? PolicyNumber);
public sealed record TransferDecision(string? Reason);

/// <summary>
/// transfer-service: traspaso de una póliza de otra gestora a un plan Nordvik. Saga larga con estados:
/// VALUATION_PENDING → VALUED → (cliente confirma) AWAITING_APPROVAL → (Operaciones aprueba) se pide el dinero a la gestora →
/// importe dentro del 2 % → COMPLETED (aportación TRANSFER al plan) · fuera del 2 % → MANUAL_REVIEW (accept-amount o reject).
/// Cancelable por el cliente hasta la aprobación. Cualquier paso fuera de orden: 409 INVALID_STATE_TRANSITION. Cada paso queda en history.
/// </summary>
public sealed class TransferService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "transfer-service", "L-pensions", 7108, 3, "Pensiones",
        "Traspasos entrantes desde otras gestoras: saga con valoración, confirmación del cliente, aprobación de Operaciones, tolerancia del 2 %, revisión manual e historial.",
        DependsOn: new[] { "transfer-valuation", "provider-network-adapter", "pension-plan-service", "contribution-service", "permission-service", "clock-service" },
        Emits: new[] { "transfer.requested", "transfer.status-changed", "transfer.completed" });

    public static readonly Dictionary<string, string[]> Next = new()
    {
        ["VALUATION_PENDING"] = new[] { "VALUED", "CANCELLED" },
        ["VALUED"] = new[] { "AWAITING_APPROVAL", "CANCELLED", "EXPIRED" },
        ["AWAITING_APPROVAL"] = new[] { "COMPLETED", "MANUAL_REVIEW", "REJECTED", "CANCELLED", "EXPIRED" },
        ["MANUAL_REVIEW"] = new[] { "COMPLETED", "REJECTED" },
    };

    public static bool CanMove(string from, string to) => Next.TryGetValue(from, out var n) && n.Contains(to);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS transfers (id TEXT PRIMARY KEY, customer_id TEXT, plan_id TEXT, provider TEXT, policy TEXT, valuation_id TEXT, status TEXT, valued TEXT, received TEXT,
                        deviation TEXT, contribution_id TEXT, history TEXT, created_at TEXT, updated_at TEXT)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Plan, string Prov, string Pol, string Val, string Status, string? Valued, string? Rec, string? Dev, string? Ctb, string Hist, string At, string Up)>(
            "SELECT id, customer_id, plan_id, provider, policy, valuation_id, status, valued, received, deviation, contribution_id, history, created_at, updated_at FROM transfers WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, targetPlanId = r.Plan, providerCode = r.Prov, policyNumber = r.Pol, valuationId = r.Val, status = r.Status,
            valuedAmount = r.Valued, receivedAmount = r.Rec, deviationPct = r.Dev, contributionId = r.Ctb, history = JsonDocument.Parse(r.Hist).RootElement, createdAt = r.At, updatedAt = r.Up,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] ops = { "Operations", "Compliance", "Admin" };

        IResult? Move(HttpContext http, Microsoft.Data.Sqlite.SqliteConnection db, string id, string to, string by, string? note, object? extra = null)
        {
            var (cur, hist) = db.QuerySingle<(string, string)>("SELECT status, history FROM transfers WHERE id = @id", new { id });
            if (cur == to) return null;
            if (!CanMove(cur, to)) return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Un traspaso {cur} no puede pasar a {to}.", $"Desde {cur} se puede ir a: {string.Join(", ", Next.GetValueOrDefault(cur) ?? Array.Empty<string>())}.");
            var h = JsonSerializer.Deserialize<List<JsonElement>>(hist)!;
            h.Add(JsonSerializer.SerializeToElement(new { from = cur, to, by, note, at = Nordic.Iso(clock.LocalNow) }, Canonical.Web));
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE transfers SET status = @to, history = @h, updated_at = @at WHERE id = @id", new { id, to, h = JsonSerializer.Serialize(h), at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, to == "COMPLETED" ? "transfer.completed" : "transfer.status-changed", id, new { transferId = id, from = cur, to, extra }, tx);
            tx.Commit();
            return null;
        }

        async Task<(IResult? Fail, string Status)> Sync(HttpContext http, string id)
        {
            using var db = ctx.OpenDb();
            var (status, val) = db.QuerySingle<(string, string)>("SELECT status, valuation_id FROM transfers WHERE id = @id", new { id });
            if (status is not ("VALUATION_PENDING" or "VALUED" or "AWAITING_APPROVAL")) return (null, status);
            var v = await Downstream.Get(http, "transfer-valuation", $"/api/v1/internal/transfer-valuations/{val}", forwardAuth: false);
            var vs = v.Str("status");
            if (status == "VALUATION_PENDING" && vs is "READY" or "EXPIRED")
            {
                db.Execute("UPDATE transfers SET valued = @a WHERE id = @id", new { id, a = v.Json.GetProperty("value").GetProperty("amount").GetString() });
                Move(http, db, id, "VALUED", "system", "La gestora envió la valoración.");
                status = "VALUED";
            }
            if (vs == "EXPIRED" && status is "VALUED" or "AWAITING_APPROVAL") { Move(http, db, id, "EXPIRED", "system", "La valoración caducó (30 días)."); status = "EXPIRED"; }
            return (null, status);
        }

        async Task<(IResult? Fail, string CustomerId)> Owner(HttpContext http, Principal p, string id, string action)
        {
            using var db = ctx.OpenDb();
            var c = db.ExecuteScalar<string?>("SELECT customer_id FROM transfers WHERE id = @id", new { id });
            if (c is null) return (Problems.NotFound(http, "TRANSFER_NOT_FOUND", $"No existe el traspaso {id}.", null), "");
            return (await CustomerGuard.Check(http, p, action, c), c);
        }

        app.MapPost("/api/v1/transfers", async (HttpContext http, TransferRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (b.CustomerId is null || b.TargetPlanId is null || b.ProviderCode is null || b.PolicyNumber is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "customerId, targetPlanId, providerCode y policyNumber son obligatorios.", null);
            if (await CustomerGuard.Check(http, p, "portfolio:update", b.CustomerId) is { } g) return g;
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{b.TargetPlanId}", forwardAuth: false);
            if (!plan.Ok || plan.Str("customerId") != b.CustomerId) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"El cliente no tiene el plan {b.TargetPlanId}.", null);
            if (plan.Str("status") != "ACTIVE") return Problems.Conflict(http, "PLAN_NOT_ACTIVE", $"El plan destino está {plan.Str("status")}.", null);
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM transfers WHERE provider = @p AND policy = @PolicyNumber AND status NOT IN ('CANCELLED','REJECTED','EXPIRED')", new { p = b.ProviderCode.ToUpperInvariant(), b.PolicyNumber }) is { } open)
                    return Problems.Conflict(http, "TRANSFER_ALREADY_IN_PROGRESS", $"Esa póliza ya tiene un traspaso en curso: {open}.", null);
            var val = await Downstream.Send(http, "transfer-valuation", HttpMethod.Post, "/api/v1/transfer-valuations", new { customerId = b.CustomerId, providerCode = b.ProviderCode, policyNumber = b.PolicyNumber });
            if (!val.Ok) return Results.Content(val.Raw, "application/problem+json", System.Text.Encoding.UTF8, val.Status);
            var id = "tr_" + Guid.NewGuid().ToString("N")[..12];
            var hist = JsonSerializer.Serialize(new[] { new { from = (string?)null, to = "VALUATION_PENDING", by = p.Sub, note = "Traspaso solicitado.", at = Nordic.Iso(clock.LocalNow) } }, Canonical.Web);
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO transfers VALUES (@id, @CustomerId, @TargetPlanId, @prov, @PolicyNumber, @v, 'VALUATION_PENDING', NULL, NULL, NULL, NULL, @hist, @at, @at)",
                    new { id, b.CustomerId, b.TargetPlanId, prov = b.ProviderCode.ToUpperInvariant(), b.PolicyNumber, v = val.Str("id"), hist, at = Nordic.Iso(clock.LocalNow) }, tx);
                Outbox.Add(db, http, clock, "transfer.requested", id, new { transferId = id, customerId = b.CustomerId, planId = b.TargetPlanId, providerCode = b.ProviderCode }, tx);
                tx.Commit();
            }
            await Sync(http, id);
            return Results.Created($"/api/v1/transfers/{id}", Load(db, id));
        }).WithTags("Traspasos").WithSummary("Solicitar un traspaso entrante (empieza pidiendo la valoración)");

        app.MapGet("/api/v1/transfers/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if ((await Owner(http, p, id, "portfolio:read")).Fail is { } g) return g;
            await Sync(http, id);
            using var db = ctx.OpenDb();
            return Results.Ok(Load(db, id));
        }).WithTags("Traspasos").WithSummary("Estado del traspaso (se sincroniza con la valoración)");

        app.MapGet("/api/v1/customers/{customerId}/transfers", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "portfolio:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM transfers WHERE customer_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Traspasos").WithSummary("Traspasos de un cliente");

        app.MapPost("/api/v1/transfers/{id}/confirm", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if ((await Owner(http, p, id, "portfolio:update")).Fail is { } g) return g;
            await Sync(http, id);
            using var db = ctx.OpenDb();
            if (Move(http, db, id, "AWAITING_APPROVAL", p.Sub, "El cliente acepta la valoración.") is { } bad) return bad;
            return Results.Ok(Load(db, id));
        }).WithTags("Traspasos").WithSummary("El cliente confirma la valoración (VALUED → AWAITING_APPROVAL)");

        app.MapPost("/api/v1/transfers/{id}/cancel", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if ((await Owner(http, p, id, "portfolio:update")).Fail is { } g) return g;
            await Sync(http, id);
            using var db = ctx.OpenDb();
            if (Move(http, db, id, "CANCELLED", p.Sub, "Cancelado.") is { } bad) return bad;
            return Results.Ok(Load(db, id));
        }).WithTags("Traspasos").WithSummary("Cancelar (solo antes de la aprobación)");

        async Task<IResult> Complete(HttpContext http, Microsoft.Data.Sqlite.SqliteConnection db, string id, string amount, string by, string note)
        {
            var planId = db.ExecuteScalar<string>("SELECT plan_id FROM transfers WHERE id = @id", new { id });
            var c = await Downstream.Send(http, "contribution-service", HttpMethod.Post, $"/api/v1/pension-plans/{planId}/contributions",
                new { amount, source = "TRANSFER", period = clock.LocalNow.ToString("yyyy-MM"), reference = id }, forwardAuth: false, headers: await ServiceToken.Header(clock));
            if (!c.Ok && c.Str("code") != "DUPLICATE_CONTRIBUTION") return Problems.Of(http, 502, "CONTRIBUTION_FAILED", "No se pudo abonar", $"contribution-service respondió {c.Status}: {c.Raw}", null);
            db.Execute("UPDATE transfers SET contribution_id = @c WHERE id = @id", new { id, c = c.Str("id") });
            if (Move(http, db, id, "COMPLETED", by, note, new { amount }) is { } bad) return bad;
            return Results.Ok(Load(db, id));
        }

        app.MapPost("/api/v1/transfers/{id}/approve", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: ops) is { } denied) return denied;
            string? cur;
            using (var db0 = ctx.OpenDb()) cur = db0.ExecuteScalar<string?>("SELECT status FROM transfers WHERE id = @id", new { id });
            if (cur is null) return Problems.NotFound(http, "TRANSFER_NOT_FOUND", $"No existe el traspaso {id}.", null);
            var (_, status) = await Sync(http, id);
            if (status == "EXPIRED") return Problems.Conflict(http, "VALUATION_EXPIRED", "La valoración caducó: hay que pedir un traspaso nuevo.", null);
            if (status != "AWAITING_APPROVAL") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Solo se aprueba un traspaso AWAITING_APPROVAL (está {status}).", null);
            using var db = ctx.OpenDb();
            var val = db.ExecuteScalar<string>("SELECT valuation_id FROM transfers WHERE id = @id", new { id });
            var ext = (await Downstream.Get(http, "transfer-valuation", $"/api/v1/internal/transfer-valuations/{val}", forwardAuth: false)).Str("externalId");
            var money = await Downstream.Send(http, "provider-network-adapter", HttpMethod.Post, "/api/v1/internal/transfers", new { externalId = ext }, forwardAuth: false);
            if (!money.Ok) return Results.Content(money.Raw, "application/problem+json", System.Text.Encoding.UTF8, money.Status);
            var amount = money.Str("amount")!;
            var cmp = await Downstream.Send(http, "transfer-valuation", HttpMethod.Post, $"/api/v1/internal/transfer-valuations/{val}/compare", new { amount }, forwardAuth: false);
            db.Execute("UPDATE transfers SET received = @amount, deviation = @d WHERE id = @id", new { id, amount, d = cmp.Str("deviationPct") });
            if (!cmp.Json.GetProperty("withinTolerance").GetBoolean())
            {
                Move(http, db, id, "MANUAL_REVIEW", p.Sub, $"Llegaron {amount} SEK: desviación {cmp.Str("deviationPct")} % (tolerancia ±2 %).");
                return Results.Ok(Load(db, id));
            }
            return await Complete(http, db, id, amount, p.Sub, "Aprobado; importe dentro de la tolerancia.");
        }).WithTags("Revisión").WithSummary("Aprobar (Operaciones): pide el dinero; ±2 % → COMPLETED, si no → MANUAL_REVIEW");

        app.MapPost("/api/v1/transfers/{id}/accept-amount", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: ops) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Status, string? Rec)>("SELECT status, received FROM transfers WHERE id = @id", new { id });
            if (r.Status is null) return Problems.NotFound(http, "TRANSFER_NOT_FOUND", $"No existe el traspaso {id}.", null);
            if (r.Status != "MANUAL_REVIEW") return Problems.Conflict(http, "INVALID_STATE_TRANSITION", $"Solo se acepta el importe en MANUAL_REVIEW (está {r.Status}).", null);
            return await Complete(http, db, id, r.Rec!, p.Sub, "Importe aceptado tras revisión manual.");
        }).WithTags("Revisión").WithSummary("Aceptar el importe recibido fuera de tolerancia (MANUAL_REVIEW → COMPLETED)");

        app.MapPost("/api/v1/transfers/{id}/reject", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: ops) is { } denied) return denied;
            string reason;
            using (var reader = new StreamReader(http.Request.Body)) { var raw = await reader.ReadToEndAsync(); reason = raw.Length > 0 && JsonDocument.Parse(raw).RootElement.TryGetProperty("reason", out var x) ? x.GetString() ?? "" : ""; }
            if (string.IsNullOrWhiteSpace(reason)) return Problems.BadRequest(http, "VALIDATION_FAILED", "reason es obligatorio para rechazar.", null);
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<string?>("SELECT status FROM transfers WHERE id = @id", new { id }) is null) return Problems.NotFound(http, "TRANSFER_NOT_FOUND", $"No existe el traspaso {id}.", null);
            if (Move(http, db, id, "REJECTED", p.Sub, reason) is { } bad) return bad;
            return Results.Ok(Load(db, id));
        }).WithTags("Revisión").WithSummary("Rechazar con motivo (AWAITING_APPROVAL o MANUAL_REVIEW → REJECTED)");
    }
}

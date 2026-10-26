using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RecommendationRequest(string? Isin, string? Amount, string? Rationale);
public sealed record AcceptRequest(string? PortfolioId);

/// <summary>
/// advisor-desk: el expediente de asesoramiento. Recoge los datos confirmados en las llamadas (con su origen), el último test de idoneidad
/// y las recomendaciones. Una recomendación solo nace si suitability-service dice SUITABLE; el cliente la acepta (y puede lanzar la orden,
/// con Idempotency-Key derivada de la recomendación) o la rechaza.
/// </summary>
public sealed class AdvisorDesk : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "advisor-desk", "G-advice", 6603, 3, "Asesoramiento y comunicación",
        "Expediente de asesoramiento: datos confirmados en llamadas (con evidencia), idoneidad y recomendaciones que solo nacen si son idóneas; aceptación del cliente con orden idempotente.",
        DependsOn: new[] { "suitability-service", "insight-extraction", "voice-service", "order-management", "permission-service", "clock-service" },
        Emits: new[] { "recommendation.proposed", "recommendation.accepted", "recommendation.declined" }, Consumes: new[] { "voice.review.completed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS facts (customer_id TEXT, field TEXT, value TEXT, call_id TEXT, turn INTEGER, quote TEXT, confirmed_at TEXT, PRIMARY KEY (customer_id, field, call_id));
                     CREATE TABLE IF NOT EXISTS recommendations (id TEXT PRIMARY KEY, customer_id TEXT, isin TEXT, amount TEXT, rationale TEXT, status TEXT, suitability TEXT, proposed_by TEXT, proposed_at TEXT,
                        decided_at TEXT, order_id TEXT)");
    }

    private static object? Rec(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Isin, string Amount, string? Why, string Status, string Suit, string By, string At, string? Dec, string? Order)>(
            "SELECT id, customer_id, isin, amount, rationale, status, suitability, proposed_by, proposed_at, decided_at, order_id FROM recommendations WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, customerId = r.Cust, isin = r.Isin, fund = FundSeed.All.First(f => f.Isin == r.Isin).Name, amount = r.Amount, rationale = r.Why, status = r.Status,
            suitability = JsonDocument.Parse(r.Suit).RootElement, proposedBy = r.By, proposedAt = r.At, decidedAt = r.Dec, orderId = r.Order,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        EventConsumer.Start(app, ctx, Info.Consumes!, async ev =>
        {
            var callId = ev.Payload.GetProperty("callId").GetString()!;
            var svc = await ServiceToken.Header(clock);
            var call = await Downstream.Send(null, "voice-service", HttpMethod.Get, $"/api/v2/calls/{callId}", null, forwardAuth: false, headers: svc);
            if (!call.Ok) return;
            var customerId = call.Str("customerId")!;
            var ins = await Downstream.Get(null, "insight-extraction", $"/api/v1/insights/{callId}", forwardAuth: false);
            using var db = ctx.OpenDb();
            foreach (var i in ins.Json.GetProperty("items").EnumerateArray())
            {
                var status = i.GetProperty("status").GetString();
                var review = i.GetProperty("needsHumanReview").GetBoolean();
                if (status == "REJECTED" || (review && status != "CONFIRMED") || i.GetProperty("field").GetString() == "orderIntent") continue;
                db.Execute("INSERT OR REPLACE INTO facts VALUES (@customerId, @f, @v, @callId, @t, @q, @at)",
                    new { customerId, f = i.GetProperty("field").GetString(), v = i.GetProperty("value").GetRawText(), callId, t = i.GetProperty("evidence").GetProperty("turn").GetInt32(),
                          q = i.GetProperty("evidence").GetProperty("quote").GetString(), at = Nordic.Iso(clock.LocalNow) });
            }
        });

        app.MapGet("/api/v1/customers/{customerId}/advisory-file", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var facts = db.Query<(string Field, string Value, string Call, long Turn, string Quote, string At)>("SELECT field, value, call_id, turn, quote, confirmed_at FROM facts WHERE customer_id = @customerId ORDER BY confirmed_at, field", new { customerId })
                .Select(f => new { field = f.Field, value = JsonDocument.Parse(f.Value).RootElement, source = new { type = "VOICE_CALL", callId = f.Call, turn = f.Turn, quote = f.Quote }, confirmedAt = f.At }).ToList();
            var suit = await Downstream.Get(http, "suitability-service", $"/api/v1/customers/{customerId}/assessments/latest");
            var recs = db.Query<string>("SELECT id FROM recommendations WHERE customer_id = @customerId ORDER BY proposed_at", new { customerId }).Select(i => Rec(db, i)).ToList();
            return Results.Ok(new { customerId, facts, suitability = suit.Ok ? suit.Body : null, recommendations = recs, gaps = suit.Ok ? Array.Empty<string>() : new[] { "SUITABILITY_ASSESSMENT" } });
        }).WithTags("Expediente").WithSummary("Expediente de asesoramiento: hechos de las llamadas, idoneidad y recomendaciones");

        app.MapPost("/api/v1/customers/{customerId}/recommendations", async (HttpContext http, string customerId, RecommendationRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor" }) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            if (string.IsNullOrWhiteSpace(b.Isin) || !Money.TryParse(b.Amount, out var amount) || amount <= 0 || string.IsNullOrWhiteSpace(b.Rationale))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "isin, amount (texto) y rationale son obligatorios.", "Ejemplo: { \"isin\": \"SE0000000201\", \"amount\": \"10000.00\", \"rationale\": \"Perfil 4, horizonte largo\" }");
            var s = await Downstream.Post(http, "suitability-service", "/api/v1/suitability-checks", new { customerId, isin = b.Isin });
            if (!s.Ok) return Results.Content(s.Raw, "application/problem+json", System.Text.Encoding.UTF8, s.Status);
            if (s.Str("result") != "SUITABLE")
                return Problems.Unprocessable(http, "RECOMMENDATION_NOT_SUITABLE", "No se puede recomendar un producto que no es idóneo: " + string.Join(" ", s.Json.GetProperty("reasons").EnumerateArray().Select(r => r.GetString())), null);
            var id = "rec_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO recommendations VALUES (@id, @customerId, @Isin, @amount, @Rationale, 'PROPOSED', @suit, @by, @at, NULL, NULL)",
                new { id, customerId, b.Isin, amount = Money.Format(amount, 2), b.Rationale, suit = s.Raw, by = p.Sub, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "recommendation.proposed", id, new { recommendationId = id, customerId, isin = b.Isin }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/recommendations/{id}", Rec(db, id));
        }).WithTags("Recomendaciones").WithSummary("Recomendar un fondo (solo si es idóneo para el cliente)");

        app.MapGet("/api/v1/customers/{customerId}/recommendations", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM recommendations WHERE customer_id = @customerId ORDER BY proposed_at", new { customerId }).Select(i => Rec(db, i)).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Recomendaciones").WithSummary("Recomendaciones de un cliente (el cliente ve las suyas)");

        app.MapPost("/api/v1/recommendations/{id}/accept", async (HttpContext http, string id) =>
        {
            AcceptRequest? b = null;
            if (http.Request.ContentLength is > 0) try { b = await JsonSerializer.DeserializeAsync<AcceptRequest>(http.Request.Body, Canonical.Web); } catch (JsonException) { }
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Cust, string Isin, string Amount, string Status)>("SELECT id, customer_id, isin, amount, status FROM recommendations WHERE id = @id", new { id });
            if (r.Id is null || r.Cust != p.CustomerId) return Problems.NotFound(http, "RECOMMENDATION_NOT_FOUND", $"No existe la recomendación {id}.", null);
            if (r.Status != "PROPOSED") return Problems.Conflict(http, "RECOMMENDATION_CLOSED", $"La recomendación está {r.Status}.", null);
            string? orderId = null;
            if (b?.PortfolioId is not null)
            {
                var o = await Downstream.Send(http, "order-management", HttpMethod.Post, "/api/v1/orders", new { portfolioId = b.PortfolioId, isin = r.Isin, side = "BUY", amount = r.Amount },
                    headers: new Dictionary<string, string> { ["Idempotency-Key"] = "rec-" + id });
                if (!o.Ok) return Results.Content(o.Raw, "application/problem+json", System.Text.Encoding.UTF8, o.Status);
                orderId = o.Str("id");
            }
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE recommendations SET status = 'ACCEPTED', decided_at = @at, order_id = @orderId WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), orderId, id }, tx);
            Outbox.Add(db, http, clock, "recommendation.accepted", id, new { recommendationId = id, customerId = r.Cust, orderId }, tx);
            tx.Commit();
            return Results.Ok(Rec(db, id));
        }).WithTags("Recomendaciones").WithSummary("El cliente acepta (con portfolioId, además se lanza la orden con Idempotency-Key rec-{id})");

        app.MapPost("/api/v1/recommendations/{id}/decline", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Cust, string Status)>("SELECT id, customer_id, status FROM recommendations WHERE id = @id", new { id });
            if (r.Id is null || r.Cust != p.CustomerId) return Problems.NotFound(http, "RECOMMENDATION_NOT_FOUND", $"No existe la recomendación {id}.", null);
            if (r.Status != "PROPOSED") return Problems.Conflict(http, "RECOMMENDATION_CLOSED", $"La recomendación está {r.Status}.", null);
            db.Execute("UPDATE recommendations SET status = 'DECLINED', decided_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id });
            Outbox.Add(db, http, clock, "recommendation.declined", id, new { recommendationId = id, customerId = r.Cust });
            return Results.Ok(Rec(db, id));
        }).WithTags("Recomendaciones").WithSummary("El cliente rechaza la recomendación");
    }
}

using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ContribRequest(string? Amount, string? Source, string? Period, string? Reference);

/// <summary>
/// contribution-service: cada aportación a un plan. El dinero entra en la caja del cliente (asiento en ledger con referencia única)
/// y se invierte en el momento según la asignación de la cartera del plan: una orden de compra por fondo, con Idempotency-Key derivada
/// de la aportación (repetir la llamada no compra dos veces). Las líneas de menos de 100 SEK se suman a la más grande (mínimo por orden).
/// La misma aportación (plan, origen, periodo y referencia) no entra dos veces: 409.
/// </summary>
public sealed class ContributionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "contribution-service", "L-pensions", 7101, 3, "Pensiones",
        "Aportaciones a planes de pensión (privadas, de empresa, intercambio salarial, traspasos): asiento en ledger e inversión inmediata según la asignación, sin duplicados.",
        DependsOn: new[] { "pension-plan-service", "portfolio-service", "ledger-service", "order-management", "permission-service", "clock-service" },
        Emits: new[] { "contribution.received" });

    public static readonly string[] Sources = { "PRIVATE", "EMPLOYER", "SALARY_EXCHANGE", "TRANSFER" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS contributions (id TEXT PRIMARY KEY, plan_id TEXT, customer_id TEXT, amount TEXT, source TEXT, period TEXT, reference TEXT, status TEXT, ledger_entry TEXT,
                        orders TEXT, created_at TEXT, created_by TEXT, UNIQUE (plan_id, source, period, reference))");
    }

    /// <summary>Reparte un importe según porcentajes, al céntimo, y junta las líneas por debajo del mínimo en la mayor.</summary>
    public static List<(string Isin, decimal Amount)> Split(decimal amount, IEnumerable<(string Isin, decimal Pct)> alloc, decimal minimum = 100m)
    {
        var lines = alloc.Select(a => (a.Isin, Amount: Math.Round(amount * a.Pct / 100, 2, MidpointRounding.ToEven))).ToList();
        var diff = amount - lines.Sum(l => l.Amount);
        var biggest = lines.IndexOf(lines.OrderByDescending(l => l.Amount).First());
        lines[biggest] = (lines[biggest].Isin, lines[biggest].Amount + diff);
        var small = lines.Where(l => l.Amount < minimum).ToList();
        if (small.Count > 0 && small.Count < lines.Count)
        {
            var keep = lines.Where(l => l.Amount >= minimum).ToList();
            var top = keep.IndexOf(keep.OrderByDescending(l => l.Amount).First());
            keep[top] = (keep[top].Isin, keep[top].Amount + small.Sum(s => s.Amount));
            lines = keep;
        }
        return lines;
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Plan, string Cust, string Amount, string Source, string Period, string? Ref, string Status, string? Ledger, string? Orders, string At, string By)>(
            "SELECT id, plan_id, customer_id, amount, source, period, reference, status, ledger_entry, orders, created_at, created_by FROM contributions WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, planId = r.Plan, amount = new { amount = r.Amount, currency = "SEK" }, source = r.Source, period = r.Period, reference = r.Ref, status = r.Status,
            ledgerEntryId = r.Ledger, orders = r.Orders is null ? (JsonElement?)null : JsonDocument.Parse(r.Orders).RootElement, createdAt = r.At, createdBy = r.By,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/pension-plans/{planId}/contributions", async (HttpContext http, string planId, ContribRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Customer", "Operations", "Service" }) is { } denied) return denied;
            var source = (b.Source ?? (p.Role == "Customer" ? "PRIVATE" : null))?.ToUpperInvariant();
            var errors = new List<FieldError>();
            if (!Money.TryParse(b.Amount, out var amount) || amount < 100 || Math.Round(amount, 2) != amount) errors.Add(new FieldError("amount", "mínimo 100.00 SEK, como texto con 2 decimales"));
            if (source is null || !Sources.Contains(source)) errors.Add(new FieldError("source", string.Join(", ", Sources)));
            if (b.Period is null || !System.Text.RegularExpressions.Regex.IsMatch(b.Period, @"^\d{4}-(0[1-9]|1[0-2])$")) errors.Add(new FieldError("period", "AAAA-MM"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Aportación no válida.", "Ejemplo: { \"amount\": \"1500.00\", \"source\": \"PRIVATE\", \"period\": \"2026-10\", \"reference\": \"OCT\" }", errors);
            if (p.Role == "Customer" && source != "PRIVATE") return Problems.Of(http, 403, "SOURCE_NOT_ALLOWED", "Prohibido", "Un cliente solo hace aportaciones PRIVATE.", null);
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{planId}", forwardAuth: false);
            if (plan.Status == 404) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {planId}.", null);
            var customerId = plan.Str("customerId")!;
            var d = await Permissions.Check(http, p, "payment:create", customerId);
            if (!d.Allowed && p.Role != "Service") return Permissions.Denied(http, p, d, "PLAN_NOT_FOUND", $"el plan {planId}");
            if (plan.Str("status") != "ACTIVE") return Problems.Conflict(http, plan.Str("status") == "FROZEN" ? "PLAN_FROZEN" : "PLAN_NOT_ACTIVE", $"El plan está {plan.Str("status")}: no admite aportaciones.", null);
            if (source is "EMPLOYER" or "SALARY_EXCHANGE" && plan.Str("type") == "PRIVATE") return Problems.Unprocessable(http, "WRONG_PLAN_TYPE", "Las aportaciones de empresa van a planes de empresa.", null);
            var reference = b.Reference ?? "-";
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM contributions WHERE plan_id = @planId AND source = @source AND period = @Period AND reference = @reference", new { planId, source, b.Period, reference }) is { } dup)
                    return Problems.Conflict(http, "DUPLICATE_CONTRIBUTION", $"Esa aportación ya está registrada: {dup}.", "Mismo plan, origen, periodo y referencia: no se cobra dos veces.");
            var id = "ctb_" + Guid.NewGuid().ToString("N")[..14];
            var entry = await Downstream.Post(http, "ledger-service", "/api/v1/journal-entries", new
            {
                reference = "CONTRIB-" + id, description = $"Aportación {source} {b.Period} al plan {planId}",
                lines = new object[] { new { account = "bank:client-money", debit = Money.Format(amount, 2) }, new { account = $"customer:{customerId}:cash", credit = Money.Format(amount, 2) } },
            }, forwardAuth: false);
            if (!entry.Ok) return Problems.Of(http, 502, "LEDGER_ERROR", "Error contable", "ledger-service respondió " + entry.Status + ": " + entry.Raw, null);
            var pf = await Downstream.Get(http, "portfolio-service", $"/api/v1/internal/portfolios/{plan.Str("portfolioId")}", forwardAuth: false);
            var alloc = pf.Json.GetProperty("targetAllocation").EnumerateArray().Select(a => (a.GetProperty("isin").GetString()!, a.GetProperty("percent").GetDecimal()));
            var svc = await ServiceToken.Header(clock);
            var orders = new List<object>();
            foreach (var (isin, part) in Split(amount, alloc))
            {
                var h = new Dictionary<string, string>(svc) { ["Idempotency-Key"] = $"contrib-{id}-{isin}" };
                var o = await Downstream.Send(http, "order-management", HttpMethod.Post, "/api/v1/orders", new { portfolioId = plan.Str("portfolioId"), isin, side = "BUY", amount = Money.Format(part, 2) }, forwardAuth: false, headers: h);
                orders.Add(new { isin, amount = Money.Format(part, 2), orderId = o.Ok ? o.Str("id") : null, status = o.Ok ? o.Str("status") : "FAILED", error = o.Ok ? null : o.Str("code") });
            }
            var status = orders.All(o => JsonSerializer.SerializeToElement(o, Canonical.Web).GetProperty("orderId").ValueKind == JsonValueKind.String) ? "INVESTING" : "PARTIALLY_INVESTED";
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO contributions VALUES (@id, @planId, @customerId, @amt, @source, @Period, @reference, @status, @entry, @orders, @at, @by)",
                new { id, planId, customerId, amt = Money.Format(amount, 2), source, b.Period, reference, status, entry = entry.Str("id"), orders = JsonSerializer.Serialize(orders, Canonical.Web), at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "contribution.received", id, new { contributionId = id, planId, customerId, amount = Money.Format(amount, 2), source, period = b.Period }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/contributions/{id}", Load(db, id));
        }).WithTags("Aportaciones").WithSummary("Aportar a un plan: entra en caja (ledger) y se invierte según la asignación");

        app.MapGet("/api/v1/contributions/{id}", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var cust = db.ExecuteScalar<string?>("SELECT customer_id FROM contributions WHERE id = @id", new { id });
            if (cust is null) return Problems.NotFound(http, "CONTRIBUTION_NOT_FOUND", $"No existe la aportación {id}.", null);
            var d = await Permissions.Check(http, p, "portfolio:read", cust);
            return d.Allowed ? Results.Ok(Load(db, id)) : Permissions.Denied(http, p, d, "CONTRIBUTION_NOT_FOUND", $"la aportación {id}");
        }).WithTags("Aportaciones").WithSummary("Una aportación con sus órdenes");

        app.MapGet("/api/v1/pension-plans/{planId}/contributions", async (HttpContext http, string planId, string? period, string? source) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{planId}", forwardAuth: false);
            if (!plan.Ok) return Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {planId}.", null);
            var d = await Permissions.Check(http, p, "portfolio:read", plan.Str("customerId"));
            if (!d.Allowed && p.Role != "Service") return Permissions.Denied(http, p, d, "PLAN_NOT_FOUND", $"el plan {planId}");
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM contributions WHERE plan_id = @planId AND (@period IS NULL OR period = @period) AND (@source IS NULL OR source = @source) ORDER BY created_at",
                new { planId, period, source = source?.ToUpperInvariant() }).Select(i => Load(db, i)).ToList();
            var total = db.Query<string>("SELECT amount FROM contributions WHERE plan_id = @planId", new { planId }).Sum(a => decimal.Parse(a, Money.Inv));
            return Results.Ok(new { planId, items, count = items.Count, totalContributed = new { amount = Money.Format(total, 2), currency = "SEK" } });
        }).WithTags("Aportaciones").WithSummary("Aportaciones de un plan (por periodo u origen) y el total");

        app.MapGet("/api/v1/internal/contributions", (string? planId, string? period, string? source, string? customerId) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM contributions WHERE (@planId IS NULL OR plan_id = @planId) AND (@period IS NULL OR period = @period) AND (@source IS NULL OR source = @source) AND (@customerId IS NULL OR customer_id = @customerId) ORDER BY created_at",
                new { planId, period, source, customerId }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Interno").WithSummary("(interno) Aportaciones por plan, periodo, origen o cliente");
    }
}

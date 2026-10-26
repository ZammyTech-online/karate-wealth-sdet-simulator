using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record HoldRequest(string? CustomerId, string? Amount, string? Reference, string? Reason);
public sealed record CashHold(string Id, string CustomerId, string Amount, string Reference, string? Reason, string Status, string CreatedAt, string? ClosedAt);
public sealed record CashPosition(string CustomerId, string Currency, string Booked, string Reserved, string Available, IReadOnlyList<CashHold> ActiveHolds);

/// <summary>
/// cash-management: saldo disponible frente a saldo contable. El contable sale del ledger; las retenciones (holds) apartan dinero
/// mientras una orden o una retirada está en curso. Disponible = contable − retenido. Nunca se puede retener más de lo disponible.
/// </summary>
public sealed class CashManagement : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "cash-management", "K-payments", 7004, 2, "Pagos y contabilidad",
        "Saldo contable (ledger), retenciones y saldo disponible del efectivo de cada cliente. Las retenciones son idempotentes por referencia.",
        DependsOn: new[] { "ledger-service", "customer-registry", "permission-service", "clock-service" });

    private static readonly object Gate = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS holds (id TEXT PRIMARY KEY, customer_id TEXT, amount TEXT, reference TEXT UNIQUE, reason TEXT, status TEXT, created_at TEXT, closed_at TEXT)");
    }

    private const string Sel = "SELECT id, customer_id AS CustomerId, amount, reference, reason, status, created_at AS CreatedAt, closed_at AS ClosedAt FROM holds";

    private static async Task<decimal> Booked(HttpContext? http, string customerId)
    {
        var r = await Downstream.Get(http, "ledger-service", $"/api/v1/accounts/customer:{customerId}:cash/balance", forwardAuth: false);
        return r.Ok ? decimal.Parse(r.Str("balance")!, CultureInfo.InvariantCulture) : throw new DependencyUnavailableException("ledger-service", "ledger-service respondió " + r.Status);
    }

    private static async Task<CashPosition> Position(HttpContext http, ServiceContext ctx, string customerId)
    {
        var booked = await Booked(http, customerId);
        using var db = ctx.OpenDb();
        var holds = db.Query<CashHold>(Sel + " WHERE customer_id = @customerId AND status = 'ACTIVE' ORDER BY created_at", new { customerId }).ToList();
        var reserved = holds.Sum(h => decimal.Parse(h.Amount, CultureInfo.InvariantCulture));
        return new CashPosition(customerId, "SEK", Money.Format(booked, 2), Money.Format(reserved, 2), Money.Format(booked - reserved, 2), holds);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/customers/{customerId}/cash", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "cash:read", customerId);
            if (guard is not null) return guard;
            return Results.Ok(await Position(http, ctx, customerId));
        }).WithTags("Efectivo").WithSummary("Saldo contable, retenido y disponible de un cliente").Produces<CashPosition>();

        app.MapGet("/api/v1/internal/customers/{customerId}/cash", async (HttpContext http, string customerId) => Results.Ok(await Position(http, ctx, customerId)))
            .WithTags("Interno").WithSummary("Saldo para otros servicios (red interna)").Produces<CashPosition>();

        app.MapPost("/api/v1/holds", async (HttpContext http, IClock clock, HoldRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (!Money.TryParse(b.Amount, out var amount) || amount <= 0 || decimal.Round(amount, 2) != amount) errors.Add(new FieldError("amount", "importe positivo con 2 decimales como mucho"));
            if (string.IsNullOrWhiteSpace(b.Reference)) errors.Add(new FieldError("reference", "obligatoria (id de la orden o retirada)"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Retención no válida.", null, errors);
            var booked = await Booked(http, b.CustomerId!);
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                var existing = db.QuerySingleOrDefault<CashHold>(Sel + " WHERE reference = @Reference", b);
                if (existing is not null) return Results.Ok(existing);
                var reserved = db.Query<string>("SELECT amount FROM holds WHERE customer_id = @CustomerId AND status = 'ACTIVE'", b).Sum(a => decimal.Parse(a, CultureInfo.InvariantCulture));
                var available = booked - reserved;
                if (amount > available)
                    return Problems.Unprocessable(http, "INSUFFICIENT_FUNDS", $"Saldo disponible insuficiente: hay {Money.Format(available, 2)} SEK y se necesitan {Money.Format(amount, 2)}.",
                        "Disponible = contable − retenido. Ingresa dinero primero (payments-gateway, /api/v1/deposits).");
                var id = "hold_" + Guid.NewGuid().ToString("N")[..16];
                db.Execute("INSERT INTO holds VALUES (@id, @CustomerId, @amount, @Reference, @Reason, 'ACTIVE', @at, NULL)",
                    new { id, b.CustomerId, amount = Money.Format(amount, 2), b.Reference, b.Reason, at = Nordic.Iso(clock.LocalNow) });
                return Results.Created($"/api/v1/holds/{id}", db.QuerySingle<CashHold>(Sel + " WHERE id = @id", new { id }));
            }
        }).WithTags("Retenciones").WithSummary("Apartar dinero (201) o devolver la retención existente con esa referencia (200)").Produces<CashHold>(201);

        IResult Close(HttpContext http, IClock clock, string id, string to)
        {
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                var h = db.QuerySingleOrDefault<CashHold>(Sel + " WHERE id = @id", new { id });
                if (h is null) return Problems.NotFound(http, "HOLD_NOT_FOUND", $"No existe la retención {id}.", null);
                if (h.Status == to) return Results.Ok(h);
                if (h.Status != "ACTIVE") return Problems.Conflict(http, "HOLD_CLOSED", $"La retención ya está {h.Status}.", null);
                db.Execute("UPDATE holds SET status = @to, closed_at = @at WHERE id = @id", new { to, at = Nordic.Iso(clock.LocalNow), id });
                return Results.Ok(db.QuerySingle<CashHold>(Sel + " WHERE id = @id", new { id }));
            }
        }

        app.MapPost("/api/v1/holds/{id}/release", (HttpContext http, IClock clock, string id) => Close(http, clock, id, "RELEASED"))
            .WithTags("Retenciones").WithSummary("Liberar (la operación no se hizo)").Produces<CashHold>();
        app.MapPost("/api/v1/holds/{id}/capture", (HttpContext http, IClock clock, string id) => Close(http, clock, id, "CAPTURED"))
            .WithTags("Retenciones").WithSummary("Consumir (la operación se hizo y ya está en el ledger)").Produces<CashHold>();
        app.MapGet("/api/v1/holds/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var h = db.QuerySingleOrDefault<CashHold>(Sel + " WHERE id = @id", new { id });
            return h is null ? Problems.NotFound(http, "HOLD_NOT_FOUND", $"No existe la retención {id}.", null) : Results.Ok(h);
        }).WithTags("Retenciones").WithSummary("Una retención").Produces<CashHold>();
        app.MapGet("/api/v1/holds", (HttpContext http, string? customerId, string? reference) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<CashHold>(Sel + " WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@reference IS NULL OR reference = @reference) ORDER BY created_at", new { customerId, reference }).ToList());
        }).WithTags("Retenciones").WithSummary("Retenciones de un cliente o por referencia").Produces<List<CashHold>>();
    }
}

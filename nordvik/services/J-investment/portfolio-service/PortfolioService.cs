using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AllocationLine(string Isin, decimal Percent);
public sealed record CreatePortfolioRequest(string? CustomerId, string? Name, string? Type, List<AllocationLine>? TargetAllocation, string? ExternalRef);
public sealed record UpdateAllocationRequest(List<AllocationLine>? TargetAllocation);
public sealed record Portfolio(string Id, string CustomerId, string Name, string Type, string Status, IReadOnlyList<AllocationLine> TargetAllocation, long Version, string CreatedAt, string UpdatedAt);

internal sealed record PortfolioRow(string Id, string CustomerId, string Name, string Type, string Status, long Version, string CreatedAt, string UpdatedAt);

/// <summary>
/// portfolio-service: carteras del cliente y su asignación objetivo. La suma debe ser exactamente 100,
/// cada fondo debe existir y estar abierto en product-catalog, y como mucho 10 fondos (config-service).
/// </summary>
public sealed class PortfolioService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "portfolio-service", "J-investment", 6900, 1, "Inversiones",
        "Carteras (ISK, KF, DEPOT) con asignación objetivo por fondo. Valida fondos con product-catalog y permisos con permission-service.",
        DependsOn: new[] { "clock-service", "customer-registry", "permission-service", "product-catalog", "config-service" });

    private static readonly string[] Types = { "ISK", "KF", "DEPOT", "PENSION" };
    private const string Sel = "SELECT id, customer_id AS CustomerId, name, type, status, version, created_at AS CreatedAt, updated_at AS UpdatedAt FROM portfolios";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS portfolios (id TEXT PRIMARY KEY, customer_id TEXT, name TEXT, type TEXT, status TEXT, version INTEGER, created_at TEXT, updated_at TEXT, external_ref TEXT);
                     CREATE TABLE IF NOT EXISTS allocations (portfolio_id TEXT, isin TEXT, percent TEXT, PRIMARY KEY (portfolio_id, isin))");
        var id = SeedIds.PortfolioAna;
        if (db.Execute("INSERT OR IGNORE INTO portfolios VALUES (@id, @c, 'Ahorro jubilación', 'ISK', 'ACTIVE', 1, '2026-01-02T09:00:00.000+01:00', '2026-01-02T09:00:00.000+01:00', NULL)", new { id, c = SeedIds.CustomerAna }) > 0)
            db.Execute("INSERT INTO allocations VALUES (@id, 'SE0000000001', '60.00'), (@id, 'SE0000000101', '40.00')", new { id });
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        db.Execute("DELETE FROM allocations WHERE portfolio_id IN (SELECT id FROM portfolios WHERE external_ref LIKE @p)", new { p = prefix + "%" });
        return db.Execute("DELETE FROM portfolios WHERE external_ref LIKE @p", new { p = prefix + "%" });
    }

    private static Portfolio? Load(ServiceContext ctx, string id)
    {
        using var db = ctx.OpenDb();
        var r = db.QuerySingleOrDefault<PortfolioRow>(Sel + " WHERE id = @id", new { id });
        if (r is null) return null;
        var lines = db.Query<(string Isin, string Percent)>("SELECT isin, percent FROM allocations WHERE portfolio_id = @id ORDER BY isin", new { id })
            .Select(l => new AllocationLine(l.Isin, decimal.Parse(l.Percent, Money.Inv))).ToList();
        return new Portfolio(r.Id, r.CustomerId, r.Name, r.Type, r.Status, lines, r.Version, r.CreatedAt, r.UpdatedAt);
    }

    private static async Task<IResult?> ValidateAllocation(HttpContext http, List<AllocationLine>? lines)
    {
        if (lines is null || lines.Count == 0)
            return Problems.BadRequest(http, "VALIDATION_FAILED", "targetAllocation es obligatorio.", "Ejemplo: [{ \"isin\": \"SE0000000001\", \"percent\": 60 }, { \"isin\": \"SE0000000101\", \"percent\": 40 }]",
                new[] { new FieldError("targetAllocation", "al menos un fondo") });
        var max = 10;
        var cfg = await Downstream.Get(http, "config-service", "/api/v1/config/portfolio-service/maxFundsPerPortfolio", forwardAuth: false);
        if (cfg.Ok && int.TryParse(cfg.Str("value"), out var m)) max = m;
        if (lines.Count > max) return Problems.Unprocessable(http, "TOO_MANY_FUNDS", $"Una cartera admite como mucho {max} fondos.", "El límite está en config-service (portfolio-service/maxFundsPerPortfolio).");
        var dup = lines.GroupBy(l => l.Isin).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) return Problems.Unprocessable(http, "DUPLICATE_FUND", $"El fondo {dup.Key} aparece dos veces.", "Junta los porcentajes en una sola línea.");
        if (lines.Any(l => l.Percent <= 0 || decimal.Round(l.Percent, 2) != l.Percent))
            return Problems.Unprocessable(http, "INVALID_PERCENT", "Cada porcentaje debe ser mayor que 0 y tener como mucho 2 decimales.", null);
        var sum = lines.Sum(l => l.Percent);
        if (sum != 100m)
            return Problems.Unprocessable(http, "ALLOCATION_NOT_100", $"La asignación suma {sum.ToString(Money.Inv)} y debe sumar exactamente 100.",
                "Ojo con los decimales: 33.33 + 33.33 + 33.34 = 100. Y en JavaScript 0.1 + 0.2 no es 0.3.");
        foreach (var l in lines)
        {
            var f = await Downstream.Get(http, "product-catalog", $"/api/v1/funds/{l.Isin}", forwardAuth: false);
            if (f.Status is 400 or 404) return Problems.Unprocessable(http, "UNKNOWN_FUND", $"El fondo {l.Isin} no existe en el catálogo.", "Lista los fondos en product-catalog (puerto 6801).");
            if (f.Ok && f.Str("status") == "CLOSED") return Problems.Unprocessable(http, "FUND_CLOSED", $"El fondo {l.Isin} está cerrado a nuevas inversiones.", null);
        }
        return null;
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/portfolios", async (HttpContext http, IClock clock, CreatePortfolioRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:write");
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 60) errors.Add(new FieldError("name", "obligatorio, máximo 60"));
            if (body.Type is null || !Types.Contains(body.Type) || (body.Type == "PENSION" && p.Role != "Service"))
                errors.Add(new FieldError("type", "ISK, KF o DEPOT (las carteras PENSION solo las crea pension-plan-service)"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "La cartera no es válida.", "Las carteras de pensión se crean desde pension-core (oleada 3).", errors);
            var guard = await CustomerGuard.Check(http, p, "portfolio:create", body.CustomerId!);
            if (guard is not null) return guard;
            var bad = await ValidateAllocation(http, body.TargetAllocation);
            if (bad is not null) return bad;
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM portfolios WHERE customer_id = @CustomerId AND name = @Name AND status = 'ACTIVE'", body) > 0)
                return Problems.Conflict(http, "PORTFOLIO_NAME_TAKEN", $"El cliente ya tiene una cartera activa llamada '{body.Name}'.", "Usa un nombre distinto por cartera.");
            var id = Guid.NewGuid().ToString();
            var now = Nordic.Iso(clock.LocalNow);
            db.Execute("INSERT INTO portfolios VALUES (@id, @CustomerId, @Name, @Type, 'ACTIVE', 1, @now, @now, @ExternalRef)", new { id, body.CustomerId, body.Name, body.Type, now, body.ExternalRef });
            foreach (var l in body.TargetAllocation!) db.Execute("INSERT INTO allocations VALUES (@id, @Isin, @pct)", new { id, l.Isin, pct = l.Percent.ToString("F2", Money.Inv) });
            http.Response.Headers.ETag = "\"1\"";
            return Results.Created($"/api/v1/portfolios/{id}", Load(ctx, id));
        }).WithTags("Carteras").WithSummary("Crear una cartera con su asignación objetivo").Produces<Portfolio>(201);

        app.MapGet("/api/v1/portfolios/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            var pf = Load(ctx, id);
            if (pf is null) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {id}.", null);
            var d = await Permissions.Check(http, p, "portfolio:read", pf.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {id}");
            http.Response.Headers.ETag = $"\"{pf.Version}\"";
            return Results.Ok(pf);
        }).WithTags("Carteras").WithSummary("Ver una cartera").Produces<Portfolio>();

        app.MapGet("/api/v1/internal/portfolios/{id}", (HttpContext http, string id) =>
        {
            var pf = Load(ctx, id);
            return pf is null ? Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {id}.", null) : Results.Ok(pf);
        }).WithTags("Interno").WithSummary("Cartera para otros servicios (red interna, sin autenticación)").Produces<Portfolio>();

        app.MapGet("/api/v1/internal/portfolios", (string? status, string? customerId) =>
        {
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM portfolios WHERE (@status IS NULL OR status = @status) AND (@customerId IS NULL OR customer_id = @customerId) ORDER BY created_at", new { status, customerId });
            return Results.Ok(ids.Select(i => Load(ctx, i)!).ToList());
        }).WithTags("Interno").WithSummary("Todas las carteras (red interna)").Produces<List<Portfolio>>();

        app.MapGet("/api/v1/customers/{customerId}/portfolios", async (HttpContext http, IClock clock, string customerId, string? status) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:read");
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "portfolio:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT id FROM portfolios WHERE customer_id = @customerId AND (@status IS NULL OR status = @status) ORDER BY created_at", new { customerId, status });
            return Results.Ok(ids.Select(i => Load(ctx, i)!).ToList());
        }).WithTags("Carteras").WithSummary("Carteras de un cliente").Produces<List<Portfolio>>();

        app.MapPut("/api/v1/portfolios/{id}/allocation", async (HttpContext http, IClock clock, string id, UpdateAllocationRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:write");
            if (denied is not null) return denied;
            var pf = Load(ctx, id);
            if (pf is null) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {id}.", null);
            var d = await Permissions.Check(http, p, "portfolio:update", pf.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {id}");
            var ifMatch = http.Request.Headers.IfMatch.ToString();
            if (string.IsNullOrEmpty(ifMatch)) return Problems.Of(http, 428, "PRECONDITION_REQUIRED", "Falta If-Match", "Envía la versión de la cartera que leíste.", "Copia la cabecera ETag de un GET.");
            if (ifMatch.Trim('"', ' ', 'W', '/') != pf.Version.ToString())
                return Problems.Of(http, 412, "VERSION_CONFLICT", "La versión no coincide", $"La versión actual es \"{pf.Version}\".", "Vuelve a leer la cartera.");
            if (pf.Status != "ACTIVE") return Problems.Conflict(http, "PORTFOLIO_CLOSED", "La cartera está cerrada.", null);
            var bad = await ValidateAllocation(http, body.TargetAllocation);
            if (bad is not null) return bad;
            using var db = ctx.OpenDb();
            db.Execute("DELETE FROM allocations WHERE portfolio_id = @id", new { id });
            foreach (var l in body.TargetAllocation!) db.Execute("INSERT INTO allocations VALUES (@id, @Isin, @pct)", new { id, l.Isin, pct = l.Percent.ToString("F2", Money.Inv) });
            db.Execute("UPDATE portfolios SET version = version + 1, updated_at = @now WHERE id = @id", new { now = Nordic.Iso(clock.LocalNow), id });
            var updated = Load(ctx, id)!;
            http.Response.Headers.ETag = $"\"{updated.Version}\"";
            return Results.Ok(updated);
        }).WithTags("Carteras").WithSummary("Cambiar la asignación objetivo (exige If-Match)").Produces<Portfolio>();

        app.MapDelete("/api/v1/portfolios/{id}", async (HttpContext http, IClock clock, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "portfolios:write");
            if (denied is not null) return denied;
            var pf = Load(ctx, id);
            if (pf is null) return Problems.NotFound(http, "PORTFOLIO_NOT_FOUND", $"No existe la cartera {id}.", null);
            var d = await Permissions.Check(http, p, "portfolio:update", pf.CustomerId);
            if (!d.Allowed) return Permissions.Denied(http, p, d, "PORTFOLIO_NOT_FOUND", $"la cartera {id}");
            if (pf.Status == "CLOSED") return Problems.Conflict(http, "PORTFOLIO_ALREADY_CLOSED", "La cartera ya estaba cerrada.", null);
            using var db = ctx.OpenDb();
            db.Execute("UPDATE portfolios SET status = 'CLOSED', version = version + 1, updated_at = @now WHERE id = @id", new { now = Nordic.Iso(clock.LocalNow), id });
            return Results.Ok(Load(ctx, id));
        }).WithTags("Carteras").WithSummary("Cerrar una cartera (sin posiciones: en la oleada 2 se exigirá que esté vacía)").Produces<Portfolio>();
    }
}

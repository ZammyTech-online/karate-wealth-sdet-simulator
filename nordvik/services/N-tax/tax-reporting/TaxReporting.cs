using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TaxReportRequest(int? Year, string? Kind);

/// <summary>
/// tax-reporting: el informe fiscal anual en lote. Junta los pagos de pensión del año (payout-execution) por cliente, saca el número nacional
/// de customer-registry, entrega el fichero de control a la Agencia Tributaria (E8) y guarda el acuse. Un ORIGINAL por año; los cambios
/// posteriores van como CORRECTION. Lo que el cliente ve en su certificado anual sale de aquí.
/// </summary>
public sealed class TaxReporting : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "tax-reporting", "N-tax", 7301, 4, "Fiscalidad",
        "Informe fiscal anual en lote: pagos de pensión del año por cliente, fichero de control a la Agencia Tributaria con acuse; original y correcciones.",
        DependsOn: new[] { "payout-execution", "customer-registry", "tax-agency-adapter", "clock-service" }, Emits: new[] { "tax-report.filed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS reports (id TEXT PRIMARY KEY, year INTEGER, kind TEXT, status TEXT, customers INTEGER, total_gross TEXT, total_tax TEXT, receipt TEXT, filed_at TEXT, filed_by TEXT);
                     CREATE TABLE IF NOT EXISTS report_lines (report_id TEXT, line_no INTEGER, customer_id TEXT, masked TEXT, gross TEXT, tax TEXT, payments INTEGER, status TEXT, PRIMARY KEY (report_id, line_no))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, long Year, string Kind, string Status, long Cust, string Gross, string Tax, string Receipt, string At, string By)>(
            "SELECT id, year, kind, status, customers, total_gross, total_tax, receipt, filed_at, filed_by FROM reports WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var lines = db.Query<(long No, string Masked, string Gross, string Tax, long Payments, string Status)>("SELECT line_no, masked, gross, tax, payments, status FROM report_lines WHERE report_id = @id ORDER BY line_no", new { id })
            .Select(l => new { line = l.No, personalNumberMasked = l.Masked, gross = l.Gross, taxWithheld = l.Tax, payments = l.Payments, status = l.Status }).ToList();
        return new { id = r.Id, year = r.Year, kind = r.Kind, status = r.Status, customers = r.Cust, totalGross = r.Gross, totalTaxWithheld = r.Tax, receipt = JsonDocument.Parse(r.Receipt).RootElement, lines, filedAt = r.At, filedBy = r.By };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Admin", "Compliance" };

        app.MapPost("/api/v1/tax-reports", async (HttpContext http, TaxReportRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            var kind = (b.Kind ?? "ORIGINAL").ToUpperInvariant();
            if (b.Year is null || b.Year < 2015 || b.Year > clock.Today.Year || kind is not ("ORIGINAL" or "CORRECTION"))
                return Problems.BadRequest(http, "VALIDATION_FAILED", $"year entre 2015 y {clock.Today.Year}; kind ORIGINAL o CORRECTION.", null);
            using (var db0 = ctx.OpenDb())
            {
                var filed = db0.ExecuteScalar<string?>("SELECT id FROM reports WHERE year = @Year AND status <> 'REJECTED' ORDER BY filed_at LIMIT 1", new { b.Year });
                if (kind == "ORIGINAL" && filed is not null) return Problems.Conflict(http, "REPORT_ALREADY_FILED", $"El año {b.Year} ya se presentó: {filed}.", "Para cambiarlo, envía kind: CORRECTION.");
                if (kind == "CORRECTION" && filed is null) return Problems.Conflict(http, "NOTHING_TO_CORRECT", $"El año {b.Year} no se ha presentado todavía.", null);
            }
            var pays = await Downstream.Get(http, "payout-execution", $"/api/v1/internal/payments?year={b.Year}", forwardAuth: false);
            var byCustomer = pays.Json.GetProperty("items").EnumerateArray()
                .GroupBy(x => x.GetProperty("customerId").GetString()!)
                .Select(g => (Customer: g.Key, Gross: g.Sum(x => decimal.Parse(x.GetProperty("gross").GetString()!, Money.Inv)), Tax: g.Sum(x => decimal.Parse(x.GetProperty("taxWithheld").GetString()!, Money.Inv)), Count: g.Count()))
                .OrderBy(x => x.Customer).ToList();
            var lines = new List<(string Customer, string Pnr, decimal Gross, decimal Tax, int Count)>();
            foreach (var c in byCustomer)
            {
                var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{c.Customer}", forwardAuth: false);
                lines.Add((c.Customer, cust.Str("nationalId") ?? "", c.Gross, c.Tax, c.Count));
            }
            var filing = await Downstream.Send(http, "tax-agency-adapter", HttpMethod.Post, "/api/v1/internal/filings", new
            {
                year = b.Year, kind,
                lines = lines.Select(l => new { personalNumber = l.Pnr, incomeType = "PENSION", gross = Money.Format(l.Gross, 2), taxWithheld = Money.Format(l.Tax, 2) }),
            }, forwardAuth: false);
            if (!filing.Ok) return Results.Content(filing.Raw, "application/problem+json", System.Text.Encoding.UTF8, filing.Status);
            var rejected = filing.Json.GetProperty("rejected").EnumerateArray().ToDictionary(x => x.GetProperty("line").GetInt32(), x => x.GetProperty("code").GetString());
            var id = "tr_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO reports VALUES (@id, @Year, @kind, @status, @n, @g, @t, @receipt, @at, @by)",
                    new { id, b.Year, kind, status = rejected.Count == 0 ? "FILED" : "PARTIALLY_ACCEPTED", n = lines.Count, g = Money.Format(lines.Sum(l => l.Gross), 2), t = Money.Format(lines.Sum(l => l.Tax), 2),
                          receipt = filing.Raw, at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
                for (var i = 0; i < lines.Count; i++)
                    db.Execute("INSERT INTO report_lines VALUES (@id, @no, @c, @m, @g, @t, @n, @s)",
                        new { id, no = i + 1, c = lines[i].Customer, m = NationalId.Check(lines[i].Pnr).Valid ? NationalId.Mask(NationalId.Check(lines[i].Pnr).Normalized!) : "********-****",
                              g = Money.Format(lines[i].Gross, 2), t = Money.Format(lines[i].Tax, 2), n = lines[i].Count, s = rejected.TryGetValue(i + 1, out var code) ? "REJECTED:" + code : "ACCEPTED" }, tx);
                Outbox.Add(db, http, clock, "tax-report.filed", id, new { reportId = id, year = b.Year, kind, customers = lines.Count }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/tax-reports/{id}", Load(db, id));
        }).WithTags("Informes fiscales").WithSummary("Generar y presentar el informe anual (ORIGINAL o CORRECTION)");

        app.MapGet("/api/v1/tax-reports/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } r ? Results.Ok(r) : Problems.NotFound(http, "REPORT_NOT_FOUND", $"No existe el informe {id}.", null);
        }).WithTags("Informes fiscales").WithSummary("Un informe con su acuse y sus líneas (enmascaradas)");

        app.MapGet("/api/v1/tax-reports", (HttpContext http, int? year) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM reports WHERE (@year IS NULL OR year = @year) ORDER BY filed_at", new { year }).Select(i => Load(db, i)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Informes fiscales").WithSummary("Informes presentados");

        app.MapGet("/api/v1/internal/tax-reports/{year:int}/customers/{customerId}", (HttpContext http, int year, string customerId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Kind, string At, string Masked, string Gross, string Tax, long Payments, string Status)>(
                @"SELECT r.id, r.kind, r.filed_at, l.masked, l.gross, l.tax, l.payments, l.status FROM reports r JOIN report_lines l ON l.report_id = r.id
                  WHERE r.year = @year AND l.customer_id = @customerId ORDER BY r.filed_at DESC, r.rowid DESC LIMIT 1", new { year, customerId });
            return r.Id is null ? Problems.NotFound(http, "NOT_REPORTED", $"El cliente no aparece en ningún informe de {year}.", null)
                : Results.Ok(new { year, customerId, reportId = r.Id, kind = r.Kind, filedAt = r.At, personalNumberMasked = r.Masked, gross = r.Gross, taxWithheld = r.Tax, payments = r.Payments, status = r.Status });
        }).WithTags("Interno").WithSummary("(interno) Línea de un cliente en el último informe del año");
    }
}

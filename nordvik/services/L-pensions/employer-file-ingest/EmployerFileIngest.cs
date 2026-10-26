using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record EmployerFileRequest(string? FileId, string? EmployerOrgNumber, string? Period, string? Csv);
public sealed record LineResult(int Line, string PersonalNumberMasked, string? Amount, string Status, string? Code, string? ContributionId);

/// <summary>
/// employer-file-ingest: procesa el fichero mensual de una empresa línea a línea. Una línea vale si el número nacional es válido,
/// es cliente, tiene un plan OCCUPATIONAL con esa empresa, el importe es positivo, el convenio coincide y el mes no está repetido.
/// Las válidas se aportan (contribution-service); las demás van a la cola de errores. El acuse dice cuántas y por qué.
/// </summary>
public sealed class EmployerFileIngest : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "employer-file-ingest", "L-pensions", 7103, 3, "Pensiones",
        "Ficheros mensuales de empleadores: validación línea a línea (empleado, plan, importe, convenio, mes repetido), aportación de las válidas, cola de errores y acuse.",
        DependsOn: new[] { "customer-registry", "pension-plan-service", "contribution-service", "clock-service" }, Emits: new[] { "employer-file.processed" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS files (id TEXT PRIMARY KEY, file_id TEXT UNIQUE, employer_org TEXT, period TEXT, lines INTEGER, accepted INTEGER, rejected INTEGER, accepted_amount TEXT, results TEXT, processed_at TEXT);
                     CREATE TABLE IF NOT EXISTS seen (customer_id TEXT, employer_org TEXT, period TEXT, PRIMARY KEY (customer_id, employer_org, period))");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string File, string Org, string Period, long Lines, long Acc, long Rej, string Amt, string Results, string At)>(
            "SELECT id, file_id, employer_org, period, lines, accepted, rejected, accepted_amount, results, processed_at FROM files WHERE id = @id OR file_id = @id", new { id });
        if (r.Id is null) return null;
        var results = JsonDocument.Parse(r.Results).RootElement;
        return new
        {
            id = r.Id, fileId = r.File, employerOrgNumber = r.Org, period = r.Period, processedAt = r.At,
            receipt = new
            {
                fileId = r.File, period = r.Period, totalLines = r.Lines, accepted = r.Acc, acceptedAmount = new { amount = r.Amt, currency = "SEK" },
                rejected = results.EnumerateArray().Where(x => x.GetProperty("status").GetString() == "REJECTED")
                    .Select(x => new { line = x.GetProperty("line").GetInt32(), personalNumberMasked = x.GetProperty("personalNumberMasked").GetString(), code = x.GetProperty("code").GetString() }).ToList(),
            },
            lines = results,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] staff = { "Operations", "Admin", "Service" };

        app.MapPost("/api/v1/employer-files", async (HttpContext http, EmployerFileRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            if (string.IsNullOrWhiteSpace(b.Csv) || string.IsNullOrWhiteSpace(b.EmployerOrgNumber) || b.Period is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta employerOrgNumber, period y csv.", "Cabecera del CSV: personnummer;belopp;avtal;period");
            var fileId = b.FileId ?? "manual_" + Guid.NewGuid().ToString("N")[..10];
            using (var db0 = ctx.OpenDb())
                if (db0.ExecuteScalar<string?>("SELECT id FROM files WHERE file_id = @fileId", new { fileId }) is { } done)
                    return Results.Ok(Load(db0, done));
            var rows = b.Csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
            var svc = await ServiceToken.Header(clock);
            var results = new List<LineResult>(); decimal acceptedAmount = 0;
            var inFile = new HashSet<string>();
            for (var i = 0; i < rows.Count; i++)
            {
                var c = rows[i].Split(';');
                var pnr = c.ElementAtOrDefault(0)?.Trim() ?? "";
                var check = NationalId.Check(pnr);
                var masked = check.Valid ? NationalId.Mask(check.Normalized!) : "********-****";
                LineResult Reject(string code) => new(i + 1, masked, c.ElementAtOrDefault(1), "REJECTED", code, null);
                if (c.Length < 3) { results.Add(Reject("MALFORMED_LINE")); continue; }
                if (!check.Valid) { results.Add(Reject("INVALID_PERSONAL_NUMBER")); continue; }
                if (!Money.TryParse(c[1], out var amount)) { results.Add(Reject("INVALID_AMOUNT")); continue; }
                if (amount <= 0) { results.Add(Reject("NEGATIVE_AMOUNT")); continue; }
                var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/by-national-id/{check.Normalized}", forwardAuth: false);
                if (!cust.Ok) { results.Add(Reject("UNKNOWN_EMPLOYEE")); continue; }
                var customerId = cust.Str("id")!;
                var plans = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans?customerId={customerId}&employerOrgNumber={b.EmployerOrgNumber}&type=OCCUPATIONAL", forwardAuth: false);
                var plan = plans.Json.GetProperty("items").EnumerateArray().FirstOrDefault(pl => pl.GetProperty("status").GetString() == "ACTIVE");
                if (plan.ValueKind != JsonValueKind.Object) { results.Add(Reject("NO_PLAN_WITH_EMPLOYER")); continue; }
                if (!string.Equals(plan.GetProperty("agreement").GetString(), c[2].Trim(), StringComparison.OrdinalIgnoreCase)) { results.Add(Reject("AGREEMENT_MISMATCH")); continue; }
                using (var db1 = ctx.OpenDb())
                    if (!inFile.Add(customerId) || db1.ExecuteScalar<long>("SELECT COUNT(*) FROM seen WHERE customer_id = @customerId AND employer_org = @o AND period = @Period", new { customerId, o = b.EmployerOrgNumber, b.Period }) > 0)
                    { results.Add(Reject("DUPLICATE_MONTH")); continue; }
                var ctb = await Downstream.Send(http, "contribution-service", HttpMethod.Post, $"/api/v1/pension-plans/{plan.GetProperty("id").GetString()}/contributions",
                    new { amount = Money.Format(amount, 2), source = "EMPLOYER", period = b.Period, reference = fileId }, forwardAuth: false, headers: svc);
                if (!ctb.Ok) { results.Add(Reject(ctb.Str("code") ?? "CONTRIBUTION_FAILED")); continue; }
                using (var db2 = ctx.OpenDb()) db2.Execute("INSERT OR IGNORE INTO seen VALUES (@customerId, @o, @Period)", new { customerId, o = b.EmployerOrgNumber, b.Period });
                acceptedAmount += amount;
                results.Add(new LineResult(i + 1, masked, Money.Format(amount, 2), "ACCEPTED", null, ctb.Str("id")));
            }
            var id = "ef_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO files VALUES (@id, @fileId, @EmployerOrgNumber, @Period, @n, @acc, @rej, @amt, @res, @at)",
                new { id, fileId, b.EmployerOrgNumber, b.Period, n = rows.Count, acc = results.Count(r => r.Status == "ACCEPTED"), rej = results.Count(r => r.Status == "REJECTED"),
                      amt = Money.Format(acceptedAmount, 2), res = JsonSerializer.Serialize(results, Canonical.Web), at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "employer-file.processed", id, new { ingestId = id, fileId, employerOrgNumber = b.EmployerOrgNumber, period = b.Period, accepted = results.Count(r => r.Status == "ACCEPTED"), rejected = results.Count(r => r.Status == "REJECTED") }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/employer-files/{id}", Load(db, id));
        }).WithTags("Ficheros").WithSummary("Procesar un fichero CSV de un empleador (idempotente por fileId)");

        app.MapGet("/api/v1/employer-files/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } f ? Results.Ok(f) : Problems.NotFound(http, "FILE_NOT_FOUND", $"No existe el fichero {id}.", null);
        }).WithTags("Ficheros").WithSummary("Resultado línea a línea");

        app.MapGet("/api/v1/employer-files/{id}/receipt", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } f ? Results.Ok(JsonSerializer.SerializeToElement(f, Canonical.Web).GetProperty("receipt")) : Problems.NotFound(http, "FILE_NOT_FOUND", $"No existe el fichero {id}.", null);
        }).WithTags("Ficheros").WithSummary("Acuse: aceptadas, importe aceptado y rechazadas con su código");

        app.MapGet("/api/v1/error-queue", (HttpContext http, string? code) =>
        {
            if (Auth.Require(http, clock, out _, roles: staff) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<(string Id, string File, string Results)>("SELECT id, file_id, results FROM files ORDER BY processed_at").SelectMany(f =>
                JsonDocument.Parse(f.Results).RootElement.EnumerateArray().Where(x => x.GetProperty("status").GetString() == "REJECTED" && (code is null || x.GetProperty("code").GetString() == code))
                    .Select(x => new { ingestId = f.Id, fileId = f.File, line = x.GetProperty("line").GetInt32(), personalNumberMasked = x.GetProperty("personalNumberMasked").GetString(), code = x.GetProperty("code").GetString() })).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Ficheros").WithSummary("Cola de errores de todos los ficheros (por código)");
    }
}

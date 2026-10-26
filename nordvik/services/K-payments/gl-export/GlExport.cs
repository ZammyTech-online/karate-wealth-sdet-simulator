using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ExportRequest(string? Date);
public sealed record GlExportFile(string Id, string Date, int Version, int Entries, int Lines, string TotalDebit, string TotalCredit, string Sha256, string FileName, string CreatedAt, string CreatedBy);

/// <summary>
/// gl-export: exportación contable a fichero CSV (separado por punto y coma) para el sistema de contabilidad general.
/// Lleva una línea final de totales y la cabecera X-Checksum-SHA256: tu test puede recalcularla para comprobar el fichero.
/// </summary>
public sealed class GlExport : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "gl-export", "K-payments", 7008, 2, "Pagos y contabilidad",
        "Exporta los asientos de un día a CSV con totales y checksum SHA-256. Una sola exportación por día salvo force=true (versión nueva).",
        DependsOn: new[] { "ledger-service", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS exports (id TEXT PRIMARY KEY, date TEXT, version INTEGER, entries INTEGER, lines INTEGER, total_debit TEXT, total_credit TEXT, sha256 TEXT,
                     file_name TEXT, created_at TEXT, created_by TEXT, content TEXT)");
    }


    private static GlExportFile? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Date, long Version, long Entries, long Lines, string Dr, string Cr, string Sha, string File, string At, string By)>(
            "SELECT id, date, version, entries, lines, total_debit, total_credit, sha256, file_name, created_at, created_by FROM exports WHERE id = @id", new { id });
        return r.Id is null ? null : new GlExportFile(r.Id, r.Date, (int)r.Version, (int)r.Entries, (int)r.Lines, r.Dr, r.Cr, r.Sha, r.File, r.At, r.By);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/exports", async (HttpContext http, IClock clock, ExportRequest b, bool? force) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            if (!Money.TryParseDate(b.Date, out var date)) return Problems.BadRequest(http, "VALIDATION_FAILED", "date es obligatoria (AAAA-MM-DD).", null, new[] { new FieldError("date", "AAAA-MM-DD") });
            if (date >= clock.Today) return Problems.Unprocessable(http, "DAY_NOT_CLOSED", $"El día {b.Date} aún no ha terminado: solo se exportan días cerrados.", "Exporta ayer o adelanta el reloj virtual.");
            using var db = ctx.OpenDb();
            var version = (int)(db.ExecuteScalar<long?>("SELECT MAX(version) FROM exports WHERE date = @d", new { d = b.Date }) ?? 0);
            if (version > 0 && force != true)
                return Problems.Conflict(http, "EXPORT_EXISTS", $"El día {b.Date} ya se exportó (versión {version}).", "Repite con ?force=true para generar la versión " + (version + 1) + ".");
            var entries = await Downstream.Get(http, "ledger-service", $"/api/v1/journal-entries?date={b.Date}&limit=5000", forwardAuth: false);
            if (!entries.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "ledger-service respondió " + entries.Status, null);
            var sb = new StringBuilder("entryId;reference;valueDate;lineNo;account;debit;credit\n");
            decimal dr = 0, cr = 0; var lines = 0; var count = 0;
            foreach (var e in entries.Json.EnumerateArray())
            {
                count++;
                var n = 0;
                foreach (var l in e.GetProperty("lines").EnumerateArray())
                {
                    n++; lines++;
                    var d = l.GetProperty("debit").GetString()!; var c = l.GetProperty("credit").GetString()!;
                    dr += decimal.Parse(d, CultureInfo.InvariantCulture); cr += decimal.Parse(c, CultureInfo.InvariantCulture);
                    sb.Append($"{e.GetProperty("id").GetString()};{e.GetProperty("reference").GetString()};{e.GetProperty("valueDate").GetString()};{n};{l.GetProperty("account").GetString()};{d};{c}\n");
                }
            }
            sb.Append($"TOTAL;{count};{lines};;;{Money.Format(dr, 2)};{Money.Format(cr, 2)}\n");
            var content = sb.ToString();
            var id = "gl_" + Guid.NewGuid().ToString("N")[..12];
            var fileName = $"nordvik-gl-{b.Date}-v{version + 1}.csv";
            db.Execute("INSERT INTO exports VALUES (@id, @d, @v, @count, @lines, @dr, @cr, @sha, @fileName, @at, @by, @content)",
                new { id, d = b.Date, v = version + 1, count, lines, dr = Money.Format(dr, 2), cr = Money.Format(cr, 2), sha = Canonical.Sha256Hex(content), fileName, at = Nordic.Iso(clock.LocalNow), by = p.Sub, content });
            return Results.Created($"/api/v1/exports/{id}", Load(db, id));
        }).WithTags("Exportación").WithSummary("Exportar los asientos de un día cerrado (Operaciones)").Produces<GlExportFile>(201);

        app.MapGet("/api/v1/exports/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var e = Load(db, id);
            return e is null ? Problems.NotFound(http, "EXPORT_NOT_FOUND", $"No existe la exportación {id}.", null) : Results.Ok(e);
        }).WithTags("Exportación").WithSummary("Datos de una exportación").Produces<GlExportFile>();

        app.MapGet("/api/v1/exports/{id}/file", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string? Content, string File, string Sha)>("SELECT content, file_name, sha256 FROM exports WHERE id = @id", new { id });
            if (r.Content is null) return Problems.NotFound(http, "EXPORT_NOT_FOUND", $"No existe la exportación {id}.", null);
            http.Response.Headers["X-Checksum-SHA256"] = r.Sha;
            return Results.File(Encoding.UTF8.GetBytes(r.Content), "text/csv; charset=utf-8", r.File);
        }).WithTags("Exportación").WithSummary("Descargar el CSV (Content-Disposition: attachment)");

        app.MapGet("/api/v1/exports", (string? date) =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM exports WHERE (@date IS NULL OR date = @date) ORDER BY date, version", new { date }).Select(i => Load(db, i)!).ToList());
        }).WithTags("Exportación").WithSummary("Exportaciones hechas").Produces<List<GlExportFile>>();
    }
}

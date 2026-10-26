using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// legacy-mainframe-bridge: el puente con el mainframe de pensiones. Habla en FICHEROS DE ANCHO FIJO (80 columnas, sin separadores, números
/// con ceros a la izquierda y 2 decimales implícitos) y con CÓDIGOS DE RETORNO numéricos (rc). La exportación nocturna solo se puede lanzar en
/// la ventana de lote de 02:00 a 04:00 (reloj virtual); fuera, rc 0016. Lo que manda el mainframe (saldos heredados) se valida línea a línea.
/// </summary>
public sealed class LegacyMainframeBridge : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "legacy-mainframe-bridge", "P-integrations", 7509, 4, "Integraciones",
        "Puente con el mainframe: ficheros de ancho fijo de 80 columnas, lote nocturno en la ventana 02:00-04:00 y códigos de retorno numéricos por línea.",
        DependsOn: new[] { "pension-plan-service", "clock-service" });

    public const int Width = 80;
    public static readonly Dictionary<string, string> ReturnCodes = new()
    {
        ["0000"] = "OK", ["0004"] = "WARNING: ZERO BALANCE", ["0008"] = "BAD RECORD LENGTH", ["0012"] = "UNKNOWN RECORD TYPE", ["0016"] = "BATCH WINDOW CLOSED",
        ["0042"] = "INVALID DATE", ["0043"] = "INVALID AMOUNT", ["0044"] = "UNKNOWN PLAN", ["0099"] = "FILE CHECKSUM MISMATCH",
    };

    public static string Pad(string s, int n) => (s.Length > n ? s[..n] : s).PadRight(n);
    public static string Num(decimal v, int n) => ((long)Math.Round(v * 100, MidpointRounding.ToEven)).ToString().PadLeft(n, '0');

    /// <summary>Línea de saldo heredado: BL + plan(20) + saldo(15, 2 decimales implícitos) + fecha AAAAMMDD(8) + relleno.</summary>
    public static string BalanceLine(string planId, decimal balance, DateOnly date) => Pad("BL" + Pad(planId, 20) + Num(balance, 15) + date.ToString("yyyyMMdd"), Width);

    public static (string Rc, string? PlanId, decimal? Balance, DateOnly? Date) ParseBalance(string line)
    {
        if (line.Length != Width) return ("0008", null, null, null);
        if (!line.StartsWith("BL")) return ("0012", null, null, null);
        var plan = line[2..22].Trim();
        if (!long.TryParse(line[22..37], out var cents) || line[22..37].Any(c => !char.IsDigit(c))) return ("0043", plan, null, null);
        if (!DateOnly.TryParseExact(line[37..45], "yyyyMMdd", out var d)) return ("0042", plan, cents / 100m, null);
        return (cents == 0 ? "0004" : "0000", plan, cents / 100m, d);
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS exports (date TEXT PRIMARY KEY, content TEXT, records INTEGER, created_at TEXT);
                     CREATE TABLE IF NOT EXISTS balances (plan_id TEXT PRIMARY KEY, balance TEXT, as_of TEXT, received_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        string[] ops = { "Operations", "Admin", "Service" };

        IResult Rc(HttpContext http, int status, string rc, string? detail = null)
        {
            var body = Problems.BodyOf(http, status, "MAINFRAME_RC_" + rc, ReturnCodes[rc], detail ?? ReturnCodes[rc], "Los códigos rc están en GET /api/v1/return-codes.");
            body["rc"] = rc;
            return Results.Json(body, statusCode: status, contentType: "application/problem+json");
        }

        app.MapGet("/api/v1/return-codes", () => Results.Ok(ReturnCodes.Select(k => new { rc = k.Key, message = k.Value }))).WithTags("Mainframe").WithSummary("Tabla de códigos de retorno");

        app.MapGet("/api/v1/layouts", () => Results.Ok(new object[]
        {
            new { record = "HD", description = "Cabecera", fields = new object[] { new { name = "type", from = 1, length = 2 }, new { name = "fileDate", from = 3, length = 8, format = "AAAAMMDD" }, new { name = "system", from = 11, length = 8 } } },
            new { record = "PL", description = "Plan exportado", fields = new object[] { new { name = "type", from = 1, length = 2 }, new { name = "planId", from = 3, length = 20, pad = "espacios a la derecha" }, new { name = "planType", from = 23, length = 14 }, new { name = "status", from = 37, length = 10 }, new { name = "createdOn", from = 47, length = 8, format = "AAAAMMDD" } } },
            new { record = "TR", description = "Cola", fields = new object[] { new { name = "type", from = 1, length = 2 }, new { name = "records", from = 3, length = 9, pad = "ceros a la izquierda" } } },
            new { record = "BL", description = "Saldo heredado (entrada)", fields = new object[] { new { name = "type", from = 1, length = 2 }, new { name = "planId", from = 3, length = 20 }, new { name = "balance", from = 23, length = 15, format = "céntimos, ceros a la izquierda" }, new { name = "asOf", from = 38, length = 8, format = "AAAAMMDD" } } },
        })).WithTags("Mainframe").WithSummary("Diseño de registro de cada tipo de línea (posiciones desde 1)");

        app.MapPost("/api/v1/batches/run", async (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            var now = clock.LocalNow;
            if (now.Hour < 2 || now.Hour >= 4) return Rc(http, 409, "0016", $"Son las {now:HH:mm}: el lote solo corre de 02:00 a 04:00.");
            var date = DateOnly.FromDateTime(now.DateTime);
            var plans = await Downstream.Get(http, "pension-plan-service", "/api/v1/internal/pension-plans", forwardAuth: false);
            var sb = new StringBuilder();
            sb.Append(Pad("HD" + date.ToString("yyyyMMdd") + "NORDVIK ", Width)).Append('\n');
            var n = 0;
            foreach (var p in plans.Json.GetProperty("items").EnumerateArray())
            {
                var created = DateOnly.FromDateTime(DateTimeOffset.Parse(p.GetProperty("createdAt").GetString()!).DateTime);
                sb.Append(Pad("PL" + Pad(p.GetProperty("id").GetString()!, 20) + Pad(p.GetProperty("type").GetString()!, 14) + Pad(p.GetProperty("status").GetString()!, 10) + created.ToString("yyyyMMdd"), Width)).Append('\n');
                n++;
            }
            sb.Append(Pad("TR" + n.ToString().PadLeft(9, '0'), Width)).Append('\n');
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO exports VALUES (@d, @c, @n, @at) ON CONFLICT(date) DO UPDATE SET content = @c, records = @n, created_at = @at",
                new { d = date.ToString("yyyy-MM-dd"), c = sb.ToString(), n, at = Nordic.Iso(now) });
            return Results.Ok(new { rc = "0000", date = date.ToString("yyyy-MM-dd"), records = n, file = $"/api/v1/batches/{date:yyyy-MM-dd}/export" });
        }).WithTags("Mainframe").WithSummary("Lanzar el lote nocturno (solo de 02:00 a 04:00)");

        app.MapGet("/api/v1/batches/{date}/export", (HttpContext http, string date) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var c = db.ExecuteScalar<string?>("SELECT content FROM exports WHERE date = @date", new { date });
            return c is null ? Problems.NotFound(http, "EXPORT_NOT_FOUND", $"No hay exportación del {date}.", "Se genera con POST /api/v1/batches/run en la ventana de lote.")
                : Results.Text(c, "text/plain; charset=utf-8");
        }).WithTags("Mainframe").WithSummary("Fichero exportado (texto de ancho fijo)");

        app.MapPost("/api/v1/inbound/balances", async (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: ops) is { } denied) return denied;
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var raw = (await reader.ReadToEndAsync()).Replace("\r", "");
            var lines = raw.Split('\n').Where(l => l.Length > 0).ToList();
            var results = new List<(int Line, string Rc, string? Plan)>();
            var plans = (await Downstream.Get(http, "pension-plan-service", "/api/v1/internal/pension-plans", forwardAuth: false)).Json.GetProperty("items").EnumerateArray()
                .Select(p => p.GetProperty("id").GetString()!).ToHashSet();
            using var db = ctx.OpenDb();
            for (var i = 0; i < lines.Count; i++)
            {
                var (rc, plan, bal, d) = ParseBalance(lines[i]);
                if (rc is "0000" or "0004" && !plans.Contains(plan!)) rc = "0044";
                if (rc is "0000" or "0004")
                    db.Execute("INSERT INTO balances VALUES (@plan, @b, @d, @at) ON CONFLICT(plan_id) DO UPDATE SET balance = @b, as_of = @d, received_at = @at",
                        new { plan, b = Money.Format(bal!.Value, 2), d = d!.Value.ToString("yyyy-MM-dd"), at = Nordic.Iso(clock.LocalNow) });
                results.Add((i + 1, rc, plan));
            }
            var worst = results.Select(r => r.Rc).DefaultIfEmpty("0000").Max()!;
            return Results.Ok(new { rc = worst, lines = results.Count, accepted = results.Count(r => r.Rc is "0000" or "0004"),
                                    results = results.Select(r => new { line = r.Line, rc = r.Rc, message = ReturnCodes[r.Rc], planId = r.Plan }) });
        }).WithTags("Mainframe").WithSummary("Recibir saldos heredados (text/plain, una línea BL de 80 columnas por plan)");

        app.MapGet("/api/v1/legacy-balances/{planId}", (HttpContext http, string planId) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin", "Service", "Advisor" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Plan, string Bal, string AsOf, string At)>("SELECT plan_id, balance, as_of, received_at FROM balances WHERE plan_id = @planId", new { planId });
            return r.Plan is null ? Rc(http, 404, "0044", $"El mainframe no ha mandado saldo del plan {planId}.")
                : Results.Ok(new { planId = r.Plan, balance = new { amount = r.Bal, currency = "SEK" }, asOf = r.AsOf, receivedAt = r.At, source = "MAINFRAME" });
        }).WithTags("Mainframe").WithSummary("Saldo heredado de un plan");
    }
}

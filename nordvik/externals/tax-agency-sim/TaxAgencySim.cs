using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FilingLine(string? PersonalNumber, string? IncomeType, string? Gross, string? TaxWithheld);
public sealed record FilingRequest(string? Reporter, int? Year, string? Kind, FilingLine[]? Lines);
public sealed record RejectNextRequest(int? Count, string? Reason);

/// <summary>
/// tax-agency-sim · sistema externo E8 (simulado): la Agencia Tributaria. Con API key (X-Api-Key) da la tabla de retención de cada persona
/// (municipio y tipo, deterministas por número nacional) y recibe los ficheros anuales de control (KU) devolviendo un acuse con las líneas
/// aceptadas y rechazadas. Un mismo fichero (reporter, año, tipo) solo se acepta una vez salvo que sea una corrección.
/// </summary>
public sealed class TaxAgencySim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "tax-agency-sim", "X-external", 7708, 4, "Sistema externo (simulado)",
        "E8 Agencia Tributaria: tablas de retención por número nacional (con API key) y ficheros anuales de control con acuse línea a línea.",
        DependsOn: new[] { "clock-service" });

    public const string ApiKey = "e8-skatteverket-key-2026";
    public static readonly (string Municipality, decimal Rate)[] Municipalities =
    {
        ("Stockholm", 29.82m), ("Göteborg", 32.60m), ("Malmö", 32.42m), ("Uppsala", 32.85m), ("Kiruna", 33.75m), ("Lund", 31.82m), ("Umeå", 34.45m), ("Visby", 33.60m),
    };

    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, object> _filings = new();
    private readonly ConcurrentDictionary<string, string> _accepted = new();
    private int _rejectNext; private string _rejectReason = "SCHEMA_ERROR";

    public static (string Municipality, decimal Rate, int Table) TableFor(string normalizedPnr)
    {
        var h = Canonical.Sha256Hex(normalizedPnr);
        var m = Municipalities[Convert.ToInt32(h[..2], 16) % Municipalities.Length];
        return (m.Municipality, m.Rate, (int)Math.Round(m.Rate));
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new { error = code, message }, statusCode: status);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();
        bool KeyOk(HttpContext http) => http.Request.Headers["X-Api-Key"].FirstOrDefault() == ApiKey;

        app.MapGet("/v1/tax-tables/{personalNumber}", async (HttpContext http, string personalNumber, int? year) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (!KeyOk(http)) return Error(401, "INVALID_API_KEY", "X-Api-Key missing or wrong");
            var c = NationalId.Check(personalNumber);
            if (!c.Valid) return Error(400, "INVALID_PERSONAL_NUMBER", c.Reason ?? "invalid");
            var t = TableFor(c.Normalized!);
            return Results.Ok(new { personalNumber = c.Normalized, year = year ?? clock.Today.Year, municipality = t.Municipality, table = t.Table, ratePct = t.Rate.ToString("F2", Money.Inv) });
        }).WithTags("Agencia Tributaria v1").WithSummary("Tabla de retención de una persona para un año");

        app.MapPost("/v1/filings", async (HttpContext http, FilingRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (!KeyOk(http)) return Error(401, "INVALID_API_KEY", "X-Api-Key missing or wrong");
            if (string.IsNullOrWhiteSpace(b.Reporter) || b.Year is null || b.Lines is null) return Error(400, "SCHEMA_ERROR", "reporter, year and lines are required");
            var kind = (b.Kind ?? "ORIGINAL").ToUpperInvariant();
            var key = $"{b.Reporter}/{b.Year}";
            if (kind == "ORIGINAL" && _accepted.ContainsKey(key)) return Error(409, "DUPLICATE_FILING", $"An original filing for {key} was already accepted: {_accepted[key]}. Send a CORRECTION.");
            if (Interlocked.Decrement(ref _rejectNext) >= 0) return Error(422, _rejectReason, "The whole file was rejected (simulated)");
            Interlocked.Exchange(ref _rejectNext, 0);
            var rejected = new List<object>(); var accepted = 0;
            for (var i = 0; i < b.Lines.Length; i++)
            {
                var l = b.Lines[i];
                var c = NationalId.Check(l.PersonalNumber);
                string? code = !c.Valid ? "INVALID_PERSONAL_NUMBER" : !Money.TryParse(l.Gross, out var g) || g < 0 ? "INVALID_AMOUNT"
                    : !Money.TryParse(l.TaxWithheld, out var t) || t < 0 || t > g ? "INVALID_TAX" : null;
                if (code is null) accepted++; else rejected.Add(new { line = i + 1, code });
            }
            var id = "skv_" + Guid.NewGuid().ToString("N")[..12];
            var receipt = new { receiptId = id, reporter = b.Reporter, year = b.Year, kind, receivedAt = clock.UtcNow.ToString("O"), lines = b.Lines.Length, accepted, rejected };
            _filings[id] = receipt;
            _accepted[key] = id;
            return Results.Ok(receipt);
        }).WithTags("Agencia Tributaria v1").WithSummary("Entregar un fichero anual de control (ORIGINAL o CORRECTION)");

        app.MapGet("/__admin/filings", () => Results.Ok(new { items = _filings.Values, count = _filings.Count })).WithTags("Administración del simulador").WithSummary("Ficheros recibidos");
        app.MapPost("/__admin/reject-next", (RejectNextRequest b) =>
        {
            Interlocked.Exchange(ref _rejectNext, Math.Clamp(b.Count ?? 1, 0, 10));
            _rejectReason = b.Reason ?? "SCHEMA_ERROR";
            return Results.Ok(new { rejectNext = _rejectNext, reason = _rejectReason });
        }).WithTags("Administración del simulador").WithSummary("Rechazar enteros los próximos N ficheros");
        app.MapPost("/__admin/reset", () => { _filings.Clear(); _accepted.Clear(); Interlocked.Exchange(ref _rejectNext, 0); return Results.Ok(new { reset = true }); })
            .WithTags("Administración del simulador").WithSummary("Olvidar los ficheros recibidos");
    }
}

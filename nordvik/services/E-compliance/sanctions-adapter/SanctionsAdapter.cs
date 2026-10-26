using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ScreeningRequest(string? CustomerId, string? Name, string? BirthDate, string? Country, string[]? Lists);
public sealed record DecisionRequest(string? Decision, string? Reason);
public sealed record Screening(string Id, string? CustomerId, string? SubjectRef, string Name, string? BirthDate, string Status, int Threshold, bool Partial, string[] ScreenedLists,
    JsonElement Matches, int IgnoredBelowThreshold, string? Decision, string? DecisionReason, string? DecidedBy, string CreatedAt, string CreatedBy);

/// <summary>
/// sanctions-adapter: traduce Nordvik ↔ SanctionScreen (E3). Pide la API key a secrets-service (y si el proveedor la rechaza por rotación,
/// la vuelve a pedir una vez), nunca manda el número nacional (solo nombre y fecha de nacimiento), filtra por el umbral de config-service
/// y deja la decisión final a Cumplimiento. Una respuesta parcial NUNCA es CLEAR: es INCOMPLETE.
/// </summary>
public sealed class SanctionsAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "sanctions-adapter", "E-compliance", 6401, 3, "Cumplimiento normativo",
        "Cribado de sanciones y PEP contra SanctionScreen (E3): umbral configurable, respuestas parciales = INCOMPLETE, decisión de Cumplimiento sobre posibles coincidencias.",
        DependsOn: new[] { "sanctionscreen-sim", "secrets-service", "customer-registry", "config-service", "clock-service" },
        Emits: new[] { "screening.completed", "screening.decided" });

    private static string? _apiKey;
    private static readonly SemaphoreSlim KeyGate = new(1, 1);

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS screenings (id TEXT PRIMARY KEY, customer_id TEXT, name TEXT, birth_date TEXT, status TEXT, threshold INTEGER, partial INTEGER, lists TEXT, matches TEXT,
                        ignored INTEGER, decision TEXT, decision_reason TEXT, decided_by TEXT, created_at TEXT, created_by TEXT)");
    }

    private static async Task<string> ApiKey(IClock clock, bool refresh)
    {
        await KeyGate.WaitAsync();
        try
        {
            if (_apiKey is not null && !refresh) return _apiKey;
            var r = await Downstream.Send(null, "secrets-service", HttpMethod.Get, "/api/v1/secrets/sanctionscreen-api-key", forwardAuth: false, headers: await ServiceToken.Header(clock));
            if (!r.Ok) throw new DependencyUnavailableException("secrets-service", "secrets-service no dio la API key: " + r.Status);
            return _apiKey = r.Str("value")!;
        }
        finally { KeyGate.Release(); }
    }

    private static Screening? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string? Cust, string Name, string? Birth, string Status, long Thr, long Partial, string Lists, string Matches, long Ignored,
            string? Decision, string? Reason, string? By, string Created, string CreatedBy)>(
            "SELECT id, customer_id, name, birth_date, status, threshold, partial, lists, matches, ignored, decision, decision_reason, decided_by, created_at, created_by FROM screenings WHERE id = @id", new { id });
        if (r.Id is null) return null;
        return new Screening(r.Id, r.Cust, r.Cust is null ? null : "tok_cu_" + Canonical.Sha256Hex(r.Cust)[..6], r.Name, r.Birth, r.Status, (int)r.Thr, r.Partial == 1,
            JsonSerializer.Deserialize<string[]>(r.Lists)!, JsonDocument.Parse(r.Matches).RootElement, (int)r.Ignored, r.Decision, r.Reason, r.By, r.Created, r.CreatedBy);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] screeners = { "Advisor", "Operations", "Compliance", "Admin", "Service" };

        app.MapPost("/api/v1/screenings", async (HttpContext http, ScreeningRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: screeners) is { } denied) return denied;
            string? name = b.Name, birth = b.BirthDate;
            if (b.CustomerId is not null)
            {
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
                if (c.Status == 404) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}.", null);
                name ??= $"{c.Str("firstName")} {c.Str("lastName")}";
                birth ??= c.Str("birthDate");
            }
            if (string.IsNullOrWhiteSpace(name))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Hace falta customerId o name.", "Ejemplo: { \"customerId\": \"c000...\" } o { \"name\": \"Viktor Draganov\", \"birthDate\": \"1961-04-12\" }", new[] { new FieldError("name", "obligatorio sin customerId") });
            if (birth is not null && !Money.TryParseDate(birth, out _)) return Problems.BadRequest(http, "VALIDATION_FAILED", "birthDate en formato AAAA-MM-DD.", null, new[] { new FieldError("birthDate", "AAAA-MM-DD") });
            var threshold = await Config.GetInt(http, "sanctions-adapter", "matchThreshold", 85);
            var body = new { name, birthDate = birth, country = b.Country, lists = b.Lists };
            DownstreamReply r;
            try
            {
                r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("sanctionscreen-sim") + "/v2/screen", body, new Dictionary<string, string> { ["X-Api-Key"] = await ApiKey(clock, false) }, TimeSpan.FromSeconds(5));
                if (r.Status == 401)
                    r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("sanctionscreen-sim") + "/v2/screen", body, new Dictionary<string, string> { ["X-Api-Key"] = await ApiKey(clock, true) }, TimeSpan.FromSeconds(5));
            }
            catch (DependencyUnavailableException)
            {
                return Problems.Of(http, 504, "SCREENING_TIMEOUT", "El proveedor no respondió", "SanctionScreen no respondió en 5 s.", "No des por limpio a un cliente sin cribar: reintenta más tarde.");
            }
            if (r.Status is 500 or 503) return Problems.Of(http, 503, "SCREENING_PROVIDER_DOWN", "Proveedor no disponible", $"SanctionScreen respondió {r.Status}.", "El cribado queda pendiente; no apruebes el KYC sin él.");
            if (!r.Ok) return Problems.Of(http, 502, "SCREENING_PROVIDER_ERROR", "Error del proveedor", $"SanctionScreen respondió {r.Status}: {r.Raw}", null);
            var all = r.Json.GetProperty("hits").EnumerateArray().ToList();
            var kept = all.Where(h => h.GetProperty("score").GetInt32() >= threshold).ToList();
            var partial = r.Json.GetProperty("partial").GetBoolean();
            var status = kept.Count > 0 ? "POTENTIAL_MATCH" : partial ? "INCOMPLETE" : "CLEAR";
            var id = "scr_" + Guid.NewGuid().ToString("N")[..16];
            var lists = r.Json.GetProperty("screenedLists").GetRawText();
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO screenings VALUES (@id, @cust, @name, @birth, @status, @threshold, @partial, @lists, @matches, @ignored, NULL, NULL, NULL, @at, @by)",
                new { id, cust = b.CustomerId, name, birth, status, threshold, partial = partial ? 1 : 0, lists, matches = JsonSerializer.Serialize(kept), ignored = all.Count - kept.Count, at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "screening.completed", id, new { screeningId = id, customerId = b.CustomerId, status, maxScore = kept.Count == 0 ? 0 : kept.Max(h => h.GetProperty("score").GetInt32()) }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/screenings/{id}", Load(db, id));
        }).WithTags("Cribado").WithSummary("Cribar a un cliente (por customerId) o un nombre suelto contra sanciones y PEP").Produces<Screening>(201);

        app.MapGet("/api/v1/screenings/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: screeners) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id) is { } s ? Results.Ok(s) : Problems.NotFound(http, "SCREENING_NOT_FOUND", $"No existe el cribado {id}.", null);
        }).WithTags("Cribado").WithSummary("Un cribado").Produces<Screening>();

        app.MapGet("/api/v1/screenings", (HttpContext http, string? customerId, string? status) =>
        {
            if (Auth.Require(http, clock, out _, roles: screeners) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM screenings WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@status IS NULL OR status = @status) ORDER BY created_at DESC, id",
                new { customerId, status }).Select(i => Load(db, i)!).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Cribado").WithSummary("Cribados (el más reciente primero)");

        app.MapPost("/api/v1/screenings/{id}/decision", (HttpContext http, string id, DecisionRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Compliance" }) is { } denied) return denied;
            var decision = b.Decision?.ToUpperInvariant();
            if (decision is not ("FALSE_POSITIVE" or "TRUE_MATCH") || string.IsNullOrWhiteSpace(b.Reason))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "decision (FALSE_POSITIVE o TRUE_MATCH) y reason son obligatorios.", null);
            using var db = ctx.OpenDb();
            var s = Load(db, id);
            if (s is null) return Problems.NotFound(http, "SCREENING_NOT_FOUND", $"No existe el cribado {id}.", null);
            if (s.Status != "POTENTIAL_MATCH") return Problems.Conflict(http, "NOTHING_TO_DECIDE", $"El cribado está {s.Status}: solo se decide sobre POTENTIAL_MATCH.", null);
            var status = decision == "TRUE_MATCH" ? "CONFIRMED_MATCH" : "CLEARED_FALSE_POSITIVE";
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE screenings SET status = @status, decision = @decision, decision_reason = @r, decided_by = @by WHERE id = @id", new { status, decision, r = b.Reason, by = p.Sub, id }, tx);
            Outbox.Add(db, http, clock, "screening.decided", id, new { screeningId = id, customerId = s.CustomerId, status, decidedBy = p.Sub }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Cribado").WithSummary("Cumplimiento decide sobre una posible coincidencia (falso positivo o coincidencia real)");
    }
}

using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ConsentRequest(string? CustomerId, string? Purpose, bool? Granted, string? Channel, string? Text);
public sealed record TamperRequest(string? EntryId);
public sealed record ConsentEntry(string Id, int Seq, string CustomerId, string Purpose, bool Granted, string Channel, string? Text, string RecordedBy, string RecordedAt, string PrevHash, string Hash);
public sealed record ConsentState(string Purpose, bool Granted, string Source, string? Since, string? EntryId);

/// <summary>
/// consent-ledger: libro de consentimientos solo de añadir. Cada cambio es una entrada nueva encadenada con la anterior
/// (hash de la anterior + hash propio), así que nadie puede reescribir el pasado sin que /verify lo detecte.
/// Sin entrada, un propósito está denegado por defecto (privacidad por defecto, art. 25 RGPD).
/// </summary>
public sealed class ConsentLedger : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "consent-ledger", "F-privacy", 6502, 3, "Privacidad y protección de datos",
        "Consentimientos por propósito en un libro encadenado por hash: estado actual, historial completo y verificación de integridad. Denegado por defecto.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "consent.granted", "consent.withdrawn" });

    public static readonly string[] Purposes = { "MARKETING", "ANALYTICS", "PROFILING", "THIRD_PARTY_SHARING", "VOICE_RECORDING", "ESG_PROFILING" };
    public static readonly string[] Channels = { "WEB", "APP", "ADVISOR", "VOICE", "PAPER" };
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS entries (id TEXT PRIMARY KEY, seq INTEGER, customer_id TEXT, purpose TEXT, granted INTEGER, channel TEXT, text TEXT, recorded_by TEXT,
                        recorded_at TEXT, prev_hash TEXT, hash TEXT, UNIQUE (customer_id, seq))");
    }

    public static string HashOf(string prevHash, int seq, string customerId, string purpose, bool granted, string channel, string? text, string by, string at) =>
        Canonical.Hash(new { prevHash, seq, customerId, purpose, granted, channel, text, recordedBy = by, recordedAt = at });

    private static List<ConsentEntry> History(Microsoft.Data.Sqlite.SqliteConnection db, string customerId) =>
        db.Query<(string Id, long Seq, string Cust, string Purpose, long Granted, string Channel, string? Text, string By, string At, string Prev, string Hash)>(
            "SELECT id, seq, customer_id, purpose, granted, channel, text, recorded_by, recorded_at, prev_hash, hash FROM entries WHERE customer_id = @customerId ORDER BY seq", new { customerId })
          .Select(r => new ConsentEntry(r.Id, (int)r.Seq, r.Cust, r.Purpose, r.Granted == 1, r.Channel, r.Text, r.By, r.At, r.Prev, r.Hash)).ToList();

    private static List<ConsentState> State(List<ConsentEntry> history) =>
        Purposes.Select(pu =>
        {
            var last = history.LastOrDefault(e => e.Purpose == pu);
            return last is null ? new ConsentState(pu, false, "DEFAULT", null, null) : new ConsentState(pu, last.Granted, "CUSTOMER_CHOICE", last.RecordedAt, last.Id);
        }).ToList();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/consents", async (HttpContext http, ConsentRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var errors = new List<FieldError>();
            var customerId = b.CustomerId ?? p.CustomerId;
            var purpose = b.Purpose?.ToUpperInvariant();
            var channel = (b.Channel ?? (p.Role == "Customer" ? "WEB" : "ADVISOR")).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(customerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (purpose is null || !Purposes.Contains(purpose)) errors.Add(new FieldError("purpose", "uno de " + string.Join(", ", Purposes)));
            if (b.Granted is null) errors.Add(new FieldError("granted", "true o false"));
            if (!Channels.Contains(channel)) errors.Add(new FieldError("channel", "uno de " + string.Join(", ", Channels)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Consentimiento no válido.", "Ejemplo: { \"customerId\": \"c000...\", \"purpose\": \"MARKETING\", \"granted\": true, \"channel\": \"APP\" }", errors);
            if (await CustomerGuard.Check(http, p, "preferences:write", customerId!) is { } g) return g;
            using var db = ctx.OpenDb();
            var history = History(db, customerId!);
            var current = State(history).First(s => s.Purpose == purpose);
            if (current.Source != "DEFAULT" && current.Granted == b.Granted)
                return Results.Ok(new { changed = false, state = current, entry = history.First(e => e.Id == current.EntryId) });
            var seq = history.Count + 1;
            var prev = history.Count == 0 ? Genesis : history[^1].Hash;
            var at = Nordic.Iso(clock.LocalNow);
            var id = "cns_" + Guid.NewGuid().ToString("N")[..16];
            var hash = HashOf(prev, seq, customerId!, purpose!, b.Granted!.Value, channel, b.Text, p.Sub, at);
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO entries VALUES (@id, @seq, @customerId, @purpose, @g, @channel, @Text, @by, @at, @prev, @hash)",
                new { id, seq, customerId, purpose, g = b.Granted.Value ? 1 : 0, channel, b.Text, by = p.Sub, at, prev, hash }, tx);
            Outbox.Add(db, http, clock, b.Granted.Value ? "consent.granted" : "consent.withdrawn", customerId!, new { customerId, purpose, channel, entryId = id }, tx);
            tx.Commit();
            var entry = History(db, customerId!).Last();
            return Results.Created($"/api/v1/customers/{customerId}/consents/history", new { changed = true, state = State(new List<ConsentEntry>(History(db, customerId!))).First(s => s.Purpose == purpose), entry });
        }).WithTags("Consentimientos").WithSummary("Dar o retirar un consentimiento (una entrada nueva en el libro; repetir el mismo estado no añade nada)");

        app.MapGet("/api/v1/customers/{customerId}/consents", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "preferences:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            return Results.Ok(new { customerId, consents = State(History(db, customerId)) });
        }).WithTags("Consentimientos").WithSummary("Estado actual de cada propósito (sin entrada = denegado por defecto)");

        app.MapGet("/api/v1/customers/{customerId}/consents/history", async (HttpContext http, string customerId, string? purpose) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "preferences:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = History(db, customerId).Where(e => purpose is null || e.Purpose == purpose.ToUpperInvariant()).ToList();
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Consentimientos").WithSummary("Historial completo y encadenado");

        app.MapGet("/api/v1/customers/{customerId}/consents/verify", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "preferences:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var prev = Genesis; int? brokenAt = null; string? reason = null;
            var history = History(db, customerId);
            foreach (var e in history)
            {
                if (e.PrevHash != prev) { brokenAt = e.Seq; reason = "prevHash no coincide con el hash de la entrada anterior"; break; }
                if (HashOf(e.PrevHash, e.Seq, e.CustomerId, e.Purpose, e.Granted, e.Channel, e.Text, e.RecordedBy, e.RecordedAt) != e.Hash) { brokenAt = e.Seq; reason = "el contenido de la entrada no corresponde a su hash"; break; }
                prev = e.Hash;
            }
            return Results.Ok(new { customerId, valid = brokenAt is null, entries = history.Count, brokenAt, reason, head = history.Count == 0 ? Genesis : history[^1].Hash });
        }).WithTags("Consentimientos").WithSummary("Verificar la cadena de hashes: ¿alguien ha reescrito el pasado?");

        app.MapGet("/api/v1/purposes", () => Results.Ok(new { purposes = Purposes, channels = Channels, @default = false })).WithTags("Consentimientos").WithSummary("Propósitos y canales");

        app.MapGet("/api/v1/internal/consents/check", (string customerId, string purpose) =>
        {
            using var db = ctx.OpenDb();
            var s = State(History(db, customerId)).FirstOrDefault(x => x.Purpose == purpose.ToUpperInvariant());
            return Results.Ok(new { customerId, purpose, granted = s?.Granted ?? false, source = s?.Source ?? "UNKNOWN_PURPOSE" });
        }).WithTags("Interno").WithSummary("(interno) ¿Tiene el cliente este consentimiento?");

        app.MapPost("/api/v1/admin/tamper", (HttpContext http, TamperRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE entries SET granted = 1 - granted WHERE id = @id", new { id = b.EntryId });
            return n == 0 ? Problems.NotFound(http, "ENTRY_NOT_FOUND", $"No existe la entrada {b.EntryId}.", null)
                          : Results.Ok(new { tampered = b.EntryId, note = "Entrada alterada SIN recalcular su hash. Comprueba que /verify lo detecta." });
        }).WithTags("Laboratorio").WithSummary("(laboratorio, Admin) Alterar una entrada a escondidas para probar que /verify lo detecta");
    }
}

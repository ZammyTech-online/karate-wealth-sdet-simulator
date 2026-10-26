using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ArchiveRequest(string? SourceService, string? DocumentType, string? Content, string? CustomerId, int? RetentionYears, string? Reference);
public sealed record ArchiveTamperRequest(string? RecordId);

/// <summary>
/// archive-service: el archivo legal inmutable (WORM). Cada registro guarda el SHA-256 de su contenido y el hash del registro anterior:
/// una cadena. No se puede cambiar ni borrar (405/409 ARCHIVE_IMMUTABLE); solo se purga lo que ya pasó su fecha de retención (mínimo 10 años).
/// /verify recorre la cadena y dice dónde se rompió si alguien manipuló la base de datos (/api/v1/admin/tamper lo simula).
/// </summary>
public sealed class ArchiveService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "archive-service", "H-contracts", 6707, 4, "Contratos y documentos",
        "Archivo legal inmutable de 10 años: cadena de hashes, verificación de integridad, prohibido modificar o borrar y purga solo tras la retención.",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "archive.record-added" });

    public const int MinRetentionYears = 10;
    private const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string ChainHash(long seq, string contentHash, string prev, string archivedAt) => Canonical.Sha256Hex($"{seq}|{contentHash}|{prev}|{archivedAt}");

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS records (seq INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT UNIQUE, source TEXT, doc_type TEXT, customer_id TEXT, reference TEXT, content TEXT,
                        content_hash TEXT, prev_hash TEXT, hash TEXT, archived_at TEXT, retain_until TEXT, purged INTEGER DEFAULT 0)");
    }

    private static object? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id, bool withContent)
    {
        var r = db.QuerySingleOrDefault<(long Seq, string Id, string Src, string Type, string? Cust, string? Ref, string? Content, string Ch, string Prev, string Hash, string At, string Until, long Purged)>(
            "SELECT seq, id, source, doc_type, customer_id, reference, content, content_hash, prev_hash, hash, archived_at, retain_until, purged FROM records WHERE id = @id", new { id });
        return r.Id is null ? null : new
        {
            id = r.Id, seq = r.Seq, sourceService = r.Src, documentType = r.Type, customerId = r.Cust, reference = r.Ref, content = withContent && r.Purged == 0 ? r.Content : null,
            contentSha256 = r.Ch, previousHash = r.Prev, hash = r.Hash, archivedAt = r.At, retainUntil = r.Until, purged = r.Purged == 1,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        string[] writers = { "Service", "Operations", "Compliance", "Admin" };
        string[] readers = { "Compliance", "Admin", "Operations" };
        var gate = new object();

        app.MapPost("/api/v1/archive", (HttpContext http, ArchiveRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: writers) is { } denied) return denied;
            var years = b.RetentionYears ?? MinRetentionYears;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.SourceService)) errors.Add(new FieldError("sourceService", "obligatorio"));
            if (string.IsNullOrWhiteSpace(b.DocumentType)) errors.Add(new FieldError("documentType", "obligatorio"));
            if (string.IsNullOrEmpty(b.Content)) errors.Add(new FieldError("content", "obligatorio"));
            else if (Encoding.UTF8.GetByteCount(b.Content) > 512_000) errors.Add(new FieldError("content", "como mucho 500 KB"));
            if (years < MinRetentionYears || years > 100) errors.Add(new FieldError("retentionYears", $"entre {MinRetentionYears} y 100"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Registro no válido.", null, errors);
            var id = "arc_" + Guid.NewGuid().ToString("N")[..12];
            using var db = ctx.OpenDb();
            lock (gate)
            {
                var prev = db.QuerySingleOrDefault<(long Seq, string Hash)>("SELECT seq, hash FROM records ORDER BY seq DESC LIMIT 1");
                var seq = prev.Seq + 1;
                var at = clock.UtcNow.ToString("O");
                var ch = Canonical.Sha256Hex(b.Content!);
                var ph = prev.Hash ?? Genesis;
                using var tx = db.BeginTransaction();
                db.Execute("INSERT INTO records (seq, id, source, doc_type, customer_id, reference, content, content_hash, prev_hash, hash, archived_at, retain_until) VALUES (@seq, @id, @SourceService, @DocumentType, @CustomerId, @Reference, @Content, @ch, @ph, @h, @at, @until)",
                    new { seq, id, b.SourceService, b.DocumentType, b.CustomerId, b.Reference, b.Content, ch, ph, h = ChainHash(seq, ch, ph, at), at, until = Money.Date(clock.Today.AddYears(years)) }, tx);
                Outbox.Add(db, http, clock, "archive.record-added", id, new { recordId = id, seq, documentType = b.DocumentType }, tx);
                tx.Commit();
            }
            return Results.Created($"/api/v1/archive/{id}", Load(db, id, false));
        }).WithTags("Archivo").WithSummary("Archivar un documento (queda encadenado e inmutable)");

        app.MapGet("/api/v1/archive/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _, roles: readers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            return Load(db, id, true) is { } x ? Results.Ok(x) : Problems.NotFound(http, "RECORD_NOT_FOUND", $"No existe el registro {id}.", null);
        }).WithTags("Archivo").WithSummary("Un registro con su contenido y sus hashes");

        app.MapMethods("/api/v1/archive/{id}", new[] { "PUT", "PATCH", "DELETE" }, (HttpContext http, string id) =>
            Problems.Of(http, 405, "ARCHIVE_IMMUTABLE", "Archivo inmutable", "Lo archivado no se cambia ni se borra (WORM).", "Si hay que corregir algo, archiva un registro nuevo que lo referencie."))
            .WithTags("Archivo").WithSummary("Siempre 405: el archivo es inmutable");

        app.MapGet("/api/v1/archive", (HttpContext http, string? customerId, string? documentType) =>
        {
            if (Auth.Require(http, clock, out _, roles: readers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT id FROM records WHERE (@customerId IS NULL OR customer_id = @customerId) AND (@documentType IS NULL OR doc_type = @documentType) ORDER BY seq",
                new { customerId, documentType }).Select(i => Load(db, i, false)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Archivo").WithSummary("Buscar registros (sin contenido)");

        app.MapGet("/api/v1/archive-verify", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: readers) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var rows = db.Query<(long Seq, string Id, string? Content, string Ch, string Prev, string Hash, string At, long Purged)>("SELECT seq, id, content, content_hash, prev_hash, hash, archived_at, purged FROM records ORDER BY seq").ToList();
            var expectedPrev = Genesis;
            foreach (var r in rows)
            {
                string? problem = r.Prev != expectedPrev ? "PREVIOUS_HASH_MISMATCH"
                    : ChainHash(r.Seq, r.Ch, r.Prev, r.At) != r.Hash ? "RECORD_HASH_MISMATCH"
                    : r.Purged == 0 && Canonical.Sha256Hex(r.Content ?? "") != r.Ch ? "CONTENT_MODIFIED" : null;
                if (problem is not null) return Results.Ok(new { valid = false, records = rows.Count, brokenAt = new { seq = r.Seq, id = r.Id, problem } });
                expectedPrev = r.Hash;
            }
            return Results.Ok(new { valid = true, records = rows.Count, head = rows.LastOrDefault().Hash ?? Genesis, brokenAt = (object?)null });
        }).WithTags("Archivo").WithSummary("Verificar toda la cadena (dónde se rompió, si se rompió)");

        app.MapPost("/api/v1/archive-purge", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE records SET content = NULL, purged = 1 WHERE purged = 0 AND retain_until < @today", new { today = Money.Date(clock.Today) });
            return Results.Ok(new { purged = n, asOf = Money.Date(clock.Today), note = "Se borra el contenido; el eslabón (hashes) se queda para que la cadena siga verificable." });
        }).WithTags("Archivo").WithSummary("Purgar lo que ya pasó su retención (Admin)");

        app.MapPost("/api/v1/admin/tamper", (HttpContext http, ArchiveTamperRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var n = db.Execute("UPDATE records SET content = content || ' (manipulado)' WHERE id = @RecordId AND purged = 0", new { b.RecordId });
            return n == 0 ? Problems.NotFound(http, "RECORD_NOT_FOUND", "No existe ese registro.", null)
                : Results.Ok(new { tampered = b.RecordId, note = "Simulación: alguien cambió el contenido directamente en la base de datos. Ahora /api/v1/archive-verify lo detecta." });
        }).WithTags("Administración").WithSummary("Simular una manipulación directa en la base de datos (Admin)");
    }
}

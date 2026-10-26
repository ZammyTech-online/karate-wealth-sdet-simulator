using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateContractRequest(string? Type, string? CustomerId, string? AccountType, Dictionary<string, JsonElement>? Facts);
public sealed record SigningSessionRequest(string? Party);
public sealed record InternalSignature(string? Party, string? SessionId, string? SignedHash, int? TemplateVersion, string? Signature, string? SignedAt, string? Signer);
public sealed record AmendRequest(string? AccountType, string? Reason);
public sealed record ReasonRequest(string? Reason);
public sealed record JsonAnnexRequest(string? Kind, JsonElement? Content);
public sealed record ContractParty(string Party, bool Required, bool Signed);
public sealed record ContractSignature(string Party, string SignedAt, string SessionId, string SignedHash, int TemplateVersion, string Signer);
public sealed record ContractAnnex(string DocumentId, string Kind, string ContentHash, string MimeType, long Size);
public sealed record Contract(string Id, string Type, int TemplateVersion, string Status, string CustomerRef, string? AccountType, string Language, IReadOnlyList<ContractParty> Parties,
    IReadOnlyList<ContractSignature> Signatures, IReadOnlyList<ContractAnnex> Annexes, JsonElement Content, string ContentHash, int Version, string CreatedAt, string CreatedBy,
    string? SentAt, string? ExpiresAt, string? ActivatedAt, string? WithdrawalDeadline, string? Amends, string? AmendedBy, string? InvalidReason, string? ClosedAt);
public sealed record ContractPage(IReadOnlyList<Contract> Items, string? NextCursor, int Limit);

/// <summary>
/// contract-service (API v2): contratos generados desde plantilla + cláusulas condicionales, con anexos en document-vault,
/// firma de varias partes con NordID (14 días de plazo), activación, desistimiento (14 días), enmiendas que nunca editan el original,
/// PDF de un solo uso e integridad por contentHash (SHA-256 del JSON canónico). Nueve estados:
/// DRAFT → PENDING_SIGNATURE → PARTIALLY_SIGNED → ACTIVE → (WITHDRAWN | SUPERSEDED | TERMINATED) · EXPIRED · INVALID_SIGNATURE.
/// </summary>
public sealed class ContractService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "contract-service", "H-contracts", 6700, 2, "Contratos y documentos",
        "Contratos con plantilla versionada, cláusulas condicionales, anexos, firma múltiple con NordID, enmiendas, desistimiento, PDF de un solo uso y verificación de integridad.",
        "2.0.0",
        DependsOn: new[] { "contract-template-service", "clause-engine", "document-vault", "antivirus-scan", "render-service", "signing-service", "customer-registry", "permission-service", "event-bus", "clock-service" },
        Emits: new[] { "contract.created", "contract.sent", "contract.signed", "contract.activated", "contract.invalidated", "contract.withdrawn", "contract.expired", "contract.superseded", "contract.terminated" });

    private static readonly string[] Types = { "MASTER_AGREEMENT", "ADVISORY_MANDATE", "DISCRETIONARY_MANDATE", "RECORDING_CONSENT" };
    private Timer? _timer;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS contracts (id TEXT PRIMARY KEY, seq INTEGER, type TEXT, template_version INTEGER, status TEXT, customer_id TEXT, customer_ref TEXT, account_type TEXT,
                        language TEXT, parties TEXT, content TEXT, content_hash TEXT, version INTEGER, created_at TEXT, created_by TEXT, sent_at TEXT, expires_ticks INTEGER, expires_at TEXT,
                        activated_at TEXT, deadline_ticks INTEGER, deadline TEXT, amends TEXT, amended_by TEXT, invalid_reason TEXT, closed_at TEXT);
                     CREATE TABLE IF NOT EXISTS signatures (contract_id TEXT, party TEXT, signed_at TEXT, session_id TEXT, signed_hash TEXT, template_version INTEGER, signature TEXT, signer TEXT, PRIMARY KEY (contract_id, party));
                     CREATE TABLE IF NOT EXISTS annexes (contract_id TEXT, document_id TEXT, kind TEXT, content_hash TEXT, mime TEXT, size INTEGER, seq INTEGER)");
    }

    // ---------- lectura ----------
    public static Contract? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Type, long Tv, string Status, string Ref, string? Acc, string Lang, string Parties, string Content, string Hash, long Version,
            string Created, string By, string? Sent, string? Expires, string? Activated, string? Deadline, string? Amends, string? AmendedBy, string? Invalid, string? Closed)>(
            @"SELECT id, type, template_version, status, customer_ref, account_type, language, parties, content, content_hash, version, created_at, created_by, sent_at, expires_at,
                     activated_at, deadline, amends, amended_by, invalid_reason, closed_at FROM contracts WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var sigs = db.Query<(string Party, string At, string Session, string Hash, long Tv, string Signer)>(
            "SELECT party, signed_at, session_id, signed_hash, template_version, signer FROM signatures WHERE contract_id = @id ORDER BY signed_at", new { id })
            .Select(s => new ContractSignature(s.Party, s.At, s.Session, s.Hash, (int)s.Tv, s.Signer)).ToList();
        var annexes = db.Query<(string Doc, string Kind, string Hash, string Mime, long Size)>("SELECT document_id, kind, content_hash, mime, size FROM annexes WHERE contract_id = @id ORDER BY seq", new { id })
            .Select(a => new ContractAnnex(a.Doc, a.Kind, a.Hash, a.Mime, a.Size)).ToList();
        var parties = JsonSerializer.Deserialize<List<string>>(r.Parties)!.Select(p => new ContractParty(p, true, sigs.Any(s => s.Party == p))).ToList();
        return new Contract(r.Id, r.Type, (int)r.Tv, r.Status, r.Ref, r.Acc, r.Lang, parties, sigs, annexes, JsonDocument.Parse(r.Content).RootElement, r.Hash, (int)r.Version,
            r.Created, r.By, r.Sent, r.Expires, r.Activated, r.Deadline, r.Amends, r.AmendedBy, r.Invalid, r.Closed);
    }

    private static string? CustomerOf(Microsoft.Data.Sqlite.SqliteConnection db, string id) => db.ExecuteScalar<string?>("SELECT customer_id FROM contracts WHERE id = @id", new { id });

    /// <summary>Caducidad perezosa: un contrato sin todas las firmas a los 14 días pasa a EXPIRED.</summary>
    private static void Expire(Microsoft.Data.Sqlite.SqliteConnection db, IClock clock)
    {
        foreach (var id in db.Query<string>("SELECT id FROM contracts WHERE status IN ('PENDING_SIGNATURE','PARTIALLY_SIGNED') AND expires_ticks <= @t", new { t = clock.UtcNow.UtcTicks }).ToList())
        {
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE contracts SET status = 'EXPIRED', closed_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, null, clock, "contract.expired", id, new { contractId = id }, tx);
            tx.Commit();
        }
    }

    private static string EmployeePersonalNumber(string sub)
    {
        var h = Canonical.Sha256Hex(sub);
        var birth = new DateOnly(1970, 1, 1).AddDays(int.Parse(h[..4], NumberStyles.HexNumber) % 9000);
        return NationalId.Create(birth, int.Parse(h[4..6], NumberStyles.HexNumber) % 100, false);
    }

    private static async Task<IResult?> Guard(HttpContext http, Principal p, Microsoft.Data.Sqlite.SqliteConnection db, string id, string action)
    {
        var cust = CustomerOf(db, id);
        if (cust is null) return Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null);
        var d = await Permissions.Check(http, p, action, cust);
        return d.Allowed ? null : Permissions.Denied(http, p, d, "CONTRACT_NOT_FOUND", $"el contrato {id}");
    }

    private static IResult Ok(HttpContext http, Contract c)
    {
        http.Response.Headers.ETag = $"\"{c.Version}\"";
        return Results.Ok(c);
    }

    private static void Recompute(Microsoft.Data.Sqlite.SqliteConnection db, string id, Microsoft.Data.Sqlite.SqliteTransaction? tx = null)
    {
        var content = JsonNode.Parse(db.ExecuteScalar<string>("SELECT content FROM contracts WHERE id = @id", new { id }, tx))!.AsObject();
        var annexes = new JsonArray();
        foreach (var a in db.Query<(string Doc, string Kind, string Hash)>("SELECT document_id, kind, content_hash FROM annexes WHERE contract_id = @id ORDER BY seq", new { id }, tx))
            annexes.Add(new JsonObject { ["documentId"] = a.Doc, ["kind"] = a.Kind, ["contentHash"] = a.Hash });
        content["annexes"] = annexes;
        var json = Canonical.OfJson(content.ToJsonString());
        db.Execute("UPDATE contracts SET content = @json, content_hash = @h, version = version + 1 WHERE id = @id", new { json, h = Canonical.Sha256Hex(json), id }, tx);
    }

    private static async Task<IResult> AddAnnex(HttpContext http, IClock clock, ServiceContext ctx, string id, Principal p, string kind, byte[] data, string mime, string? fileName, JsonElement? json)
    {
        using var db = ctx.OpenDb();
        var c = Load(db, id)!;
        if (c.Status != "DRAFT") return Problems.Conflict(http, "CONTRACT_NOT_EDITABLE", $"El contrato está {c.Status}: los anexos solo se añaden en DRAFT.", "Si ya está firmado, crea una enmienda.");
        if (data.Length > 5 * 1024 * 1024) return Problems.Of(http, 413, "FILE_TOO_LARGE", "Fichero demasiado grande", "Un anexo puede tener como mucho 5 MB.", null);
        if (mime is not ("application/pdf" or "application/json"))
            return Problems.Of(http, 415, "UNSUPPORTED_MEDIA_TYPE", "Tipo no admitido", $"Los anexos son application/pdf o application/json, no {mime}.", null);
        var scan = await Downstream.SendBytes(http, "antivirus-scan", "/api/v1/scans", data, mime, new Dictionary<string, string> { ["X-File-Name"] = fileName ?? "anexo" });
        if (!scan.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "antivirus-scan respondió " + scan.Status, null);
        if (scan.Str("verdict") == "INFECTED") return Problems.Unprocessable(http, "ANNEX_INFECTED", $"El antivirus detectó {scan.Str("signature")}: el anexo se rechaza.", null);
        if (scan.Json.GetProperty("mimeMismatch").GetBoolean())
            return Problems.Unprocessable(http, "ANNEX_MIME_MISMATCH", $"Se declaró {mime} pero el contenido es {scan.Str("detectedMime")}.", "El tipo se comprueba por los primeros bytes del fichero, no por la extensión.");
        var doc = await Downstream.Post(http, "document-vault", "/api/v1/documents", json is not null
            ? new { kind, content = json, contractId = id, customerId = CustomerOf(db, id), mimeType = mime }
            : (object)new { kind, contentBase64 = Convert.ToBase64String(data), mimeType = mime, fileName, contractId = id, customerId = CustomerOf(db, id) }, forwardAuth: false);
        if (!doc.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "document-vault respondió " + doc.Status, null);
        using var tx = db.BeginTransaction();
        var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM annexes WHERE contract_id = @id", new { id }, tx) ?? 0) + 1;
        db.Execute("INSERT INTO annexes VALUES (@id, @doc, @kind, @hash, @mime, @size, @seq)",
            new { id, doc = doc.Str("id"), kind, hash = doc.Str("contentHash"), mime, size = data.LongLength, seq }, tx);
        Recompute(db, id, tx);
        tx.Commit();
        var updated = Load(db, id)!;
        http.Response.Headers.ETag = $"\"{updated.Version}\"";
        return Results.Created($"/api/v2/contracts/{id}", updated);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();
        var g = app.MapGroup("/api/v2/contracts").WithTags("Contratos");

        g.MapPost("", async (HttpContext http, CreateContractRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:write");
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (b.Type is null || !Types.Contains(b.Type)) errors.Add(new FieldError("type", string.Join(", ", Types)));
            if (string.IsNullOrWhiteSpace(b.CustomerId)) errors.Add(new FieldError("customerId", "obligatorio"));
            if (b.AccountType is not (null or "ISK" or "KF" or "DEPOT")) errors.Add(new FieldError("accountType", "ISK, KF o DEPOT"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Contrato no válido.", "Ejemplo: { \"type\": \"MASTER_AGREEMENT\", \"customerId\": \"...\", \"accountType\": \"ISK\" }", errors);
            var guard = await CustomerGuard.Check(http, p, "contract:create", b.CustomerId!);
            if (guard is not null) return guard;
            var tpl = await Downstream.Get(http, "contract-template-service", $"/api/v1/contract-templates/{b.Type}/current", forwardAuth: false);
            var facts = new Dictionary<string, JsonElement>(b.Facts ?? new());
            if (b.AccountType is not null) facts["accountType"] = JsonSerializer.SerializeToElement(b.AccountType);
            var res = await Downstream.Post(http, "clause-engine", "/api/v1/clauses/resolve", new { templateType = b.Type, customerId = b.CustomerId, facts }, forwardAuth: false);
            if (!res.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "clause-engine respondió " + res.Status, null);
            var included = res.Json.GetProperty("included").EnumerateArray().Select(i => i.GetProperty("clauseNo").GetInt32()).ToHashSet();
            var customer = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
            var language = customer.Str("language") ?? tpl.Str("language") ?? "sv";
            var customerRef = "tok_cu_" + Canonical.Sha256Hex(b.CustomerId!)[..6];
            var content = new
            {
                title = tpl.Str("title"), templateType = b.Type, templateVersion = tpl.Json.GetProperty("version").GetInt32(), language, customerRef, accountType = b.AccountType,
                clauses = tpl.Json.GetProperty("clauses").EnumerateArray().Where(c => included.Contains(c.GetProperty("clauseNo").GetInt32()))
                    .Select(c => new { clauseNo = c.GetProperty("clauseNo").GetInt32(), code = c.GetProperty("code").GetString(), title = c.GetProperty("title").GetString(), text = c.GetProperty("text").GetString() }).ToList(),
                annexes = Array.Empty<object>(),
            };
            var json = Canonical.Of(content);
            var id = Guid.NewGuid().ToString();
            using var db = ctx.OpenDb();
            var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM contracts") ?? 0) + 1;
            using var tx = db.BeginTransaction();
            db.Execute(@"INSERT INTO contracts (id, seq, type, template_version, status, customer_id, customer_ref, account_type, language, parties, content, content_hash, version, created_at, created_by)
                         VALUES (@id, @seq, @Type, @tv, 'DRAFT', @CustomerId, @customerRef, @AccountType, @language, @parties, @json, @hash, 1, @at, @by)",
                new { id, seq, b.Type, tv = content.templateVersion, b.CustomerId, customerRef, b.AccountType, language, parties = JsonSerializer.Serialize(tpl.Json.GetProperty("parties")),
                      json, hash = Canonical.Sha256Hex(json), at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "contract.created", id, new { contractId = id, customerId = b.CustomerId, type = b.Type, templateVersion = content.templateVersion }, tx);
            tx.Commit();
            var c = Load(db, id)!;
            http.Response.Headers.ETag = $"\"{c.Version}\"";
            return Results.Created($"/api/v2/contracts/{id}", c);
        }).WithSummary("Crear un contrato desde la plantilla vigente (cláusulas según el cliente)").Produces<Contract>(201);

        g.MapGet("/{id}", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:read") is { } g2) return g2;
            Expire(db, clock);
            return Ok(http, Load(db, id)!);
        }).WithSummary("Un contrato (ETag = versión)").Produces<Contract>();

        g.MapGet("", async (HttpContext http, string? customerId, string? customerRef, string? status, int? limit, string? cursor) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            if (p.Role == "Customer") { customerId = p.CustomerId; customerRef = null; }
            else if (p.Role == "Advisor")
            {
                if (customerId is null) return Problems.BadRequest(http, "CUSTOMER_REQUIRED", "Un asesor tiene que filtrar por customerId.", null);
                var d = await Permissions.Check(http, p, "contract:read", customerId);
                if (!d.Allowed) return Permissions.Denied(http, p, d, "CUSTOMER_NOT_FOUND", $"el cliente {customerId}");
            }
            var size = Math.Clamp(limit ?? 20, 1, 100);
            long after = 0;
            if (cursor is not null)
            {
                try { after = long.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':')[1]); }
                catch { return Problems.BadRequest(http, "INVALID_CURSOR", "El cursor no es válido.", "Usa exactamente el nextCursor de la página anterior."); }
            }
            using var db = ctx.OpenDb();
            Expire(db, clock);
            var rows = db.Query<(string Id, long Seq)>(@"SELECT id, seq FROM contracts WHERE seq > @after AND (@customerId IS NULL OR customer_id = @customerId)
                                                        AND (@customerRef IS NULL OR customer_ref = @customerRef) AND (@status IS NULL OR status = @status) ORDER BY seq LIMIT @n",
                new { after, customerId, customerRef, status, n = size + 1 }).ToList();
            var items = rows.Take(size).Select(r => Load(db, r.Id)!).ToList();
            var next = rows.Count > size ? Convert.ToBase64String(Encoding.UTF8.GetBytes("seq:" + rows[size - 1].Seq)) : null;
            return Results.Ok(new ContractPage(items, next, size));
        }).WithSummary("Buscar contratos con paginación por cursor").Produces<ContractPage>();

        g.MapPost("/{id}/annexes", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:write");
            if (denied is not null) return denied;
            using (var db = ctx.OpenDb()) if (await Guard(http, p, db, id, "contract:create") is { } g2) return g2;
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                var kind = form["kind"].FirstOrDefault();
                if (file is null || string.IsNullOrWhiteSpace(kind))
                    return Problems.BadRequest(http, "VALIDATION_FAILED", "Hacen falta el fichero (file) y el tipo (kind).", "En Karate: * multipart file file = { read: 'anexo.pdf', contentType: 'application/pdf' } y * multipart field kind = 'FEE_SCHEDULE'");
                if (file.Length > 5 * 1024 * 1024) return Problems.Of(http, 413, "FILE_TOO_LARGE", "Fichero demasiado grande", "Un anexo puede tener como mucho 5 MB.", null);
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var data = ms.ToArray();
                var mime = (file.ContentType ?? "").Split(';')[0];
                JsonElement? json = null;
                if (mime == "application/json") { try { json = JsonDocument.Parse(data).RootElement; } catch { return Problems.Unprocessable(http, "ANNEX_MIME_MISMATCH", "Se declaró application/json pero no es JSON válido.", null); } }
                return await AddAnnex(http, clock, ctx, id, p, kind, data, mime, file.FileName, json);
            }
            JsonAnnexRequest? b;
            try { b = await JsonSerializer.DeserializeAsync<JsonAnnexRequest>(http.Request.Body, Canonical.Web); } catch (JsonException) { b = null; }
            if (b?.Kind is null || b.Content is null)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Envía multipart (file + kind) o JSON { kind, content }.", "Ejemplo JSON: { \"kind\": \"FEE_SCHEDULE\", \"content\": { \"managementFee\": \"0.45\", \"currency\": \"SEK\" } }");
            return await AddAnnex(http, clock, ctx, id, p, b.Kind, Encoding.UTF8.GetBytes(b.Content.Value.GetRawText()), "application/json", null, b.Content);
        }).WithSummary("Adjuntar un anexo PDF o JSON (multipart o JSON); antivirus, tipo real y 5 MB").Produces<Contract>(201);

        g.MapPost("/{id}/send", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:write");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:create") is { } g2) return g2;
            var c = Load(db, id)!;
            if (c.Status != "DRAFT") return Problems.Conflict(http, "CONTRACT_NOT_DRAFT", $"El contrato está {c.Status}: solo se envía un DRAFT.", null);
            var now = clock.UtcNow; var exp = now.AddDays(14);
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{CustomerOf(db, id)}", forwardAuth: false);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE contracts SET status = 'PENDING_SIGNATURE', sent_at = @at, expires_ticks = @et, expires_at = @ea WHERE id = @id",
                new { at = Nordic.Iso(clock.LocalNow), et = exp.UtcTicks, ea = Nordic.Iso(Nordic.ToStockholm(exp)), id }, tx);
            Outbox.Add(db, http, clock, "contract.sent", id, new
            {
                contractId = id, customerId = CustomerOf(db, id), type = c.Type, expiresAt = Nordic.Iso(Nordic.ToStockholm(exp)),
                notificationVars = new { firstName = cust.Str("firstName") ?? "", contractType = c.Content.GetProperty("title").GetString(), deadline = Nordic.ToStockholm(exp).ToString("yyyy-MM-dd") },
            }, tx);
            tx.Commit();
            return Ok(http, Load(db, id)!);
        }).WithSummary("Enviar a firma (14 días de plazo)").Produces<Contract>();

        g.MapPost("/{id}/signing-sessions", async (HttpContext http, string id, SigningSessionRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:sign") is { } g2) return g2;
            Expire(db, clock);
            var c = Load(db, id)!;
            if (c.Status is not ("PENDING_SIGNATURE" or "PARTIALLY_SIGNED"))
                return Problems.Conflict(http, "CONTRACT_NOT_SIGNABLE", $"El contrato está {c.Status}.", c.Status == "DRAFT" ? "Primero hay que enviarlo: POST /api/v2/contracts/{id}/send" : null);
            var party = c.Parties.FirstOrDefault(x => x.Party == b.Party);
            if (party is null) return Problems.BadRequest(http, "UNKNOWN_PARTY", $"Este contrato no tiene la parte {b.Party}.", "Partes: " + string.Join(", ", c.Parties.Select(x => x.Party)));
            if (party.Signed) return Problems.Conflict(http, "PARTY_ALREADY_SIGNED", $"La parte {b.Party} ya firmó.", null);
            var roleOk = b.Party switch { "CUSTOMER" => p.Role == "Customer", "ADVISOR" => p.Role == "Advisor", "COMPLIANCE" => p.Role == "Compliance", _ => false };
            if (!roleOk) return Problems.Of(http, 403, "WRONG_SIGNER", "Prohibido", $"La parte {b.Party} la firma su titular, no el rol {p.Role}.", "CUSTOMER → token del cliente · ADVISOR → token del asesor · COMPLIANCE → compliance.karin");
            string pn;
            if (b.Party == "CUSTOMER") pn = (await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{CustomerOf(db, id)}", forwardAuth: false)).Str("nationalId")!;
            else pn = EmployeePersonalNumber(p.Sub);
            var s = await Downstream.Post(http, "signing-service", "/api/v1/signing-sessions", new
            {
                contractId = id, party = b.Party, personalNumber = pn, contentHash = c.ContentHash, templateVersion = c.TemplateVersion, title = c.Content.GetProperty("title").GetString(), signer = p.Sub,
            }, forwardAuth: false);
            if (s.Status == 409) return Problems.Conflict(http, "SIGNING_ALREADY_IN_PROGRESS", "Ya hay una firma NordID en curso para esta persona.", "Termínala o espera a que caduque (3 min).");
            if (!s.Ok) return Problems.Of(http, s.Status >= 500 ? 503 : 502, "SIGNING_UNAVAILABLE", "Firma no disponible", "signing-service respondió " + s.Status, null);
            return Results.Created($"/api/v2/contracts/{id}/signing-sessions/{s.Str("id")}", s.Json);
        }).WithSummary("Abrir la firma NordID de una parte (CUSTOMER, ADVISOR o COMPLIANCE)");

        g.MapGet("/{id}/signing-sessions/{sessionId}", async (HttpContext http, string id, string sessionId) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using (var db = ctx.OpenDb()) if (await Guard(http, p, db, id, "contract:read") is { } g2) return g2;
            var s = await Downstream.Get(http, "signing-service", $"/api/v1/signing-sessions/{sessionId}", forwardAuth: false);
            if (s.Status == 404 || (s.Ok && s.Str("contractId") != id)) return Problems.NotFound(http, "SIGNING_SESSION_NOT_FOUND", $"No existe la sesión de firma {sessionId} en este contrato.", null);
            return Results.Content(s.Raw, "application/json", Encoding.UTF8, s.Status);
        }).WithSummary("Estado de una sesión de firma (sondéala hasta COMPLETED)");

        g.MapGet("/{id}/signatures", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:read") is { } g2) return g2;
            Expire(db, clock);
            var c = Load(db, id)!;
            return Results.Ok(new { contractId = id, status = c.Status, required = c.Parties.Select(x => x.Party), signed = c.Signatures.Select(s => s.Party),
                missing = c.Parties.Where(x => !x.Signed).Select(x => x.Party), signatures = c.Signatures, expiresAt = c.ExpiresAt });
        }).WithSummary("Quién ha firmado y quién falta");

        g.MapPost("/{id}/amendments", async (HttpContext http, string id, AmendRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:write");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:create") is { } g2) return g2;
            var c = Load(db, id)!;
            if (c.Status != "ACTIVE") return Problems.Conflict(http, "CONTRACT_NOT_ACTIVE", $"Solo se enmienda un contrato ACTIVE (este está {c.Status}).", null);
            if (c.AmendedBy is not null) return Problems.Conflict(http, "AMENDMENT_IN_PROGRESS", $"Ya existe la enmienda {c.AmendedBy}.", null);
            if (string.IsNullOrWhiteSpace(b.Reason)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta reason.", null, new[] { new FieldError("reason", "obligatorio") });
            var create = await Downstream.Send(http, "contract-service", HttpMethod.Post, "/api/v2/contracts", new { type = c.Type, customerId = CustomerOf(db, id), accountType = b.AccountType ?? c.AccountType });
            if (!create.Ok) return Results.Content(create.Raw, "application/problem+json; charset=utf-8", Encoding.UTF8, create.Status);
            var newId = create.Str("id")!;
            db.Execute("UPDATE contracts SET amends = @id WHERE id = @newId", new { id, newId });
            db.Execute("UPDATE contracts SET amended_by = @newId WHERE id = @id", new { newId, id });
            return Results.Created($"/api/v2/contracts/{newId}", Load(db, newId));
        }).WithSummary("Enmendar: crea un contrato nuevo enlazado (amends); el original no cambia hasta que la enmienda se activa").Produces<Contract>(201);

        g.MapPost("/{id}/withdraw", async (HttpContext http, string id, ReasonRequest? b) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:write");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:withdraw") is { } g2) return g2;
            var c = Load(db, id)!;
            if (c.Status != "ACTIVE") return Problems.Conflict(http, "CONTRACT_NOT_ACTIVE", $"Solo se desiste de un contrato ACTIVE (este está {c.Status}).", null);
            var deadline = db.ExecuteScalar<long>("SELECT deadline_ticks FROM contracts WHERE id = @id", new { id });
            if (clock.UtcNow.UtcTicks > deadline)
                return Problems.Unprocessable(http, "WITHDRAWAL_PERIOD_EXPIRED", $"El plazo de desistimiento terminó el {c.WithdrawalDeadline}.", "Son 14 días naturales desde la activación, hasta las 23:59:59 hora de Estocolmo del último día.");
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE contracts SET status = 'WITHDRAWN', closed_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, http, clock, "contract.withdrawn", id, new { contractId = id, customerId = CustomerOf(db, id), reason = b?.Reason }, tx);
            tx.Commit();
            return Ok(http, Load(db, id)!);
        }).WithSummary("Desistir en los 14 días siguientes a la activación").Produces<Contract>();

        g.MapPost("/{id}/terminate", async (HttpContext http, string id, ReasonRequest? b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (CustomerOf(db, id) is null) return Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null);
            var c = Load(db, id)!;
            if (c.Status != "ACTIVE") return Problems.Conflict(http, "CONTRACT_NOT_ACTIVE", $"Solo se resuelve un contrato ACTIVE (este está {c.Status}).", null);
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE contracts SET status = 'TERMINATED', closed_at = @at WHERE id = @id", new { at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, http, clock, "contract.terminated", id, new { contractId = id, reason = b?.Reason }, tx);
            tx.Commit();
            return Ok(http, Load(db, id)!);
        }).WithSummary("Resolver un contrato activo (Operaciones)").Produces<Contract>();

        g.MapGet("/{id}/render", async (HttpContext http, string id, string? format) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:read") is { } g2) return g2;
            if (format is not (null or "pdf")) return Problems.BadRequest(http, "UNSUPPORTED_FORMAT", "Solo format=pdf.", null);
            var c = Load(db, id)!;
            var lines = new List<string> { $"Referencia: {c.Id}", $"Cliente: {c.CustomerRef}", $"Plantilla: {c.Type} v{c.TemplateVersion} · Estado: {c.Status}", "" };
            foreach (var cl in c.Content.GetProperty("clauses").EnumerateArray())
            {
                lines.Add($"{cl.GetProperty("clauseNo").GetInt32()}. {cl.GetProperty("title").GetString()}");
                lines.Add(cl.GetProperty("text").GetString()!);
                lines.Add("");
            }
            lines.Add("Huella SHA-256 del contenido: " + c.ContentHash);
            foreach (var s in c.Signatures) lines.Add($"Firmado por {s.Party} el {s.SignedAt}");
            var pdf = await Downstream.Post(http, "render-service", "/api/v1/render?format=json", new { title = c.Content.GetProperty("title").GetString(), lines, format = "json" }, forwardAuth: false);
            if (!pdf.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "render-service respondió " + pdf.Status, null);
            var doc = await Downstream.Post(http, "document-vault", "/api/v1/documents", new
            {
                kind = "CONTRACT_PDF", mimeType = "application/pdf", fileName = $"contrato-{id[..8]}.pdf", contentBase64 = pdf.Str("contentBase64"), contractId = id, expiresInSeconds = 3600,
            }, forwardAuth: false);
            var link = await Downstream.Post(http, "document-vault", $"/api/v1/documents/{doc.Str("id")}/links", new { ttlSeconds = 60 }, forwardAuth: false);
            return Results.Created(link.Str("url")!, new { contractId = id, documentId = doc.Str("id"), pages = pdf.Json.GetProperty("pages").GetInt32(), sha256 = pdf.Str("sha256"),
                url = link.Str("url"), expiresAt = link.Str("expiresAt"), singleUse = true });
        }).WithSummary("PDF legible: devuelve una URL de descarga de un solo uso (60 s)");

        g.MapPost("/{id}/verify", async (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out var p, scope: "contracts:read");
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            if (await Guard(http, p, db, id, "contract:read") is { } g2) return g2;
            var stored = db.QuerySingle<(string Content, string Hash)>("SELECT content, content_hash FROM contracts WHERE id = @id", new { id });
            var computed = Canonical.Sha256Hex(Canonical.OfJson(stored.Content));
            var c = Load(db, id)!;
            var sigs = c.Signatures.Select(s => new { party = s.Party, signedHash = s.SignedHash, valid = s.SignedHash == computed && s.TemplateVersion == c.TemplateVersion }).ToList();
            return Results.Ok(new { contractId = id, storedHash = stored.Hash, computedHash = computed, contentHashValid = computed == stored.Hash, signatures = sigs,
                valid = computed == stored.Hash && sigs.All(s => s.valid) });
        }).WithSummary("Recalcular contentHash y comprobar cada firma");

        // ---------- internos ----------
        app.MapPost("/api/v2/internal/contracts/{id}/signatures", (HttpContext http, string id, InternalSignature b) =>
        {
            using var db = ctx.OpenDb();
            var c = Load(db, id);
            if (c is null) return Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null);
            if (c.Status is not ("PENDING_SIGNATURE" or "PARTIALLY_SIGNED")) return Problems.Conflict(http, "CONTRACT_NOT_SIGNABLE", $"El contrato está {c.Status}.", null);
            if (c.Signatures.Any(s => s.Party == b.Party)) return Problems.Conflict(http, "PARTY_ALREADY_SIGNED", $"La parte {b.Party} ya firmó.", null);
            var customerId = CustomerOf(db, id);
            using var tx = db.BeginTransaction();
            if (b.SignedHash != c.ContentHash || b.TemplateVersion != c.TemplateVersion)
            {
                var reason = b.SignedHash != c.ContentHash ? "La firma se hizo sobre un contenido distinto (contentHash)." : $"La firma usó la plantilla v{b.TemplateVersion} y el contrato es v{c.TemplateVersion}.";
                db.Execute("UPDATE contracts SET status = 'INVALID_SIGNATURE', invalid_reason = @reason, closed_at = @at WHERE id = @id", new { reason, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, http, clock, "contract.invalidated", id, new { contractId = id, customerId, party = b.Party, reason }, tx);
                tx.Commit();
                return Results.Ok(Load(db, id));
            }
            db.Execute("INSERT INTO signatures VALUES (@id, @Party, @SignedAt, @SessionId, @SignedHash, @TemplateVersion, @Signature, @Signer)",
                new { id, b.Party, b.SignedAt, b.SessionId, b.SignedHash, b.TemplateVersion, b.Signature, b.Signer }, tx);
            var signedNow = c.Signatures.Select(s => s.Party).Append(b.Party!).ToHashSet();
            if (c.Parties.All(x => signedNow.Contains(x.Party)))
            {
                var local = clock.LocalNow;
                var lastDay = DateOnly.FromDateTime(local.DateTime).AddDays(14);
                var end = new DateTime(lastDay.Year, lastDay.Month, lastDay.Day, 23, 59, 59, 999);
                var deadline = new DateTimeOffset(end, Nordic.Stockholm.GetUtcOffset(end));
                db.Execute("UPDATE contracts SET status = 'ACTIVE', activated_at = @at, deadline_ticks = @dt, deadline = @d WHERE id = @id",
                    new { at = Nordic.Iso(local), dt = deadline.UtcTicks, d = Nordic.Iso(deadline), id }, tx);
                Outbox.Add(db, http, clock, "contract.activated", id, new { contractId = id, customerId, type = c.Type, amends = c.Amends }, tx);
                if (c.Amends is not null)
                {
                    db.Execute("UPDATE contracts SET status = 'SUPERSEDED', closed_at = @at WHERE id = @o AND status = 'ACTIVE'", new { at = Nordic.Iso(local), o = c.Amends }, tx);
                    Outbox.Add(db, http, clock, "contract.superseded", c.Amends, new { contractId = c.Amends, supersededBy = id }, tx);
                }
            }
            else
            {
                db.Execute("UPDATE contracts SET status = 'PARTIALLY_SIGNED' WHERE id = @id", new { id }, tx);
                Outbox.Add(db, http, clock, "contract.signed", id, new { contractId = id, customerId, party = b.Party }, tx);
            }
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Interno").WithSummary("signing-service registra una firma completada (red interna)");

        app.MapGet("/api/v2/internal/contracts/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            Expire(db, clock);
            var c = Load(db, id);
            return c is null ? Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null) : Ok(http, c);
        }).WithTags("Interno").WithSummary("Contrato para otros servicios (red interna)").Produces<Contract>();

        app.MapGet("/api/v2/internal/contracts", (string customerId, string? type, string? status) =>
        {
            using var db = ctx.OpenDb();
            Expire(db, clock);
            return Results.Ok(db.Query<string>("SELECT id FROM contracts WHERE customer_id = @customerId AND (@type IS NULL OR type = @type) AND (@status IS NULL OR status = @status) ORDER BY seq DESC",
                new { customerId, type, status }).Select(i => Load(db, i)!).ToList());
        }).WithTags("Interno").WithSummary("Contratos de un cliente (red interna)").Produces<List<Contract>>();

        app.MapPost("/_test/contracts/{id}/tamper", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var content = db.ExecuteScalar<string?>("SELECT content FROM contracts WHERE id = @id", new { id });
            if (content is null) return Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null);
            db.Execute("UPDATE contracts SET content = @c WHERE id = @id", new { c = content.Replace("Nordvik", "N0rdvik"), id });
            return Results.Ok(new { contractId = id, tampered = true, note = "Ahora POST /verify debe dar contentHashValid=false" });
        }).WithTags("Solo pruebas").WithSummary("Solo pruebas: altera el texto de una cláusula sin recalcular el hash");

        app.MapPost("/_test/contracts/{id}/bump-template", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            return db.Execute("UPDATE contracts SET template_version = template_version + 1 WHERE id = @id", new { id }) == 0
                ? Problems.NotFound(http, "CONTRACT_NOT_FOUND", $"No existe el contrato {id}.", null)
                : Results.Ok(new { contractId = id, note = "Simula la trampa T26: la siguiente firma se hará con la plantilla anterior y el contrato quedará INVALID_SIGNATURE" });
        }).WithTags("Solo pruebas").WithSummary("Solo pruebas: cambia la versión de plantilla entre dos firmas (trampa T26)");

        app.Lifetime.ApplicationStarted.Register(() => _timer = new Timer(_ => { try { using var db = ctx.OpenDb(); Expire(db, clock); } catch { } }, null, 2000, 2000));
        app.Lifetime.ApplicationStopping.Register(() => _timer?.Dispose());
    }
}

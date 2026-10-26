using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record EidAuthRequest(string? PersonalNumber, string? Purpose);
public sealed record EidSignRequest(string? PersonalNumber, string? VisibleText, string? DataHash, string? Purpose);
public sealed record EidUser(string PersonalNumber, string Name);
public sealed record EidOrder(string OrderRef, string Type, string Status, string? HintCode, string? FailureReason, string? Purpose, string MaskedPersonalNumber,
    string CreatedAt, string ExpiresAt, string? CompletedAt, EidUser? User, string? Signature, string? SignedDataHash, string? PurgeAt);

/// <summary>
/// eid-broker: nuestra fachada sobre NordID (E1). Traduce sus estados a los nuestros (PENDING, COMPLETE, FAILED, EXPIRED),
/// caduca las órdenes a los 3 minutos del reloj virtual y, por protección de datos, borra el resultado (personalNumber y firma)
/// 10 minutos después de completarse: a partir de ahí la orden responde 410.
/// </summary>
public sealed class EidBroker : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "eid-broker", "C-identity", 6203, 2, "Identidad",
        "Autenticación y firma con NordID (E1): orderRef, sondeo, caducidad a 3 min y borrado del resultado a los 10 min (dato efímero).",
        DependsOn: new[] { "nordid-sim", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS orders (order_ref TEXT PRIMARY KEY, type TEXT, status TEXT, hint TEXT, failure TEXT, purpose TEXT, masked TEXT, pn_hash TEXT,
                     created_ticks INTEGER, created_at TEXT, expires_ticks INTEGER, expires_at TEXT, completed_at TEXT, purge_ticks INTEGER, purge_at TEXT,
                     user_pn TEXT, user_name TEXT, signature TEXT, data_hash TEXT, purged INTEGER)");
    }

    private static async Task<DownstreamReply?> Sim(string path, object body)
    {
        try { return await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("nordid-sim") + path, body, null, TimeSpan.FromSeconds(5)); }
        catch (Exception) { return null; }
    }

    private static IResult Unavailable(HttpContext http) =>
        Problems.Of(http, 503, "EID_PROVIDER_UNAVAILABLE", "NordID no disponible", "El proveedor de identidad electrónica no respondió.", "Mira /__admin/behaviour en nordid-sim (puerto 7701).");

    private static EidOrder? Load(Microsoft.Data.Sqlite.SqliteConnection db, string orderRef)
    {
        var r = db.QuerySingleOrDefault<(string Ref, string Type, string Status, string? Hint, string? Failure, string? Purpose, string Masked, string CreatedAt, string ExpiresAt,
            string? CompletedAt, string? PurgeAt, string? Pn, string? Name, string? Sig, string? DataHash)>(
            "SELECT order_ref, type, status, hint, failure, purpose, masked, created_at, expires_at, completed_at, purge_at, user_pn, user_name, signature, data_hash FROM orders WHERE order_ref = @orderRef", new { orderRef });
        if (r.Ref is null) return null;
        return new EidOrder(r.Ref, r.Type, r.Status, r.Hint, r.Failure, r.Purpose, r.Masked, r.CreatedAt, r.ExpiresAt, r.CompletedAt,
            r.Pn is null ? null : new EidUser(r.Pn, r.Name!), r.Sig, r.DataHash, r.PurgeAt);
    }

    private static async Task<IResult> Start(HttpContext http, IClock clock, ServiceContext ctx, string type, string? pn, string? purpose, string? visible, string? dataHash)
    {
        var errors = new List<FieldError>();
        if (pn is not null && !NationalId.Check(pn).Valid) errors.Add(new FieldError("personalNumber", "número nacional no válido (AAAAMMDD-NNNN)"));
        if (type == "SIGN" && pn is null) errors.Add(new FieldError("personalNumber", "obligatorio para firmar"));
        if (type == "SIGN" && string.IsNullOrWhiteSpace(visible)) errors.Add(new FieldError("visibleText", "obligatorio: lo que el usuario verá y firmará"));
        if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Petición de identidad electrónica no válida.", "Ejemplo: { \"personalNumber\": \"19850314-1236\", \"purpose\": \"login\" }", errors);
        var text = visible is null ? null : visible + (dataHash is null ? "" : "\nHuella del documento: " + dataHash);
        var r = await Sim(type == "AUTH" ? "/rp/v6/auth" : "/rp/v6/sign", new
        {
            personalNumber = pn, endUserIp = "10.20.30.40", userVisibleData = text is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
        });
        if (r is null || r.Status >= 500) return Unavailable(http);
        if (r.Status == 409) return Problems.Conflict(http, "EID_ALREADY_IN_PROGRESS", "Ya hay una operación de NordID en curso para esta persona.", "Cancela la anterior o espera 3 minutos.");
        if (!r.Ok) return Problems.BadRequest(http, "EID_PROVIDER_REJECTED", "NordID rechazó la petición: " + r.Str("details"), null);
        var now = clock.UtcNow;
        var orderRef = r.Str("orderRef")!;
        using var db = ctx.OpenDb();
        db.Execute(@"INSERT INTO orders (order_ref, type, status, purpose, masked, pn_hash, created_ticks, created_at, expires_ticks, expires_at, data_hash, purged)
                     VALUES (@orderRef, @type, 'PENDING', @purpose, @masked, @h, @ct, @ca, @et, @ea, @dataHash, 0)",
            new { orderRef, type, purpose, masked = pn is null ? "(cualquiera)" : NationalId.Mask(pn), h = pn is null ? null : Canonical.Sha256Hex(pn),
                  ct = now.UtcTicks, ca = Nordic.Iso(Nordic.ToStockholm(now)), et = now.AddMinutes(3).UtcTicks, ea = Nordic.Iso(Nordic.ToStockholm(now.AddMinutes(3))), dataHash });
        http.Response.Headers.Location = $"/api/v1/eid/orders/{orderRef}";
        return Results.Json(new { orderRef, autoStartToken = r.Str("autoStartToken"), status = "PENDING", expiresAt = Nordic.Iso(Nordic.ToStockholm(now.AddMinutes(3))),
            pollUrl = $"/api/v1/eid/orders/{orderRef}", pollEverySeconds = 1 }, statusCode: 201);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/eid/auth", (HttpContext http, IClock clock, EidAuthRequest b) => Start(http, clock, ctx, "AUTH", b.PersonalNumber, b.Purpose, null, null))
            .WithTags("NordID").WithSummary("Iniciar una autenticación con NordID");
        app.MapPost("/api/v1/eid/sign", (HttpContext http, IClock clock, EidSignRequest b) => Start(http, clock, ctx, "SIGN", b.PersonalNumber, b.Purpose, b.VisibleText, b.DataHash))
            .WithTags("NordID").WithSummary("Iniciar una firma con NordID (visibleText + dataHash del documento)");

        app.MapGet("/api/v1/eid/orders/{orderRef}", async (HttpContext http, IClock clock, string orderRef) =>
        {
            using var db = ctx.OpenDb();
            var now = clock.UtcNow;
            // Autodestrucción: el resultado se borra 10 minutos después de completarse
            db.Execute("UPDATE orders SET user_pn = NULL, user_name = NULL, signature = NULL, purged = 1 WHERE purge_ticks IS NOT NULL AND purge_ticks <= @t AND purged = 0", new { t = now.UtcTicks });
            var row = db.QuerySingleOrDefault<(string? Status, long ExpiresTicks, long Purged)>("SELECT status, expires_ticks, purged FROM orders WHERE order_ref = @orderRef", new { orderRef });
            if (row.Status is null) return Problems.NotFound(http, "EID_ORDER_NOT_FOUND", $"No existe la orden {orderRef}.", null);
            if (row.Purged == 1)
                return Problems.Of(http, 410, "EID_RESULT_PURGED", "Resultado destruido", "El resultado de esta orden se borró 10 minutos después de completarse (protección de datos).",
                    "Los datos de identidad son efímeros: léelos en cuanto la orden esté COMPLETE.");
            if (row.Status == "PENDING")
            {
                if (now.UtcTicks > row.ExpiresTicks)
                {
                    db.Execute("UPDATE orders SET status = 'EXPIRED', failure = 'EXPIRED' WHERE order_ref = @orderRef", new { orderRef });
                    return Results.Ok(Load(db, orderRef));
                }
                var r = await Sim("/rp/v6/collect", new { orderRef });
                if (r is null || r.Status >= 500) return Unavailable(http);
                if (!r.Ok) return Problems.NotFound(http, "EID_ORDER_NOT_FOUND", "NordID ya no conoce la orden (¿cancelada?).", null);
                var st = r.Str("status");
                if (st == "pending") db.Execute("UPDATE orders SET hint = @h WHERE order_ref = @orderRef", new { h = r.Str("hintCode"), orderRef });
                else if (st == "failed")
                {
                    var reason = r.Str("hintCode") switch
                    {
                        "userCancel" => "USER_CANCELLED", "expiredTransaction" => "EXPIRED", "certificateErr" => "CERTIFICATE_ERROR", "startFailed" => "START_FAILED", var x => x?.ToUpperInvariant() ?? "FAILED",
                    };
                    db.Execute("UPDATE orders SET status = @s, failure = @reason, hint = NULL WHERE order_ref = @orderRef", new { s = reason == "EXPIRED" ? "EXPIRED" : "FAILED", reason, orderRef });
                }
                else if (st == "complete")
                {
                    var cd = r.Json.GetProperty("completionData");
                    var user = cd.GetProperty("user");
                    db.Execute(@"UPDATE orders SET status = 'COMPLETE', hint = NULL, completed_at = @ca, purge_ticks = @pt, purge_at = @pa, user_pn = @pn, user_name = @name, signature = @sig WHERE order_ref = @orderRef",
                        new { ca = Nordic.Iso(Nordic.ToStockholm(now)), pt = now.AddMinutes(10).UtcTicks, pa = Nordic.Iso(Nordic.ToStockholm(now.AddMinutes(10))),
                              pn = user.GetProperty("personalNumber").GetString(), name = user.GetProperty("name").GetString(), sig = cd.GetProperty("signature").GetString(), orderRef });
                }
            }
            return Results.Ok(Load(db, orderRef));
        }).WithTags("NordID").WithSummary("Estado de una orden (sondéala cada segundo)").Produces<EidOrder>();

        app.MapPost("/api/v1/eid/orders/{orderRef}/cancel", async (HttpContext http, string orderRef) =>
        {
            using var db = ctx.OpenDb();
            var o = Load(db, orderRef);
            if (o is null) return Problems.NotFound(http, "EID_ORDER_NOT_FOUND", $"No existe la orden {orderRef}.", null);
            if (o.Status != "PENDING") return Problems.Conflict(http, "EID_ORDER_FINISHED", $"La orden ya está {o.Status}.", null);
            var r = await Sim("/rp/v6/cancel", new { orderRef });
            if (r is null || r.Status >= 500) return Unavailable(http);
            db.Execute("UPDATE orders SET status = 'CANCELLED', failure = 'CANCELLED_BY_RP' WHERE order_ref = @orderRef", new { orderRef });
            return Results.Ok(Load(db, orderRef));
        }).WithTags("NordID").WithSummary("Cancelar una orden pendiente");
    }
}

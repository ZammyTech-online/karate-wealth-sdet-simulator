using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// employer-clearing-adapter: recibe el webhook del Clearing (E9), comprueba la firma HMAC sobre el cuerpo EXACTO (si alguien lo
/// re-serializa, deja de cuadrar: trampa T18), descarga el CSV, lo entrega a employer-file-ingest y devuelve el acuse al clearing.
/// </summary>
public sealed class EmployerClearingAdapter : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "employer-clearing-adapter", "P-integrations", 7505, 3, "Integraciones",
        "Webhooks del Clearing de Empleadores (E9) con firma HMAC sobre el cuerpo exacto, descarga del CSV, entrega a employer-file-ingest y acuse de vuelta.",
        DependsOn: new[] { "employer-clearing-sim", "employer-file-ingest", "secrets-service", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS deliveries (file_id TEXT PRIMARY KEY, employer_org TEXT, period TEXT, received_at TEXT, signature_ok INTEGER, ingest_id TEXT, accepted INTEGER, rejected INTEGER, status TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/webhooks/clearing", async (HttpContext http) =>
        {
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync();
            var secret = (await Downstream.Send(http, "secrets-service", HttpMethod.Get, "/api/v1/secrets/employer-clearing-hmac", null, forwardAuth: false, headers: await ServiceToken.Header(clock))).Str("value") ?? "";
            var sig = http.Request.Headers["X-Signature"].FirstOrDefault()?.Replace("sha256=", "");
            if (sig is null || !string.Equals(sig, Canonical.HmacHex(secret, raw), StringComparison.OrdinalIgnoreCase))
                return Problems.Of(http, 401, "INVALID_SIGNATURE", "Firma no válida", "El HMAC de X-Signature no corresponde al cuerpo recibido.", "Se firma el texto exacto, sin re-serializar el JSON.");
            var ev = System.Text.Json.JsonDocument.Parse(raw).RootElement;
            var fileId = ev.GetProperty("fileId").GetString()!;
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM deliveries WHERE file_id = @fileId", new { fileId }) > 0)
                return Results.Ok(new { fileId, duplicate = true, note = "Webhook repetido: el fichero ya se procesó." });
            var csv = await Downstream.SendUrl(HttpMethod.Get, ServiceDirectory.UrlOf("employer-clearing-sim") + $"/v1/files/{fileId}", null, null, TimeSpan.FromSeconds(10));
            if (!csv.Ok) return Problems.Of(http, 502, "FILE_DOWNLOAD_FAILED", "No se pudo descargar", $"El clearing respondió {csv.Status}.", null);
            var ingest = await Downstream.Send(http, "employer-file-ingest", HttpMethod.Post, "/api/v1/employer-files",
                new { fileId, employerOrgNumber = ev.GetProperty("employerOrgNumber").GetString(), period = ev.GetProperty("period").GetString(), csv = csv.Raw },
                forwardAuth: false, headers: await ServiceToken.Header(clock), timeout: TimeSpan.FromSeconds(55));
            if (!ingest.Ok) return Problems.Of(http, 502, "INGEST_FAILED", "No se pudo procesar", $"employer-file-ingest respondió {ingest.Status}: {ingest.Raw}", null);
            var receipt = ingest.Json.GetProperty("receipt");
            await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("employer-clearing-sim") + $"/v1/files/{fileId}/receipt", receipt.GetRawText(), null, TimeSpan.FromSeconds(5));
            db.Execute("INSERT INTO deliveries VALUES (@fileId, @org, @period, @at, 1, @ing, @acc, @rej, 'RECEIPT_SENT')",
                new { fileId, org = ev.GetProperty("employerOrgNumber").GetString(), period = ev.GetProperty("period").GetString(), at = Nordic.Iso(clock.LocalNow), ing = ingest.Str("id"),
                      acc = receipt.GetProperty("accepted").GetInt32(), rej = receipt.GetProperty("rejected").GetArrayLength() });
            return Results.Ok(new { fileId, ingestId = ingest.Str("id"), accepted = receipt.GetProperty("accepted").GetInt32(), rejected = receipt.GetProperty("rejected").GetArrayLength() });
        }).WithTags("Webhooks").WithSummary("Webhook del clearing: firma, descarga, proceso y acuse");

        app.MapGet("/api/v1/deliveries", (HttpContext http) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Operations", "Admin" }) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = db.Query<(string File, string Org, string Period, string At, string? Ing, long Acc, long Rej, string Status)>("SELECT file_id, employer_org, period, received_at, ingest_id, accepted, rejected, status FROM deliveries ORDER BY received_at")
                .Select(x => new { fileId = x.File, employerOrgNumber = x.Org, period = x.Period, receivedAt = x.At, ingestId = x.Ing, accepted = x.Acc, rejected = x.Rej, status = x.Status }).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Webhooks").WithSummary("Ficheros recibidos del clearing");
    }
}

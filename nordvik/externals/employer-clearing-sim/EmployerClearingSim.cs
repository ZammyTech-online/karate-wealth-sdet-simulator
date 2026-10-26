using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ClearingLine(string? PersonalNumber, string? Amount, string? Agreement);
public sealed record ClearingFileRequest(string? EmployerOrgNumber, string? Period, ClearingLine[]? Lines);
public sealed record ClearingReceipt(int? Accepted, JsonElement? Rejected);

/// <summary>
/// employer-clearing-sim · sistema externo E9 (simulado): el Clearing de Empleadores. Cada mes reúne las aportaciones de una empresa en
/// un fichero CSV (personnummer;importe;convenio;periodo) y avisa a Nordvik con un webhook firmado (HMAC en X-Signature).
/// Nordvik descarga el fichero y devuelve un acuse con aceptadas y rechazadas, que aquí se puede consultar.
/// </summary>
public sealed class EmployerClearingSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "employer-clearing-sim", "X-external", 7709, 3, "Sistema externo (simulado)",
        "E9 Clearing de Empleadores: ficheros CSV mensuales de aportaciones, webhook firmado con HMAC hacia Nordvik y acuses de aceptadas y rechazadas.",
        DependsOn: new[] { "clock-service" });

    public const string WebhookSecret = "e9-clearing-webhook-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, (string Org, string Period, string Csv, int Lines, string At)> _files = new();
    private readonly ConcurrentDictionary<string, JsonElement> _receipts = new();
    private readonly ConcurrentDictionary<string, int> _webhookStatus = new();

    public static string Csv(string period, IEnumerable<ClearingLine> lines)
    {
        var sb = new StringBuilder("personnummer;belopp;avtal;period\n");
        foreach (var l in lines) sb.Append($"{l.PersonalNumber};{l.Amount};{l.Agreement};{period}\n");
        return sb.ToString();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);

        app.MapGet("/v1/files/{id}", async (string id) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            return _files.TryGetValue(id, out var f) ? Results.Text(f.Csv, "text/csv; charset=utf-8") : Results.Json(new { error = "FILE_NOT_FOUND" }, statusCode: 404);
        }).WithTags("Clearing v1").WithSummary("Descargar un fichero de aportaciones (CSV con ; )");

        app.MapPost("/v1/files/{id}/receipt", (string id, JsonElement body) =>
        {
            if (!_files.ContainsKey(id)) return Results.Json(new { error = "FILE_NOT_FOUND" }, statusCode: 404);
            _receipts[id] = body.Clone();
            return Results.Ok(new { fileId = id, receiptStored = true });
        }).WithTags("Clearing v1").WithSummary("Nordvik entrega el acuse de un fichero");

        app.MapPost("/__admin/files", async (ClearingFileRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.EmployerOrgNumber) || b.Period is null || b.Lines is not { Length: > 0 })
                return Results.Json(new { error = "BAD_REQUEST", message = "employerOrgNumber, period and lines are required" }, statusCode: 400);
            var id = "cf_" + Guid.NewGuid().ToString("N")[..12];
            _files[id] = (b.EmployerOrgNumber, b.Period, Csv(b.Period, b.Lines), b.Lines.Length, DateTimeOffset.UtcNow.ToString("O"));
            var payload = JsonSerializer.Serialize(new { eventType = "FILE_AVAILABLE", fileId = id, employerOrgNumber = b.EmployerOrgNumber, period = b.Period, lines = b.Lines.Length, url = $"/v1/files/{id}" });
            var status = 0;
            if (ServiceDirectory.Knows("employer-clearing-adapter"))
                try
                {
                    var r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("employer-clearing-adapter") + "/api/v1/webhooks/clearing", payload,
                        new Dictionary<string, string> { ["X-Signature"] = "sha256=" + Canonical.HmacHex(WebhookSecret, payload) }, TimeSpan.FromSeconds(30));
                    status = r.Status;
                }
                catch (DependencyUnavailableException) { status = 503; }
            _webhookStatus[id] = status;
            return Results.Created($"/v1/files/{id}", new { fileId = id, lines = b.Lines.Length, webhookStatus = status });
        }).WithTags("Administración del simulador").WithSummary("Publicar el fichero de una empresa (y avisar a Nordvik por webhook)");

        app.MapGet("/__admin/files/{id}/receipt", (string id) =>
            _receipts.TryGetValue(id, out var r) ? Results.Ok(r) : Results.Json(new { error = "NO_RECEIPT_YET", webhookStatus = _webhookStatus.GetValueOrDefault(id) }, statusCode: 404))
            .WithTags("Administración del simulador").WithSummary("El acuse que devolvió Nordvik");
    }
}

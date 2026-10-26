using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Una dependencia no respondió: el middleware lo convierte en 503 DEPENDENCY_UNAVAILABLE.</summary>
public sealed class DependencyUnavailableException : Exception
{
    public string Service { get; }
    public DependencyUnavailableException(string service, string message) : base(message) => Service = service;
}

/// <summary>Respuesta de otro microservicio.</summary>
public sealed record DownstreamReply(int Status, JsonElement? Body, string Raw)
{
    public bool Ok => Status is >= 200 and < 300;
    public JsonElement Json => Body ?? throw new InvalidOperationException("La respuesta no tiene cuerpo JSON");
    public string? Str(string prop) => Body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Cliente para llamar a otros microservicios. Propaga X-Correlation-Id y Authorization,
/// y traduce los fallos de red a DependencyUnavailableException.
/// </summary>
public static class Downstream
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static async Task<DownstreamReply> Send(HttpContext? caller, string service, HttpMethod method, string path,
        object? body = null, bool forwardAuth = true, IDictionary<string, string>? headers = null, TimeSpan? timeout = null)
    {
        var req = new HttpRequestMessage(method, ServiceDirectory.UrlOf(service) + path);
        if (caller?.Items[NordvikMiddleware.CorrelationKey] is string corr) req.Headers.TryAddWithoutValidation(NordvikMiddleware.CorrelationHeader, corr);
        if (forwardAuth && caller is not null && caller.Request.Headers.Authorization.Count > 0)
            req.Headers.TryAddWithoutValidation("Authorization", caller.Request.Headers.Authorization.ToString());
        req.Headers.TryAddWithoutValidation(NordvikMiddleware.LearningHeader, "true");
        if (headers is not null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        if (body is not null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
            using var res = await Http.SendAsync(req, cts.Token);
            var raw = await res.Content.ReadAsStringAsync(cts.Token);
            JsonElement? json = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { json = JsonDocument.Parse(raw).RootElement.Clone(); } catch { /* no es JSON */ }
            }
            return new DownstreamReply((int)res.StatusCode, json, raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new DependencyUnavailableException(service, $"{service} no responde ({ex.GetType().Name}).");
        }
    }

    public static Task<DownstreamReply> Get(HttpContext? caller, string service, string path, bool forwardAuth = true, TimeSpan? timeout = null) =>
        Send(caller, service, HttpMethod.Get, path, null, forwardAuth, null, timeout);

    /// <summary>Envía bytes tal cual (ficheros) a otro servicio.</summary>
    public static async Task<DownstreamReply> SendBytes(HttpContext? caller, string service, string path, byte[] data, string contentType, IDictionary<string, string>? headers = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, ServiceDirectory.UrlOf(service) + path);
        if (caller?.Items[NordvikMiddleware.CorrelationKey] is string corr) req.Headers.TryAddWithoutValidation(NordvikMiddleware.CorrelationHeader, corr);
        if (headers is not null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        try
        {
            using var res = await Http.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();
            JsonElement? json = null;
            if (!string.IsNullOrWhiteSpace(raw)) { try { json = JsonDocument.Parse(raw).RootElement.Clone(); } catch { } }
            return new DownstreamReply((int)res.StatusCode, json, raw);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DependencyUnavailableException(service, $"{service} no responde ({ex.GetType().Name}).");
        }
    }

    /// <summary>Llamada a una URL completa (sistemas externos simulados o callbacks).</summary>
    public static async Task<DownstreamReply> SendUrl(HttpMethod method, string url, object? body = null, IDictionary<string, string>? headers = null, TimeSpan? timeout = null, string? correlationId = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (correlationId is not null) req.Headers.TryAddWithoutValidation(NordvikMiddleware.CorrelationHeader, correlationId);
        if (headers is not null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        if (body is FormBody form) req.Content = new StringContent(form.Encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
        else if (body is not null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        using var res = await Http.SendAsync(req, cts.Token);
        var raw = await res.Content.ReadAsStringAsync(cts.Token);
        JsonElement? json = null;
        if (!string.IsNullOrWhiteSpace(raw)) { try { json = JsonDocument.Parse(raw).RootElement.Clone(); } catch { } }
        return new DownstreamReply((int)res.StatusCode, json, raw);
    }

    public static Task<DownstreamReply> Post(HttpContext? caller, string service, string path, object? body, bool forwardAuth = true) =>
        Send(caller, service, HttpMethod.Post, path, body, forwardAuth);
}

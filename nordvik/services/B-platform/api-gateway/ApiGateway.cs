using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Route(string Service, string GatewayPrefix, string Target, bool AuthRequired);

/// <summary>
/// api-gateway: la puerta única. http://localhost:6100/{servicio}/api/v1/... se reenvía al servicio.
/// Comprueba la cuota en rate-limiter y, para los servicios privados, que el token esté activo en iam-service
/// (introspección: aquí un token revocado SÍ se rechaza, aunque el servicio, llamado directamente, lo aceptaría).
/// </summary>
public sealed class ApiGateway : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "api-gateway", "B-platform", 6100, 1, "Plataforma",
        "Entrada única: enruta /{servicio}/... al microservicio, aplica cuotas, valida tokens activos y añade X-Correlation-Id.",
        DependsOn: new[] { "rate-limiter", "iam-service" });

    private static readonly HashSet<string> PublicDomains = new() { "A-utilities", "I-market" };
    private static readonly HashSet<string> PublicServices = new() { "iam-service" };
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase) { "Host", "Connection", "Transfer-Encoding", "Keep-Alive", "Upgrade", "Content-Length" };

    private static Dictionary<string, NordvikServiceInfo> Catalog = new();

    /// <summary>El anfitrión le pasa el catálogo completo para saber qué es público.</summary>
    public static void UseCatalog(IEnumerable<NordvikServiceInfo> infos) => Catalog = infos.ToDictionary(i => i.Name);

    private static bool AuthRequired(string service) =>
        !PublicServices.Contains(service) && !(Catalog.TryGetValue(service, out var i) && PublicDomains.Contains(i.Domain));

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/routes", () => Results.Ok(ServiceDirectory.All.Keys.Where(k => k != Info.Name).OrderBy(k => k)
            .Select(k => new Route(k, $"/{k}", ServiceDirectory.UrlOf(k), AuthRequired(k))).ToList()))
            .WithTags("Gateway").WithSummary("Rutas que conoce el gateway").Produces<List<Route>>();

        app.Map("/{service}/{**rest}", async (HttpContext http, string service, string? rest) =>
        {
            if (!ServiceDirectory.All.ContainsKey(service) || service == Info.Name)
                return Problems.NotFound(http, "SERVICE_NOT_FOUND", $"El gateway no conoce el servicio '{service}'.",
                    "La ruta es /{nombre-del-servicio}/api/v1/... Lista las rutas con GET /api/v1/routes.");

            var auth = http.Request.Headers.Authorization.ToString();
            string key = "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "local");
            if (AuthRequired(service))
            {
                if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    http.Response.Headers.WWWAuthenticate = "Bearer";
                    return Problems.Of(http, 401, "AUTHENTICATION_REQUIRED", "No autenticado", $"{service} es privado: el gateway exige un token.",
                        "Pide un token en /iam-service/api/v1/oauth/token y envíalo como Authorization: Bearer <token>.");
                }
                var intro = await Downstream.Post(http, "iam-service", "/api/v1/oauth/introspect", new { token = auth[7..].Trim() }, forwardAuth: false);
                if (!intro.Ok || !intro.Json.GetProperty("active").GetBoolean())
                {
                    http.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                    return Problems.Of(http, 401, "TOKEN_INACTIVE", "No autenticado", "El token no está activo (caducado, revocado o con la sesión cerrada).",
                        "El gateway pregunta a iam-service si el token sigue activo. Pide uno nuevo.");
                }
                key = "user:" + intro.Json.GetProperty("sub").GetString();
            }

            var quota = await Downstream.Post(http, "rate-limiter", "/api/v1/check", new { key, cost = 1 }, forwardAuth: false);
            if (quota.Ok)
            {
                http.Response.Headers["X-RateLimit-Limit"] = quota.Json.GetProperty("limit").GetInt32().ToString();
                http.Response.Headers["X-RateLimit-Remaining"] = quota.Json.GetProperty("remaining").GetInt32().ToString();
                if (!quota.Json.GetProperty("allowed").GetBoolean())
                {
                    http.Response.Headers.RetryAfter = quota.Json.GetProperty("retryAfterSeconds").GetInt32().ToString();
                    return Problems.Of(http, 429, "RATE_LIMITED", "Demasiadas peticiones", $"Has superado la cuota de {quota.Json.GetProperty("limit").GetInt32()} peticiones por minuto.",
                        "Espera los segundos de la cabecera Retry-After. En paralelo, cada hilo debería usar su propio usuario.");
                }
            }

            var target = ServiceDirectory.UrlOf(service) + "/" + (rest ?? "") + http.Request.QueryString;
            var fwd = new HttpRequestMessage(new HttpMethod(http.Request.Method), target);
            foreach (var h in http.Request.Headers)
                if (!HopHeaders.Contains(h.Key) && !h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    fwd.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            fwd.Headers.Remove(NordvikMiddleware.CorrelationHeader);
            fwd.Headers.TryAddWithoutValidation(NordvikMiddleware.CorrelationHeader, (string)http.Items[NordvikMiddleware.CorrelationKey]!);
            fwd.Headers.TryAddWithoutValidation("X-Forwarded-By", "api-gateway");
            if (http.Request.ContentLength > 0 || http.Request.Headers.TransferEncoding.Count > 0)
            {
                var ms = new MemoryStream();
                await http.Request.Body.CopyToAsync(ms);
                fwd.Content = new ByteArrayContent(ms.ToArray());
                foreach (var h in http.Request.Headers.Where(h => h.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && h.Key != "Content-Length"))
                    fwd.Content.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
            }
            HttpResponseMessage res;
            try { res = await Http.SendAsync(fwd); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new DependencyUnavailableException(service, $"{service} no responde detrás del gateway.");
            }
            http.Response.StatusCode = (int)res.StatusCode;
            foreach (var h in res.Headers.Concat(res.Content.Headers))
                if (!HopHeaders.Contains(h.Key) && h.Key != NordvikMiddleware.CorrelationHeader && h.Key != "X-Service" && h.Key != "X-Service-Version")
                    http.Response.Headers[h.Key] = h.Value.ToArray();
            http.Response.Headers["X-Upstream-Service"] = service;
            var bytes = await res.Content.ReadAsByteArrayAsync();
            if (bytes.Length > 0) await http.Response.Body.WriteAsync(bytes);
            return Results.Empty;
        }).ExcludeFromDescription();
    }
}

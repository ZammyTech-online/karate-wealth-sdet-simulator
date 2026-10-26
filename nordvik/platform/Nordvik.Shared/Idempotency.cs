using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Resultado de una operación protegida por Idempotency-Key.</summary>
public sealed record IdempotentResult(int Status, object Body, string? Location = null)
{
    public static IdempotentResult Problem(HttpContext http, int status, string code, string title, string detail, string? hint = null, IEnumerable<FieldError>? errors = null) =>
        new(status, Problems.BodyOf(http, status, code, title, detail, hint, errors));
}

/// <summary>
/// Idempotency-Key en operaciones con dinero (como Stripe): misma clave y mismo cuerpo → misma respuesta, sin repetir la operación;
/// misma clave con otro cuerpo → 422; si la primera sigue en curso → 409. Las claves viven 24 h en idempotency-store.
/// </summary>
public static class Idempotency
{
    public const string Header = "Idempotency-Key";
    private static readonly Regex Valid = new("^[A-Za-z0-9_-]{8,64}$");

    public static async Task<IResult> Run(HttpContext http, string scope, object requestBody, Func<Task<IdempotentResult>> action)
    {
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(key))
            return Problems.BadRequest(http, "IDEMPOTENCY_KEY_REQUIRED", "Falta la cabecera Idempotency-Key.",
                "Envía una clave única por operación, por ejemplo un UUID: * header Idempotency-Key = java.util.UUID.randomUUID() + ''");
        if (!Valid.IsMatch(key))
            return Problems.BadRequest(http, "IDEMPOTENCY_KEY_INVALID", "La Idempotency-Key debe tener entre 8 y 64 caracteres: letras, números, - o _.", null);
        var hash = Canonical.Hash(requestBody);
        var claim = await Downstream.Send(http, "idempotency-store", HttpMethod.Put, $"/api/v1/keys/{scope}/{key}", new { requestHash = hash }, forwardAuth: false);
        if (claim.Status == 200)
        {
            var stored = claim.Json;
            if (stored.GetProperty("state").GetString() == "COMPLETED")
            {
                http.Response.Headers["Idempotent-Replayed"] = "true";
                if (stored.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.String) http.Response.Headers.Location = loc.GetString();
                var isProblem = stored.GetProperty("status").GetInt32() >= 400;
                return Results.Content(stored.GetProperty("body").GetRawText(), isProblem ? "application/problem+json; charset=utf-8" : "application/json; charset=utf-8",
                    System.Text.Encoding.UTF8, stored.GetProperty("status").GetInt32());
            }
            return Problems.Conflict(http, "IDEMPOTENCY_IN_PROGRESS", "Ya hay una petición con esta Idempotency-Key en curso.", "Espera un momento y repite la misma petición.");
        }
        if (claim.Status == 422)
            return Problems.Unprocessable(http, "IDEMPOTENCY_KEY_REUSED", "Esta Idempotency-Key ya se usó con otro cuerpo distinto.",
                "Cada operación nueva necesita una clave nueva. Repetir la misma clave solo sirve para reintentar exactamente la misma petición.");
        if (claim.Status != 201) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "idempotency-store respondió " + claim.Status, null);

        IdempotentResult result;
        try { result = await action(); }
        catch
        {
            await Downstream.Send(http, "idempotency-store", HttpMethod.Delete, $"/api/v1/keys/{scope}/{key}", null, forwardAuth: false);
            throw;
        }
        var bodyJson = result.Body is IResultBody rb ? rb.Json : JsonSerializer.Serialize(result.Body, result.Body.GetType(), JsonFor(http));
        if (result.Status >= 500)
            await Downstream.Send(http, "idempotency-store", HttpMethod.Delete, $"/api/v1/keys/{scope}/{key}", null, forwardAuth: false);
        else
            await Downstream.Post(http, "idempotency-store", $"/api/v1/keys/{scope}/{key}/response", new { status = result.Status, body = JsonDocument.Parse(bodyJson).RootElement, location = result.Location }, forwardAuth: false);
        if (result.Location is not null) http.Response.Headers.Location = result.Location;
        return Results.Content(bodyJson, result.Status >= 400 ? "application/problem+json; charset=utf-8" : "application/json; charset=utf-8", System.Text.Encoding.UTF8, result.Status);
    }

    private static JsonSerializerOptions JsonFor(HttpContext http) =>
        http.RequestServices.GetService(typeof(Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>)) is Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> o
            ? o.Value.SerializerOptions : Canonical.Web;
}

/// <summary>Un cuerpo JSON ya serializado (por ejemplo un problema RFC 9457) para devolverlo desde Idempotency.Run.</summary>
public interface IResultBody { string Json { get; } }
public sealed record RawJson(string Json) : IResultBody;

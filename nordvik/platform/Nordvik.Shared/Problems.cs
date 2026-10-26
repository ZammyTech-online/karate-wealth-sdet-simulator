using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Un error de validación de un campo concreto.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// Errores con formato RFC 9457 (Problem Details), iguales en todos los servicios.
/// Si el modo didáctico está activo, se añade el campo "hint" con una pista en lenguaje humano.
/// </summary>
public static class Problems
{
    public const string BaseType = "https://errors.nordvik.local/";

    public static IResult Of(HttpContext http, int status, string code, string title, string detail,
        string? hint = null, IEnumerable<FieldError>? errors = null) =>
        Results.Json(BodyOf(http, status, code, title, detail, hint, errors), statusCode: status, contentType: "application/problem+json; charset=utf-8");

    /// <summary>El cuerpo del problema sin envolver en IResult (para guardarlo, por ejemplo, en idempotency-store).</summary>
    public static Dictionary<string, object?> BodyOf(HttpContext http, int status, string code, string title, string detail,
        string? hint = null, IEnumerable<FieldError>? errors = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = BaseType + Regex.Replace(code.ToLowerInvariant(), "_", "-"),
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["instance"] = http.Request.Path.Value,
            ["code"] = code,
            ["correlationId"] = http.Items[NordvikMiddleware.CorrelationKey] as string,
            ["service"] = http.Items[NordvikMiddleware.ServiceKey] as string,
        };
        if (errors is not null)
            body["errors"] = errors.Select(e => new Dictionary<string, object?> { ["field"] = e.Field, ["message"] = e.Message }).ToList();
        if (hint is not null && http.Items[NordvikMiddleware.LearningKey] is true)
            body["hint"] = hint;
        return body;
    }

    public static IResult BadRequest(HttpContext http, string code, string detail, string? hint = null, IEnumerable<FieldError>? errors = null)
        => Of(http, 400, code, "Petición no válida", detail, hint, errors);

    public static IResult NotFound(HttpContext http, string code, string detail, string? hint = null)
        => Of(http, 404, code, "No encontrado", detail, hint);

    public static IResult Conflict(HttpContext http, string code, string detail, string? hint = null)
        => Of(http, 409, code, "Conflicto", detail, hint);

    public static IResult Unprocessable(HttpContext http, string code, string detail, string? hint = null, IEnumerable<FieldError>? errors = null)
        => Of(http, 422, code, "Regla de negocio incumplida", detail, hint, errors);
}

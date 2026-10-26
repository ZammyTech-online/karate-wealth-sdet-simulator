using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Pregunta a permission-service si el principal puede hacer una acción sobre los datos de un cliente.</summary>
public static class Permissions
{
    public sealed record Decision(bool Allowed, string Reason);

    public static async Task<Decision> Check(HttpContext http, Principal p, string action, string? resourceCustomerId)
    {
        var reply = await Downstream.Post(http, "permission-service", "/api/v1/permissions/check", new
        {
            role = p.Role, customerId = p.CustomerId, advisorId = p.AdvisorId, action, resourceCustomerId,
        }, forwardAuth: false);
        if (!reply.Ok) return new Decision(false, "permission-service respondió " + reply.Status);
        return new Decision(reply.Json.GetProperty("allowed").GetBoolean(), reply.Json.GetProperty("reason").GetString() ?? "");
    }

    /// <summary>
    /// Un cliente que pide datos de otro recibe 404 (no se revela que existen: protección IDOR);
    /// los empleados reciben 403 con el motivo.
    /// </summary>
    public static IResult Denied(HttpContext http, Principal p, Decision d, string notFoundCode, string what) =>
        p.Role == "Customer"
            ? Problems.NotFound(http, notFoundCode, $"No existe {what}.", "Un cliente solo ve sus propios datos. ¿Usas el token del cliente correcto?")
            : Problems.Of(http, 403, "ACCESS_DENIED", "Prohibido", d.Reason, "Las reglas están en GET /api/v1/policies de permission-service (puerto 6202).");
}

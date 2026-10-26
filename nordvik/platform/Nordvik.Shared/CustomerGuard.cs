using Microsoft.AspNetCore.Http;

namespace Nordvik.Shared;

/// <summary>Comprobación común de los servicios satélite del cliente: el cliente existe y el principal tiene permiso.</summary>
public static class CustomerGuard
{
    public static async Task<IResult?> Check(HttpContext http, Principal p, string action, string customerId)
    {
        var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
        if (c.Status == 404)
            return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {customerId}.", "Comprueba el id en customer-registry (puerto 6300).");
        if (!c.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "customer-registry respondió " + c.Status, null);
        var d = await Permissions.Check(http, p, action, customerId);
        if (!d.Allowed) return Permissions.Denied(http, p, d, "CUSTOMER_NOT_FOUND", $"el cliente {customerId}");
        if (action.EndsWith(":write") && c.Str("status") == "CLOSED")
            return Problems.Conflict(http, "CUSTOMER_CLOSED", "El cliente está cerrado: no se pueden cambiar sus datos.", null);
        return null;
    }
}

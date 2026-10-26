using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ResolveRequest(string? TemplateType, int? Version, string? CustomerId, Dictionary<string, JsonElement>? Facts);
public sealed record ClauseDecision(int ClauseNo, string Code, string Title, string Condition, string Reason);
public sealed record Resolution(string TemplateType, int Version, IReadOnlyDictionary<string, object?> Facts, IReadOnlyList<ClauseDecision> Included, IReadOnlyList<ClauseDecision> Excluded);
public sealed record EvaluateRequest(string? Condition, Dictionary<string, JsonElement>? Facts);

/// <summary>
/// clause-engine: decide qué cláusulas entran en un contrato según los datos del cliente (edad, residencia fiscal, tipo de cuenta, PEP, idioma).
/// Las condiciones son expresiones sencillas: always · age >= 65 · taxResidency != 'SE' · accountType == 'ISK' and age &lt; 30 · isPep == true.
/// Cada cláusula incluida o excluida lleva el motivo, para que tu test sepa por qué.
/// </summary>
public sealed class ClauseEngine : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "clause-engine", "H-contracts", 6702, 2, "Contratos y documentos",
        "Evalúa las condiciones de las cláusulas con los datos del cliente y devuelve las incluidas y excluidas con su motivo.",
        DependsOn: new[] { "contract-template-service", "customer-registry", "customer-address-service" });

    private static readonly Regex Term = new(@"^\s*(\w+)\s*(==|!=|>=|<=|>|<)\s*('([^']*)'|-?\d+(\.\d+)?|true|false)\s*$");

    /// <summary>Evalúa una condición. Devuelve null si falta un dato.</summary>
    public static bool? Evaluate(string condition, IReadOnlyDictionary<string, object?> facts)
    {
        if (condition.Trim() == "always") return true;
        bool? any = false;
        foreach (var orPart in Regex.Split(condition, @"\s+or\s+"))
        {
            bool? all = true;
            foreach (var andPart in Regex.Split(orPart, @"\s+and\s+"))
            {
                var m = Term.Match(andPart);
                if (!m.Success) throw new FormatException("Condición no válida: " + andPart);
                if (!facts.TryGetValue(m.Groups[1].Value, out var v) || v is null) { all = null; break; }
                var op = m.Groups[2].Value; var lit = m.Groups[3].Value;
                bool r;
                if (lit.StartsWith('\'')) { var s = Convert.ToString(v, CultureInfo.InvariantCulture); r = op switch { "==" => s == m.Groups[4].Value, "!=" => s != m.Groups[4].Value, _ => throw new FormatException("Con textos solo == o !=") }; }
                else if (lit is "true" or "false") { var b = v is bool bb ? bb : Convert.ToString(v) == "true"; r = op switch { "==" => b == (lit == "true"), "!=" => b != (lit == "true"), _ => throw new FormatException("Con booleanos solo == o !=") }; }
                else
                {
                    var x = Convert.ToDecimal(v, CultureInfo.InvariantCulture); var y = decimal.Parse(lit, CultureInfo.InvariantCulture);
                    r = op switch { "==" => x == y, "!=" => x != y, ">=" => x >= y, "<=" => x <= y, ">" => x > y, _ => x < y };
                }
                if (!r) { all = false; break; }
            }
            if (all == true) return true;
            if (all is null) any = null;
        }
        return any;
    }

    private static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(), JsonValueKind.Number => e.GetDecimal(), JsonValueKind.True => true, JsonValueKind.False => false, _ => null,
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/clauses/resolve", async (HttpContext http, ResolveRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.TemplateType))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta templateType.", "Ejemplo: { \"templateType\": \"MASTER_AGREEMENT\", \"facts\": { \"age\": 67, \"taxResidency\": \"SE\", \"accountType\": \"ISK\" } }");
            var path = b.Version is null ? $"/api/v1/contract-templates/{b.TemplateType}/current" : $"/api/v1/contract-templates/{b.TemplateType}/versions/{b.Version}";
            var t = await Downstream.Get(http, "contract-template-service", path, forwardAuth: false);
            if (t.Status == 404) return Results.Content(t.Raw, "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, 404);
            var facts = new Dictionary<string, object?>();
            if (b.CustomerId is not null)
            {
                var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{b.CustomerId}", forwardAuth: false);
                if (c.Status == 404) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {b.CustomerId}.", null);
                facts["age"] = c.Json.GetProperty("age").GetDecimal();
                facts["language"] = c.Str("language");
                var a = await Downstream.Get(http, "customer-address-service", $"/api/v1/internal/customers/{b.CustomerId}/address", forwardAuth: false);
                facts["taxResidency"] = a.Ok ? a.Str("country") ?? "SE" : "SE";
                facts["isPep"] = false;
            }
            foreach (var (k, v) in b.Facts ?? new()) facts[k] = FromJson(v);
            var included = new List<ClauseDecision>(); var excluded = new List<ClauseDecision>();
            foreach (var cl in t.Json.GetProperty("clauses").EnumerateArray())
            {
                var cond = cl.GetProperty("condition").GetString()!;
                bool? ok;
                try { ok = Evaluate(cond, facts); } catch (FormatException ex) { return Problems.Unprocessable(http, "INVALID_CONDITION", ex.Message, null); }
                var d = new ClauseDecision(cl.GetProperty("clauseNo").GetInt32(), cl.GetProperty("code").GetString()!, cl.GetProperty("title").GetString()!, cond,
                    ok switch { true => cond == "always" ? "siempre se incluye" : $"se cumple {cond}", false => $"no se cumple {cond}", null => $"falta un dato para evaluar {cond}" });
                (ok == true ? included : excluded).Add(d);
            }
            return Results.Ok(new Resolution(b.TemplateType, t.Json.GetProperty("version").GetInt32(), facts, included, excluded));
        }).WithTags("Cláusulas").WithSummary("Qué cláusulas entran para este cliente (datos del cliente o facts a mano)").Produces<Resolution>();

        app.MapPost("/api/v1/conditions/evaluate", (HttpContext http, EvaluateRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Condition)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta condition.", null);
            try
            {
                var r = Evaluate(b.Condition, (b.Facts ?? new()).ToDictionary(k => k.Key, k => FromJson(k.Value)));
                return Results.Ok(new { condition = b.Condition, result = r, missingFacts = r is null });
            }
            catch (FormatException ex) { return Problems.Unprocessable(http, "INVALID_CONDITION", ex.Message, "Formato: dato operador valor, unidos con and/or. Textos entre comillas simples."); }
        }).WithTags("Cláusulas").WithSummary("Evaluar una condición suelta (útil para tests de tabla)");
    }
}

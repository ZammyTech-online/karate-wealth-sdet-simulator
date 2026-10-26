using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ContractCheckRequest(string? Service, string? Path, bool? AsService);

/// <summary>
/// contract-check: ¿lo que responde un servicio es lo que dice su OpenAPI publicado? Descarga /swagger/v1/swagger.json del servicio, busca la
/// operación GET de la ruta (con plantillas {id}), resuelve el esquema de la respuesta 200 y lo compara con la respuesta real: campos
/// obligatorios que faltan, campos sin documentar y tipos distintos. Detecta la deriva de contratos (la trampa T9, por ejemplo).
/// </summary>
public sealed class ContractCheck : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "contract-check", "Q-quality", 7603, 4, "Calidad y test",
        "Compara el OpenAPI publicado de un servicio con su respuesta real: obligatorios ausentes, campos sin documentar y tipos distintos (deriva de contratos).",
        DependsOn: new[] { "clock-service" });

    public static bool PathMatches(string template, string path)
    {
        var t = template.Trim('/').Split('/'); var p = path.Split('?')[0].Trim('/').Split('/');
        return t.Length == p.Length && t.Zip(p).All(x => x.First.StartsWith('{') || string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase));
    }

    public static string JsonType(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => "string", JsonValueKind.Number => v.TryGetInt64(out _) ? "integer" : "number", JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array", JsonValueKind.Object => "object", _ => "null",
    };

    /// <summary>Compara un valor con un esquema OpenAPI (recursivo en objetos y arrays). Devuelve las diferencias con su ruta JSON.</summary>
    public static void Compare(JsonElement schema, JsonElement value, JsonElement doc, string at, List<object> diffs, int depth = 0)
    {
        if (depth > 6) return;
        schema = Resolve(schema, doc);
        var type = schema.TryGetProperty("type", out var t) ? t.GetString() : schema.TryGetProperty("properties", out _) ? "object" : null;
        var nullable = schema.TryGetProperty("nullable", out var n) && n.GetBoolean();
        if (value.ValueKind == JsonValueKind.Null) { if (!nullable && type is not null) diffs.Add(new { path = at, kind = "UNEXPECTED_NULL", expected = type }); return; }
        if (type is null) return;
        var actual = JsonType(value);
        var compatible = type == actual || (type == "number" && actual == "integer");
        if (!compatible) { diffs.Add(new { path = at, kind = "TYPE_MISMATCH", expected = type, actual }); return; }
        if (type == "object" && schema.TryGetProperty("properties", out var props))
        {
            var required = schema.TryGetProperty("required", out var rq) ? rq.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();
            foreach (var pr in props.EnumerateObject())
            {
                if (value.TryGetProperty(pr.Name, out var v)) Compare(pr.Value, v, doc, $"{at}.{pr.Name}", diffs, depth + 1);
                else diffs.Add(new { path = $"{at}.{pr.Name}", kind = "MISSING_FIELD", expected = required.Contains(pr.Name) ? "obligatorio" : "presente (puede ser null, pero no ausente: trampa T10)" });
            }
            foreach (var f in value.EnumerateObject().Where(f => !props.TryGetProperty(f.Name, out _))) diffs.Add(new { path = $"{at}.{f.Name}", kind = "UNDOCUMENTED_FIELD", expected = "no documentado" });
        }
        if (type == "array" && schema.TryGetProperty("items", out var items))
            foreach (var (el, i) in value.EnumerateArray().Take(5).Select((e, i) => (e, i))) Compare(items, el, doc, $"{at}[{i}]", diffs, depth + 1);
    }

    private static JsonElement Resolve(JsonElement schema, JsonElement doc)
    {
        for (var i = 0; i < 10 && schema.TryGetProperty("$ref", out var r); i++)
        {
            var parts = r.GetString()!.TrimStart('#', '/').Split('/');
            var cur = doc;
            foreach (var part in parts) cur = cur.GetProperty(part);
            schema = cur;
        }
        return schema;
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS checks (id TEXT PRIMARY KEY, service TEXT, path TEXT, status TEXT, diffs INTEGER, report TEXT, at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/checks", async (HttpContext http, ContractCheckRequest b) =>
        {
            if (string.IsNullOrWhiteSpace(b.Service) || string.IsNullOrWhiteSpace(b.Path) || !b.Path.StartsWith('/'))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "service y path (empieza por /) son obligatorios.", "Ejemplo: { \"service\": \"product-catalog\", \"path\": \"/api/v1/funds/SE0000000001\" }");
            if (!ServiceDirectory.Knows(b.Service)) return Problems.NotFound(http, "SERVICE_NOT_FOUND", $"No hay ningún servicio llamado {b.Service}.", null);
            var baseUrl = ServiceDirectory.UrlOf(b.Service);
            DownstreamReply spec;
            try { spec = await Downstream.SendUrl(HttpMethod.Get, baseUrl + "/swagger/v1/swagger.json", null, null, TimeSpan.FromSeconds(5)); }
            catch (DependencyUnavailableException) { return Problems.Of(http, 503, "SERVICE_DOWN", "Servicio apagado", $"{b.Service} no responde: ¿está en tu perfil?", null); }
            var doc = spec.Json;
            var op = doc.GetProperty("paths").EnumerateObject().FirstOrDefault(p => PathMatches(p.Name, b.Path));
            if (op.Value.ValueKind != JsonValueKind.Object || !op.Value.TryGetProperty("get", out var get))
                return Problems.Unprocessable(http, "OPERATION_NOT_DOCUMENTED", $"El OpenAPI de {b.Service} no documenta GET {b.Path}.", null);
            JsonElement? schema = get.TryGetProperty("responses", out var resp) && resp.TryGetProperty("200", out var ok) && ok.TryGetProperty("content", out var content)
                && content.EnumerateObject().FirstOrDefault().Value is { ValueKind: JsonValueKind.Object } media && media.TryGetProperty("schema", out var sch) ? sch : null;
            var headers = b.AsService == true ? await ServiceToken.Header(clock) : null;
            var real = await Downstream.SendUrl(HttpMethod.Get, baseUrl + b.Path, null, headers, TimeSpan.FromSeconds(10));
            var diffs = new List<object>();
            string status;
            if (!real.Ok) status = "ENDPOINT_ERROR";
            else if (schema is null) status = "NO_SCHEMA";
            else
            {
                if (real.Body is { } body) Compare(schema.Value, body, doc, "$", diffs); else diffs.Add(new { path = "$", kind = "NOT_JSON", expected = "JSON" });
                status = diffs.Count == 0 ? "MATCHES" : "DRIFT";
            }
            var id = "cc_" + Guid.NewGuid().ToString("N")[..12];
            var report = new { id, service = b.Service, path = b.Path, operation = op.Name, status, httpStatus = real.Status, differences = diffs, checkedAt = Nordic.Iso(clock.LocalNow) };
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO checks VALUES (@id, @Service, @Path, @status, @n, @r, @at)", new { id, b.Service, b.Path, status, n = diffs.Count, r = JsonSerializer.Serialize(report, Canonical.Web), at = Nordic.Iso(clock.LocalNow) });
            return Results.Ok(report);
        }).WithTags("Contratos").WithSummary("Comparar el OpenAPI de un servicio con su respuesta real");

        app.MapGet("/api/v1/checks", (string? service, string? status) =>
        {
            using var db = ctx.OpenDb();
            var items = db.Query<string>("SELECT report FROM checks WHERE (@service IS NULL OR service = @service) AND (@status IS NULL OR status = @status) ORDER BY at, rowid", new { service, status })
                .Select(r => JsonDocument.Parse(r).RootElement).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Contratos").WithSummary("Comprobaciones hechas");
    }
}

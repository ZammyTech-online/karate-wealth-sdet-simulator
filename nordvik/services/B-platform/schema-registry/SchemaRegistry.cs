using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record SchemaDef(string[]? Required, Dictionary<string, string>? Properties);
public sealed record SchemaVersionRequest(SchemaDef? Schema, string? Compatibility);
public sealed record ValidateRequest(JsonElement? Payload, int? Version);

/// <summary>
/// schema-registry: versiones de los esquemas de los eventos (un "subject" por topic). Un esquema es { required: [...], properties: { campo: tipo } }
/// con tipos string, number, integer, boolean, object, array. Compatibilidad BACKWARD por defecto: una versión nueva no puede añadir campos
/// obligatorios, quitar campos ni cambiar tipos (los consumidores antiguos se romperían) → 409 INCOMPATIBLE_SCHEMA con los motivos.
/// </summary>
public sealed class SchemaRegistry : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "schema-registry", "B-platform", 6111, 4, "Plataforma",
        "Versiones de esquemas de eventos por topic con compatibilidad BACKWARD/FORWARD/NONE, validación de payloads y razones de incompatibilidad.",
        DependsOn: new[] { "clock-service" });

    public static readonly string[] Types = { "string", "number", "integer", "boolean", "object", "array" };

    private static readonly (string Topic, SchemaDef Schema)[] Seed =
    {
        ("payment.received", new(new[] { "paymentId", "customerId", "amount" }, new() { ["paymentId"] = "string", ["customerId"] = "string", ["amount"] = "string", ["reference"] = "string" })),
        ("order.executed", new(new[] { "orderId", "portfolioId", "units" }, new() { ["orderId"] = "string", ["portfolioId"] = "string", ["units"] = "string", ["nav"] = "string" })),
        ("contribution.received", new(new[] { "contributionId", "planId", "amount", "source" }, new() { ["contributionId"] = "string", ["planId"] = "string", ["customerId"] = "string", ["amount"] = "string", ["source"] = "string", ["period"] = "string" })),
    };

    /// <summary>Razones por las que 'next' rompe a quien lee con 'prev' (BACKWARD) o al revés (FORWARD).</summary>
    public static List<string> Incompatibilities(SchemaDef prev, SchemaDef next, string mode)
    {
        var reasons = new List<string>();
        var pp = prev.Properties ?? new(); var np = next.Properties ?? new();
        var pr = prev.Required ?? Array.Empty<string>(); var nr = next.Required ?? Array.Empty<string>();
        if (mode is "BACKWARD" or "FULL")
        {
            foreach (var f in nr.Except(pr)) reasons.Add($"campo obligatorio nuevo: {f}");
            foreach (var f in pp.Keys.Except(np.Keys)) reasons.Add($"campo eliminado: {f}");
        }
        if (mode is "FORWARD" or "FULL")
            foreach (var f in pr.Except(nr)) reasons.Add($"un campo obligatorio deja de serlo: {f}");
        if (mode != "NONE")
            foreach (var f in pp.Keys.Intersect(np.Keys).Where(k => pp[k] != np[k])) reasons.Add($"tipo cambiado en {f}: {pp[f]} → {np[f]}");
        return reasons;
    }

    public static List<string> Validate(SchemaDef s, JsonElement payload)
    {
        var errors = new List<string>();
        if (payload.ValueKind != JsonValueKind.Object) { errors.Add("el payload no es un objeto"); return errors; }
        foreach (var r in s.Required ?? Array.Empty<string>()) if (!payload.TryGetProperty(r, out _)) errors.Add($"falta {r}");
        foreach (var (k, t) in s.Properties ?? new())
            if (payload.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null)
            {
                var ok = t switch
                {
                    "string" => v.ValueKind == JsonValueKind.String, "number" => v.ValueKind == JsonValueKind.Number, "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
                    "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False, "object" => v.ValueKind == JsonValueKind.Object, "array" => v.ValueKind == JsonValueKind.Array, _ => true,
                };
                if (!ok) errors.Add($"{k} debería ser {t}");
            }
        return errors;
    }

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS versions (subject TEXT, version INTEGER, schema TEXT, compatibility TEXT, created_at TEXT, PRIMARY KEY (subject, version))");
        foreach (var (t, s) in Seed)
            db.Execute("INSERT OR IGNORE INTO versions VALUES (@t, 1, @s, 'BACKWARD', @at)", new { t, s = JsonSerializer.Serialize(s, Canonical.Web), at = DateTimeOffset.UtcNow.ToString("O") });
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        (int Version, SchemaDef Schema, string Mode)? Latest(Microsoft.Data.Sqlite.SqliteConnection db, string subject, int? version = null)
        {
            var r = db.QuerySingleOrDefault<(long V, string S, string C)>("SELECT version, schema, compatibility FROM versions WHERE subject = @subject AND (@version IS NULL OR version = @version) ORDER BY version DESC LIMIT 1", new { subject, version });
            return r.S is null ? null : ((int)r.V, JsonSerializer.Deserialize<SchemaDef>(r.S, Canonical.Web)!, r.C);
        }
        object View(string subject, int v, SchemaDef s, string mode) => new { subject, version = v, compatibility = mode, schema = new { required = s.Required ?? Array.Empty<string>(), properties = s.Properties ?? new() } };

        app.MapGet("/api/v1/subjects", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(new { items = db.Query<(string S, long V)>("SELECT subject, MAX(version) FROM versions GROUP BY subject ORDER BY subject").Select(x => new { subject = x.S, latestVersion = x.V }) });
        }).WithTags("Esquemas").WithSummary("Subjects (topics) registrados y su última versión");

        app.MapGet("/api/v1/subjects/{subject}/versions/{version}", (HttpContext http, string subject, string version) =>
        {
            using var db = ctx.OpenDb();
            int? v = version == "latest" ? null : int.TryParse(version, out var n) ? n : -1;
            return Latest(db, subject, v) is { } l ? Results.Ok(View(subject, l.Version, l.Schema, l.Mode))
                : Problems.NotFound(http, "SCHEMA_NOT_FOUND", $"No existe la versión {version} de {subject}.", null);
        }).WithTags("Esquemas").WithSummary("Una versión (o 'latest')");

        app.MapPost("/api/v1/subjects/{subject}/versions", (HttpContext http, string subject, SchemaVersionRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin", "Operations", "Service" }) is { } denied) return denied;
            if (!System.Text.RegularExpressions.Regex.IsMatch(subject, @"^[a-z][a-z0-9-]*(\.[a-z0-9-]+)+$")) return Problems.BadRequest(http, "VALIDATION_FAILED", "El subject es un topic: dominio.entidad.hecho.", null);
            var s = b.Schema;
            var errors = new List<FieldError>();
            if (s?.Properties is not { Count: > 0 }) errors.Add(new FieldError("schema.properties", "al menos un campo"));
            else
            {
                foreach (var (k, t) in s.Properties) if (!Types.Contains(t)) errors.Add(new FieldError($"schema.properties.{k}", string.Join(", ", Types)));
                foreach (var r in s.Required ?? Array.Empty<string>()) if (!s.Properties.ContainsKey(r)) errors.Add(new FieldError("schema.required", $"{r} no está en properties"));
            }
            using var db = ctx.OpenDb();
            var prev = Latest(db, subject);
            var mode = (b.Compatibility ?? prev?.Mode ?? "BACKWARD").ToUpperInvariant();
            if (mode is not ("BACKWARD" or "FORWARD" or "FULL" or "NONE")) errors.Add(new FieldError("compatibility", "BACKWARD, FORWARD, FULL o NONE"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Esquema no válido.", "Ejemplo: { \"schema\": { \"required\": [\"id\"], \"properties\": { \"id\": \"string\", \"amount\": \"string\" } } }", errors);
            if (prev is { } p)
            {
                var reasons = Incompatibilities(p.Schema, s!, mode);
                if (reasons.Count > 0)
                {
                    var body = Problems.BodyOf(http, 409, "INCOMPATIBLE_SCHEMA", "Esquema incompatible", $"La versión nueva rompe la compatibilidad {mode} con la v{p.Version}.",
                        "Añade campos como opcionales; no quites ni cambies tipos. O publica un topic nuevo (…v2).");
                    body["reasons"] = reasons;
                    return Results.Json(body, statusCode: 409, contentType: "application/problem+json");
                }
                if (JsonSerializer.Serialize(p.Schema, Canonical.Web) == JsonSerializer.Serialize(s, Canonical.Web)) return Results.Ok(View(subject, p.Version, p.Schema, p.Mode));
            }
            var v = (prev?.Version ?? 0) + 1;
            db.Execute("INSERT INTO versions VALUES (@subject, @v, @s, @mode, @at)", new { subject, v, s = JsonSerializer.Serialize(s, Canonical.Web), mode, at = Nordic.Iso(clock.LocalNow) });
            return Results.Created($"/api/v1/subjects/{subject}/versions/{v}", View(subject, v, s!, mode));
        }).WithTags("Esquemas").WithSummary("Registrar una versión nueva (se comprueba la compatibilidad)");

        app.MapPost("/api/v1/subjects/{subject}/validate", (HttpContext http, string subject, ValidateRequest b) =>
        {
            using var db = ctx.OpenDb();
            if (Latest(db, subject, b.Version) is not { } l) return Problems.NotFound(http, "SCHEMA_NOT_FOUND", $"No hay esquema para {subject}.", null);
            if (b.Payload is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta payload.", null);
            var errors = Validate(l.Schema, b.Payload.Value);
            return Results.Ok(new { subject, version = l.Version, valid = errors.Count == 0, errors });
        }).WithTags("Esquemas").WithSummary("Validar un payload contra una versión (por defecto, la última)");
    }
}

using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateSequenceRequest(string? Name, string? Prefix, int? Padding);
public sealed record Sequence(string Name, string Prefix, long Padding, long Current, string CreatedAt);
public sealed record SequenceValues(string Name, IReadOnlyList<string> Values, long Current);

/// <summary>sequence-service: identificadores correlativos legibles, por ejemplo RUN-0001, RUN-0002.</summary>
public sealed class SequenceService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "sequence-service", "A-utilities", 6002, 0, "Plataforma de datos de prueba",
        "Secuencias con nombre que devuelven identificadores correlativos con prefijo y relleno de ceros.",
        DependsOn: new[] { "clock-service" });

    private static readonly Regex NameRule = new("^[a-z0-9][a-z0-9-]{1,40}$");
    private static readonly object Gate = new();

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS sequences (name TEXT PRIMARY KEY, prefix TEXT NOT NULL, padding INTEGER NOT NULL, current INTEGER NOT NULL, created_at TEXT NOT NULL)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/sequences").WithTags("Secuencias");

        g.MapGet("", () =>
        {
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<Sequence>("SELECT name, prefix, padding, current, created_at AS CreatedAt FROM sequences ORDER BY name").ToList());
        }).WithSummary("Listar secuencias").Produces<List<Sequence>>();

        g.MapPost("", (HttpContext http, IClock clock, CreateSequenceRequest body) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(body.Name) || !NameRule.IsMatch(body.Name))
                errors.Add(new FieldError("name", "obligatorio; minúsculas, números y guiones, de 2 a 41 caracteres"));
            var padding = body.Padding ?? 4;
            if (padding is < 1 or > 12) errors.Add(new FieldError("padding", "entre 1 y 12"));
            var prefix = body.Prefix ?? "";
            if (prefix.Length > 20) errors.Add(new FieldError("prefix", "máximo 20 caracteres"));
            if (errors.Count > 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "La secuencia no es válida.",
                    "Ejemplo correcto: { \"name\": \"clientes\", \"prefix\": \"CLI\", \"padding\": 4 }", errors);

            lock (Gate)
            {
                using var db = ctx.OpenDb();
                if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM sequences WHERE name = @n", new { n = body.Name }) > 0)
                    return Problems.Conflict(http, "SEQUENCE_ALREADY_EXISTS", $"Ya existe la secuencia '{body.Name}'.",
                        "Usa un nombre único por ejecución (por ejemplo con un UUID de uuid-service) o borra la anterior con DELETE.");
                var created = Nordic.Iso(clock.LocalNow);
                db.Execute("INSERT INTO sequences (name, prefix, padding, current, created_at) VALUES (@n, @p, @pad, 0, @c)",
                    new { n = body.Name, p = prefix, pad = padding, c = created });
                var seq = new Sequence(body.Name!, prefix, padding, 0, created);
                return Results.Created($"/api/v1/sequences/{body.Name}", seq);
            }
        }).WithSummary("Crear una secuencia").Produces<Sequence>(201);

        g.MapGet("/{name}", (HttpContext http, string name) =>
        {
            using var db = ctx.OpenDb();
            var seq = db.QuerySingleOrDefault<Sequence>("SELECT name, prefix, padding, current, created_at AS CreatedAt FROM sequences WHERE name = @name", new { name });
            return seq is null ? NotFound(http, name) : Results.Ok(seq);
        }).WithSummary("Ver una secuencia").Produces<Sequence>();

        g.MapPost("/{name}/next", (HttpContext http, string name, int? count) =>
        {
            var n = count ?? 1;
            if (n is < 1 or > 100)
                return Problems.BadRequest(http, "COUNT_OUT_OF_RANGE", $"count={n} está fuera del rango 1-100.", "Pide entre 1 y 100 valores por llamada.");
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                using var tx = db.BeginTransaction();
                var seq = db.QuerySingleOrDefault<Sequence>("SELECT name, prefix, padding, current, created_at AS CreatedAt FROM sequences WHERE name = @name", new { name }, tx);
                if (seq is null) return NotFound(http, name);
                var values = Enumerable.Range(1, n).Select(i => Format(seq, seq.Current + i)).ToList();
                db.Execute("UPDATE sequences SET current = current + @n WHERE name = @name", new { n, name }, tx);
                tx.Commit();
                return Results.Ok(new SequenceValues(name, values, seq.Current + n));
            }
        }).WithSummary("Obtener el siguiente valor (o varios)").Produces<SequenceValues>();

        g.MapPost("/{name}/reset", (HttpContext http, string name) =>
        {
            using var db = ctx.OpenDb();
            return db.Execute("UPDATE sequences SET current = 0 WHERE name = @name", new { name }) == 0 ? NotFound(http, name) : Results.NoContent();
        }).WithSummary("Reiniciar a cero");

        g.MapDelete("/{name}", (HttpContext http, string name) =>
        {
            using var db = ctx.OpenDb();
            return db.Execute("DELETE FROM sequences WHERE name = @name", new { name }) == 0 ? NotFound(http, name) : Results.NoContent();
        }).WithSummary("Borrar una secuencia");
    }

    private static string Format(Sequence s, long value)
    {
        var number = value.ToString().PadLeft((int)s.Padding, '0');
        return string.IsNullOrEmpty(s.Prefix) ? number : $"{s.Prefix}-{number}";
    }

    private static IResult NotFound(HttpContext http, string name) =>
        Problems.NotFound(http, "SEQUENCE_NOT_FOUND", $"No existe la secuencia '{name}'.",
            "Primero créala con POST /api/v1/sequences y un cuerpo { \"name\": \"" + name + "\" }.");
}

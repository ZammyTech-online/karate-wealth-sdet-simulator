using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record CreateSeedRequest(string? Label);
public sealed record Seed(int Value, string? Label, string CreatedAt);
public sealed record RandomInts(int Seed, int Count, int Min, int Max, IReadOnlyList<int> Values);
public sealed record RandomDecimals(int Seed, int Count, string Min, string Max, int Scale, IReadOnlyList<string> Values);
public sealed record RandomPick(int Seed, int Count, bool Unique, IReadOnlyList<string> Values);

/// <summary>random-seed-service: aleatoriedad reproducible. Misma semilla = mismos datos, siempre.</summary>
public sealed class RandomSeedService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "random-seed-service", "A-utilities", 6004, 0, "Plataforma de datos de prueba",
        "Números y elecciones aleatorias reproducibles a partir de una semilla, para repetir un fallo con los mismos datos.",
        DependsOn: new[] { "clock-service" });

    private readonly ConcurrentDictionary<int, Seed> _seeds = new();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/seeds", (IClock clock, CreateSeedRequest? body) =>
        {
            var value = Random.Shared.Next(1, int.MaxValue);
            var seed = new Seed(value, body?.Label, Nordic.Iso(clock.LocalNow));
            _seeds[value] = seed;
            return Results.Created($"/api/v1/seeds/{value}", seed);
        }).WithTags("Semillas").WithSummary("Crear una semilla nueva").Produces<Seed>(201);

        app.MapGet("/api/v1/seeds", () => Results.Ok(_seeds.Values.OrderBy(s => s.CreatedAt).ToList()))
            .WithTags("Semillas").WithSummary("Listar semillas creadas desde el arranque").Produces<List<Seed>>();

        var g = app.MapGroup("/api/v1/random").WithTags("Aleatorio");

        g.MapGet("/ints", (HttpContext http, int? seed, int? count, int? min, int? max) =>
        {
            if (seed is null) return MissingSeed(http);
            var n = count ?? 1; var lo = min ?? 0; var hi = max ?? 100;
            if (n is < 1 or > 1000) return Count(http, n);
            if (lo >= hi) return Problems.BadRequest(http, "INVALID_RANGE", $"min ({lo}) debe ser menor que max ({hi}).", "Ejemplo: ?seed=42&min=1&max=100");
            var rnd = new Random(seed.Value);
            var values = Enumerable.Range(0, n).Select(_ => rnd.Next(lo, hi + 1)).ToList();
            return Results.Ok(new RandomInts(seed.Value, n, lo, hi, values));
        }).WithSummary("Enteros reproducibles (max incluido)").Produces<RandomInts>();

        g.MapGet("/decimals", (HttpContext http, int? seed, int? count, string? min, string? max, int? scale) =>
        {
            if (seed is null) return MissingSeed(http);
            var n = count ?? 1; var s = scale ?? 2;
            if (n is < 1 or > 1000) return Count(http, n);
            if (!Money.TryParse(min ?? "0", out var lo) || !Money.TryParse(max ?? "1000", out var hi) || lo >= hi)
                return Problems.BadRequest(http, "INVALID_RANGE", "min y max deben ser decimales con punto y min < max.", "Ejemplo: ?seed=42&min=100.00&max=5000.00&scale=2");
            if (s is < 0 or > 6) return Problems.BadRequest(http, "INVALID_SCALE", "scale debe estar entre 0 y 6.", "scale es el número de decimales.");
            var rnd = new Random(seed.Value);
            var values = Enumerable.Range(0, n).Select(_ => Money.Format(lo + (hi - lo) * (decimal)rnd.NextDouble(), s)).ToList();
            return Results.Ok(new RandomDecimals(seed.Value, n, Money.Format(lo, s), Money.Format(hi, s), s, values));
        }).WithSummary("Decimales reproducibles como texto").Produces<RandomDecimals>();

        g.MapGet("/pick", (HttpContext http, int? seed, string? items, int? count, bool? unique) =>
        {
            if (seed is null) return MissingSeed(http);
            var options = (items ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var n = count ?? 1; var u = unique ?? false;
            if (options.Length == 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "items está vacío.", "Ejemplo: ?seed=7&items=EQUITY,FIXED_INCOME,MIXED&count=2");
            if (n is < 1 or > 1000) return Count(http, n);
            if (u && n > options.Length)
                return Problems.Unprocessable(http, "NOT_ENOUGH_ITEMS", $"Pides {n} valores únicos de solo {options.Length} opciones.", "Con unique=true, count no puede superar el número de items.");
            var rnd = new Random(seed.Value);
            var values = u
                ? options.OrderBy(_ => rnd.Next()).Take(n).ToList()
                : Enumerable.Range(0, n).Select(_ => options[rnd.Next(options.Length)]).ToList();
            return Results.Ok(new RandomPick(seed.Value, n, u, values));
        }).WithSummary("Elegir elementos de una lista de forma reproducible").Produces<RandomPick>();
    }

    private static IResult MissingSeed(HttpContext http) =>
        Problems.BadRequest(http, "SEED_REQUIRED", "Falta el parámetro seed.",
            "Crea una con POST /api/v1/seeds o usa cualquier entero, por ejemplo ?seed=42. Con la misma semilla obtendrás siempre los mismos valores.");

    private static IResult Count(HttpContext http, int n) =>
        Problems.BadRequest(http, "COUNT_OUT_OF_RANGE", $"count={n} está fuera del rango 1-1000.", "Pide entre 1 y 1000 valores.");
}

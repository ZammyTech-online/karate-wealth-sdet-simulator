using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// pricefeed-sim · sistema externo E5 (simulado): proveedor de NAV. Exige X-Api-Key, publica a las 18:00 de cada día hábil
/// y responde 304 si mandas If-None-Match con el ETag que ya tienes (ahorra descargas).
/// </summary>
public sealed class PriceFeedSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "pricefeed-sim", "X-external", 7705, 2, "Sistema externo (simulado)",
        "E5 Proveedor de precios: NAV por fondo y fecha, descarga masiva por día, ETag/304 y API key.",
        DependsOn: new[] { "clock-service" });

    public const string ApiKey = "feed-key-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private static IResult Error(int status, string error, string message) => Results.Json(new { error, message }, statusCode: status);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var g = app.MapGroup("/feed/v1").WithTags("Feed de precios v1");

        IResult? Guard(HttpContext http) => http.Request.Headers["X-Api-Key"] != ApiKey ? Error(401, "UNAUTHORIZED", "Missing or invalid X-Api-Key") : null;

        bool ResolveDate(IClock clock, string? date, out DateOnly d, out IResult? problem)
        {
            problem = null;
            d = NavMath.LastPublishedDate(clock.LocalNow);
            if (date is not null && !Money.TryParseDate(date, out d)) { problem = Error(400, "INVALID_DATE", "date must be yyyy-MM-dd"); return false; }
            if (!NordicCalendar.IsTradingDay("XSTO", d)) { problem = Error(404, "NO_TRADING", "not a trading day"); return false; }
            if (d < NavMath.First || d > NavMath.Last) { problem = Error(404, "OUT_OF_RANGE", "no data for that date"); return false; }
            if (d > NavMath.LastPublishedDate(clock.LocalNow)) { problem = Error(404, "NOT_PUBLISHED", "NAV for that date is published at 18:00 CET"); return false; }
            return true;
        }

        g.MapGet("/nav/{isin}", async (HttpContext http, IClock clock, string isin, string? date) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Guard(http) is { } denied) return denied;
            if (!NavMath.Exists(isin)) return Error(404, "UNKNOWN_INSTRUMENT", "unknown isin");
            if (!ResolveDate(clock, date, out var d, out var problem)) return problem!;
            var etag = $"\"{isin}-{Money.Date(d)}-v1\"";
            http.Response.Headers.ETag = etag;
            if (http.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(304);
            var fund = FundSeed.All.First(f => f.Isin == isin);
            return Results.Ok(new { isin, date = Money.Date(d), nav = NavMath.Series(isin)[d], currency = fund.Currency, publishedAt = NavMath.PublishedAt(d) });
        }).WithSummary("NAV de un fondo (fecha opcional; por defecto el último publicado)");

        g.MapGet("/nav", async (HttpContext http, IClock clock, string? date) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Guard(http) is { } denied) return denied;
            if (!ResolveDate(clock, date, out var d, out var problem)) return problem!;
            var etag = $"\"all-{Money.Date(d)}-v1\"";
            http.Response.Headers.ETag = etag;
            if (http.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(304);
            return Results.Ok(new
            {
                date = Money.Date(d), publishedAt = NavMath.PublishedAt(d),
                prices = FundSeed.All.Select(f => new { isin = f.Isin, nav = NavMath.Series(f.Isin)[d], currency = f.Currency }).ToList(),
            });
        }).WithSummary("Todos los NAV de un día");
    }
}

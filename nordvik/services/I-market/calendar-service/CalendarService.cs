using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Market(string Code, string Name, string Country, string Currency);
public sealed record HolidayDto(string Date, string Name, string Weekday);
public sealed record HolidayList(string Market, int Year, int Count, IReadOnlyList<HolidayDto> Holidays);
public sealed record TradingDayCheck(string Market, string Date, string Weekday, bool TradingDay, string? Reason, string? HolidayName);
public sealed record TradingDays(string Market, string From, string To, int Count, IReadOnlyList<string> Days);
public sealed record NextTradingDay(string Market, string From, int Offset, string Date);
public sealed record MarketToday(string Market, string Today, string LocalTime, bool TradingDay, string CutOff, bool BeforeCutOff, string NextTradingDay);

/// <summary>calendar-service: días hábiles y festivos de los mercados nórdicos. Equipo de Plataforma, camelCase.</summary>
public sealed class CalendarService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "calendar-service", "I-market", 6804, 0, "Plataforma",
        "Calendario bursátil de XSTO, XOSL, XCSE y XHEL: festivos, días hábiles, T+N y hora de corte de las 15:00.",
        DependsOn: new[] { "clock-service" });

    private static readonly TimeSpan CutOff = new(15, 0, 0);

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/markets", () => Results.Ok(NordicCalendar.Markets.Select(m => new Market(m.Key, m.Value.Name, m.Value.Country, m.Value.Currency)).ToList()))
            .WithTags("Mercados").WithSummary("Mercados disponibles").Produces<List<Market>>();

        var g = app.MapGroup("/api/v1/calendar/{market}").WithTags("Calendario");

        g.MapGet("/holidays", (HttpContext http, IClock clock, string market, int? year) =>
        {
            if (!CheckMarket(http, market, out var p)) return p!;
            var y = year ?? clock.Today.Year;
            if (y is < NordicCalendar.MinYear or > NordicCalendar.MaxYear) return YearOut(http, y);
            var list = NordicCalendar.Holidays(market, y).Select(h => new HolidayDto(Money.Date(h.Date), h.Name, h.Date.DayOfWeek.ToString())).ToList();
            return Results.Ok(new HolidayList(market, y, list.Count, list));
        }).WithSummary("Festivos de un año").Produces<HolidayList>();

        g.MapGet("/is-trading-day", (HttpContext http, IClock clock, string market, string? date) =>
        {
            if (!CheckMarket(http, market, out var p)) return p!;
            if (!ParseDate(http, clock, date, out var d, out p)) return p!;
            var holiday = NordicCalendar.HolidayOn(market, d);
            var weekend = NordicCalendar.IsWeekend(d);
            var reason = weekend ? "WEEKEND" : holiday is not null ? "HOLIDAY" : null;
            return Results.Ok(new TradingDayCheck(market, Money.Date(d), d.DayOfWeek.ToString(), reason is null, reason, holiday?.Name));
        }).WithSummary("¿Es día hábil? (por defecto, hoy según el reloj virtual)").Produces<TradingDayCheck>();

        g.MapGet("/trading-days", (HttpContext http, string market, string? from, string? to) =>
        {
            if (!CheckMarket(http, market, out var p)) return p!;
            if (!Money.TryParseDate(from, out var f) || !Money.TryParseDate(to, out var t))
                return Problems.BadRequest(http, "INVALID_DATE", "from y to son obligatorios con formato AAAA-MM-DD.", "Ejemplo: ?from=2026-12-01&to=2026-12-31");
            if (f > t) return Problems.BadRequest(http, "INVALID_RANGE", "from es posterior a to.", "Intercambia las fechas.");
            if (t.DayNumber - f.DayNumber > 400) return Problems.BadRequest(http, "RANGE_TOO_LARGE", "El rango máximo es de 400 días.", "Divide la consulta.");
            if (f.Year < NordicCalendar.MinYear || t.Year > NordicCalendar.MaxYear) return YearOut(http, f.Year < NordicCalendar.MinYear ? f.Year : t.Year);
            var days = new List<string>();
            for (var d = f; d <= t; d = d.AddDays(1)) if (NordicCalendar.IsTradingDay(market, d)) days.Add(Money.Date(d));
            return Results.Ok(new TradingDays(market, Money.Date(f), Money.Date(t), days.Count, days));
        }).WithSummary("Días hábiles entre dos fechas").Produces<TradingDays>();

        g.MapGet("/next-trading-day", (HttpContext http, IClock clock, string market, string? date, int? offset) =>
        {
            if (!CheckMarket(http, market, out var p)) return p!;
            if (!ParseDate(http, clock, date, out var d, out p)) return p!;
            var n = offset ?? 1;
            if (n is < -30 or > 30) return Problems.BadRequest(http, "OFFSET_OUT_OF_RANGE", "offset debe estar entre -30 y 30.", "T+2 es offset=2; el día hábil anterior es offset=-1.");
            return Results.Ok(new NextTradingDay(market, Money.Date(d), n, Money.Date(NordicCalendar.AddTradingDays(market, d, n))));
        }).WithSummary("Día hábil a N días (T+N)").Produces<NextTradingDay>();

        g.MapGet("/today", (HttpContext http, IClock clock, string market) =>
        {
            if (!CheckMarket(http, market, out var p)) return p!;
            var local = clock.LocalNow;
            var today = DateOnly.FromDateTime(local.DateTime);
            var trading = NordicCalendar.IsTradingDay(market, today);
            return Results.Ok(new MarketToday(market, Money.Date(today), local.ToString("HH:mm:ss"), trading, "15:00",
                trading && local.TimeOfDay < CutOff, Money.Date(NordicCalendar.AddTradingDays(market, today, 1))));
        }).WithSummary("Hoy en el mercado según el reloj virtual: ¿hábil? ¿antes de la hora de corte?").Produces<MarketToday>();
    }

    private static bool CheckMarket(HttpContext http, string market, out IResult? problem)
    {
        problem = null;
        if (NordicCalendar.Markets.ContainsKey(market)) return true;
        problem = Problems.NotFound(http, "MARKET_NOT_FOUND", $"El mercado '{market}' no existe.",
            "Usa el código MIC en mayúsculas: XSTO (Estocolmo), XOSL (Oslo), XCSE (Copenhague) o XHEL (Helsinki).");
        return false;
    }

    private static bool ParseDate(HttpContext http, IClock clock, string? date, out DateOnly d, out IResult? problem)
    {
        problem = null;
        if (date is null) { d = clock.Today; return true; }
        if (Money.TryParseDate(date, out d))
        {
            if (d.Year is >= NordicCalendar.MinYear and <= NordicCalendar.MaxYear) return true;
            problem = YearOut(http, d.Year);
            return false;
        }
        problem = Problems.BadRequest(http, "INVALID_DATE", $"'{date}' no es una fecha válida.", "Usa AAAA-MM-DD, por ejemplo 2026-12-24.");
        return false;
    }

    private static IResult YearOut(HttpContext http, int y) =>
        Problems.NotFound(http, "YEAR_OUT_OF_RANGE", $"No hay calendario para {y}.", $"El calendario cubre de {NordicCalendar.MinYear} a {NordicCalendar.MaxYear}.");
}

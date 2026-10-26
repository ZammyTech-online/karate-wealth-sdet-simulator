using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AbsenceRequest(string? From, string? To, string? Reason);
public sealed record Advisor(string Id, string Username, string Name, string Office, string[] Specialties, string[] Languages, string WorkStart, string WorkEnd, string Email);

/// <summary>
/// advisor-directory: quién es quién en el asesoramiento: asesores, oficinas, especialidades, idiomas, horario y ausencias,
/// y qué clientes tiene asignados cada uno (la asignación vive en permission-service; aquí se consulta).
/// </summary>
public sealed class AdvisorDirectory : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "advisor-directory", "G-advice", 6602, 3, "Asesoramiento y comunicación",
        "Directorio de asesores y oficinas: especialidades, idiomas, horario, ausencias y clientes asignados (desde permission-service).",
        DependsOn: new[] { "permission-service", "clock-service" });

    public static readonly Advisor[] Advisors =
    {
        new(SeedIds.AdvisorErik, "asesor.erik", "Erik Johansson", "STO", new[] { "PENSIONS", "FUNDS", "VOICE_ADVICE" }, new[] { "sv", "es", "en" }, "09:00", "17:00", "erik.johansson@nordvik.test"),
        new(SeedIds.AdvisorMaja, "asesora.maja", "Maja Holm", "GBG", new[] { "ESG", "INSURANCE", "FUNDS" }, new[] { "sv", "en" }, "08:00", "16:00", "maja.holm@nordvik.test"),
    };
    public static readonly object[] Offices =
    {
        new { code = "STO", name = "Nordvik Stockholm City", city = "Stockholm", address = "Kungsgatan 12, 111 35 Stockholm" },
        new { code = "GBG", name = "Nordvik Göteborg", city = "Göteborg", address = "Östra Hamngatan 20, 411 09 Göteborg" },
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS absences (id TEXT PRIMARY KEY, advisor_id TEXT, from_date TEXT, to_date TEXT, reason TEXT)");
    }

    private static object View(Microsoft.Data.Sqlite.SqliteConnection db, Advisor a, DateOnly today)
    {
        var abs = db.Query<(string Id, string From, string To, string? Reason)>("SELECT id, from_date, to_date, reason FROM absences WHERE advisor_id = @id ORDER BY from_date", new { id = a.Id })
            .Select(x => new { id = x.Id, from = x.From, to = x.To, reason = x.Reason }).ToList();
        var today_ = Money.Date(today);
        return new
        {
            a.Id, a.Username, a.Name, a.Office, a.Specialties, a.Languages, workingHours = new { start = a.WorkStart, end = a.WorkEnd, timezone = "Europe/Stockholm" }, a.Email,
            absences = abs, absentToday = abs.Any(x => string.CompareOrdinal(x.from, today_) <= 0 && string.CompareOrdinal(x.to, today_) >= 0),
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/advisors", (HttpContext http, string? specialty, string? office, string? language) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            using var db = ctx.OpenDb();
            var items = Advisors.Where(a => (specialty is null || a.Specialties.Contains(specialty.ToUpperInvariant())) && (office is null || a.Office == office.ToUpperInvariant()) && (language is null || a.Languages.Contains(language)))
                .Select(a => View(db, a, clock.Today)).ToList();
            return Results.Ok(new { items, count = items.Count });
        }).WithTags("Asesores").WithSummary("Buscar asesores por especialidad, oficina o idioma");

        app.MapGet("/api/v1/advisors/{id}", (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            var a = Advisors.FirstOrDefault(x => x.Id == id || x.Username == id);
            using var db = ctx.OpenDb();
            return a is null ? Problems.NotFound(http, "ADVISOR_NOT_FOUND", $"No existe el asesor {id}.", null) : Results.Ok(View(db, a, clock.Today));
        }).WithTags("Asesores").WithSummary("Un asesor (por id o por usuario)");

        app.MapGet("/api/v1/advisors/{id}/customers", async (HttpContext http, string id) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Operations", "Compliance", "Admin", "Service" }) is { } denied) return denied;
            var a = Advisors.FirstOrDefault(x => x.Id == id || x.Username == id);
            if (a is null) return Problems.NotFound(http, "ADVISOR_NOT_FOUND", $"No existe el asesor {id}.", null);
            if (p.Role == "Advisor" && p.AdvisorId != a.Id) return Problems.Of(http, 403, "NOT_YOUR_BOOK", "Prohibido", "Un asesor solo ve su propia cartera de clientes.", null);
            var r = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{a.Id}/customers", forwardAuth: false);
            var items = r.Json.EnumerateArray().Select(x => new { customerId = x.GetProperty("customerId").GetString(), assignedAt = x.GetProperty("assignedAt").GetString() }).ToList();
            return Results.Ok(new { advisorId = a.Id, items, count = items.Count });
        }).WithTags("Asesores").WithSummary("Clientes asignados (el propio asesor, Operaciones, Cumplimiento)");

        app.MapGet("/api/v1/customers/{customerId}/advisor", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (p.Role == "Customer" && p.CustomerId != customerId) return Problems.NotFound(http, "CUSTOMER_NOT_FOUND", $"No existe el cliente {customerId}.", null);
            using var db = ctx.OpenDb();
            foreach (var a in Advisors)
            {
                var r = await Downstream.Get(http, "permission-service", $"/api/v1/advisors/{a.Id}/customers", forwardAuth: false);
                if (r.Ok && r.Json.EnumerateArray().Any(x => x.GetProperty("customerId").GetString() == customerId)) return Results.Ok(View(db, a, clock.Today));
            }
            return Problems.NotFound(http, "NO_ADVISOR_ASSIGNED", "Este cliente no tiene asesor asignado.", null);
        }).WithTags("Asesores").WithSummary("¿Quién es el asesor de este cliente?");

        app.MapPost("/api/v1/advisors/{id}/absences", (HttpContext http, string id, AbsenceRequest b) =>
        {
            if (Auth.Require(http, clock, out var p, roles: new[] { "Advisor", "Admin" }) is { } denied) return denied;
            var a = Advisors.FirstOrDefault(x => x.Id == id || x.Username == id);
            if (a is null) return Problems.NotFound(http, "ADVISOR_NOT_FOUND", $"No existe el asesor {id}.", null);
            if (p.Role == "Advisor" && p.AdvisorId != a.Id) return Problems.Of(http, 403, "NOT_YOURSELF", "Prohibido", "Solo registras tus propias ausencias.", null);
            if (!Money.TryParseDate(b.From, out var from) || !Money.TryParseDate(b.To, out var to) || to < from)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "from y to en AAAA-MM-DD, con to >= from.", null);
            var aid = "abs_" + Guid.NewGuid().ToString("N")[..10];
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO absences VALUES (@aid, @id, @f, @t, @Reason)", new { aid, id = a.Id, f = Money.Date(from), t = Money.Date(to), b.Reason });
            return Results.Created($"/api/v1/advisors/{a.Id}", View(db, a, clock.Today));
        }).WithTags("Asesores").WithSummary("Registrar una ausencia (vacaciones, formación): meeting-scheduler no ofrecerá huecos esos días");

        app.MapGet("/api/v1/offices", () => Results.Ok(new { items = Offices })).WithTags("Oficinas").WithSummary("Oficinas");

        app.MapGet("/api/v1/internal/advisors/{id}", (string id) =>
        {
            var a = Advisors.FirstOrDefault(x => x.Id == id || x.Username == id);
            using var db = ctx.OpenDb();
            return a is null ? Results.NotFound() : Results.Ok(View(db, a, clock.Today));
        }).WithTags("Interno").WithSummary("(interno) Asesor con horario y ausencias");
    }
}

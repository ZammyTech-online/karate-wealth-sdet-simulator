using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record EmploymentRequest(string? EmployerName, string? OrgNumber, string? StartDate, string? EndDate, string? MonthlySalary, string? CollectiveAgreement);
public sealed record EndRequest(string? EndDate);

/// <summary>
/// employment-service: empleador actual e historial laboral. Los periodos no pueden solaparse; dar de alta un empleo nuevo cierra el
/// anterior el día antes. El número de organización sueco (NNNNNN-NNNN) se valida con Luhn. El sueldo solo lo ven el cliente,
/// Cumplimiento y servicios: al asesor le sale el tramo.
/// </summary>
public sealed class EmploymentService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "employment-service", "D-customer", 6307, 3, "Cliente",
        "Empleador actual e historial laboral: sin solapes, número de organización con Luhn, convenio colectivo y sueldo visible solo para quien debe (al asesor, el tramo).",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "employment.changed" });

    public static readonly string[] Agreements = { "ITP1", "ITP2", "SAF-LO", "AKAP-KR", "PA16", "NONE" };

    public static bool OrgNumberValid(string? org)
    {
        if (org is null || !System.Text.RegularExpressions.Regex.IsMatch(org, @"^\d{6}-\d{4}$")) return false;
        var digits = org.Replace("-", "");
        if (digits[2] < '2') return false; // el tercer dígito de una persona jurídica es 2 o mayor
        int sum = 0; bool dbl = false;
        for (var i = digits.Length - 1; i >= 0; i--) { var d = digits[i] - '0'; if (dbl) { d *= 2; if (d > 9) d -= 9; } sum += d; dbl = !dbl; }
        return sum % 10 == 0;
    }

    public static string Band(decimal s) => s switch { < 25_000 => "< 25 000", < 40_000 => "25 000 - 39 999", < 60_000 => "40 000 - 59 999", _ => "≥ 60 000" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS employments (id TEXT PRIMARY KEY, customer_id TEXT, employer TEXT, org_number TEXT, start_date TEXT, end_date TEXT, salary TEXT, agreement TEXT, created_at TEXT)");
    }

    private static List<object> List(Microsoft.Data.Sqlite.SqliteConnection db, string customerId, bool showSalary, DateOnly today) =>
        db.Query<(string Id, string Emp, string Org, string Start, string? End, string Salary, string Agr)>(
            "SELECT id, employer, org_number, start_date, end_date, salary, agreement FROM employments WHERE customer_id = @customerId ORDER BY start_date DESC", new { customerId })
          .Select(e => (object)new
          {
              id = e.Id, employerName = e.Emp, orgNumber = e.Org, startDate = e.Start, endDate = e.End,
              current = string.CompareOrdinal(e.Start, Money.Date(today)) <= 0 && (e.End is null || string.CompareOrdinal(e.End, Money.Date(today)) >= 0),
              monthlySalary = showSalary ? e.Salary : null, salaryBand = Band(decimal.Parse(e.Salary, System.Globalization.CultureInfo.InvariantCulture)), collectiveAgreement = e.Agr,
          }).ToList();

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/customers/{customerId}/employments", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var items = List(db, customerId, p.Role is "Customer" or "Compliance" or "Admin" or "Service", clock.Today);
            return Results.Ok(new { customerId, items, count = items.Count });
        }).WithTags("Empleo").WithSummary("Historial laboral (el más reciente primero; al asesor, solo el tramo de sueldo)");

        app.MapPost("/api/v1/customers/{customerId}/employments", async (HttpContext http, string customerId, EmploymentRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.EmployerName)) errors.Add(new FieldError("employerName", "obligatorio"));
            if (!OrgNumberValid(b.OrgNumber)) errors.Add(new FieldError("orgNumber", "NNNNNN-NNNN de una persona jurídica, con dígito de control válido"));
            if (!Money.TryParseDate(b.StartDate, out var start)) errors.Add(new FieldError("startDate", "AAAA-MM-DD"));
            DateOnly? end = null;
            if (b.EndDate is not null) { if (Money.TryParseDate(b.EndDate, out var e)) end = e; else errors.Add(new FieldError("endDate", "AAAA-MM-DD")); }
            if (end is not null && end < start) errors.Add(new FieldError("endDate", "antes del inicio"));
            if (!Money.TryParse(b.MonthlySalary, out var salary) || salary < 0) errors.Add(new FieldError("monthlySalary", "importe como texto, p. ej. \"42000.00\""));
            var agr = (b.CollectiveAgreement ?? "NONE").ToUpperInvariant();
            if (!Agreements.Contains(agr)) errors.Add(new FieldError("collectiveAgreement", string.Join(", ", Agreements)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Empleo no válido.", "Ejemplo: { \"employerName\": \"Volvo AB\", \"orgNumber\": \"556012-5790\", \"startDate\": \"2024-01-01\", \"monthlySalary\": \"42000.00\", \"collectiveAgreement\": \"ITP1\" }", errors);
            using var db = ctx.OpenDb();
            var others = db.Query<(string Id, string Start, string? End)>("SELECT id, start_date, end_date FROM employments WHERE customer_id = @customerId", new { customerId }).ToList();
            var startS = Money.Date(start); var endS = end is null ? "9999-12-31" : Money.Date(end.Value);
            using var tx = db.BeginTransaction();
            foreach (var o in others)
            {
                var oEnd = o.End ?? "9999-12-31";
                if (string.CompareOrdinal(o.Start, endS) > 0 || string.CompareOrdinal(oEnd, startS) < 0) continue;
                if (o.End is null && string.CompareOrdinal(o.Start, startS) < 0)
                    db.Execute("UPDATE employments SET end_date = @e WHERE id = @id", new { e = Money.Date(start.AddDays(-1)), id = o.Id }, tx); // el empleo abierto se cierra el día antes
                else
                {
                    tx.Rollback();
                    return Problems.Unprocessable(http, "EMPLOYMENT_OVERLAP", $"Se solapa con el empleo {o.Id} ({o.Start} - {o.End ?? "actual"}).", "Ajusta las fechas o termina antes el empleo anterior.");
                }
            }
            var id = "emp_" + Guid.NewGuid().ToString("N")[..12];
            db.Execute("INSERT INTO employments VALUES (@id, @customerId, @EmployerName, @OrgNumber, @s, @e, @salary, @agr, @at)",
                new { id, customerId, b.EmployerName, b.OrgNumber, s = startS, e = end is null ? null : endS, salary = Money.Format(salary, 2), agr, at = Nordic.Iso(clock.LocalNow) }, tx);
            Outbox.Add(db, http, clock, "employment.changed", customerId, new { customerId, employmentId = id, orgNumber = b.OrgNumber, collectiveAgreement = agr }, tx);
            tx.Commit();
            var items = List(db, customerId, true, clock.Today);
            return Results.Created($"/api/v1/customers/{customerId}/employments", new { customerId, items, count = items.Count });
        }).WithTags("Empleo").WithSummary("Añadir un empleo (cierra el abierto anterior el día antes del inicio; los solapes son 422)");

        app.MapGet("/api/v1/org-numbers/{orgNumber}/validate", (string orgNumber) => Results.Ok(new { orgNumber, valid = OrgNumberValid(orgNumber) }))
            .WithTags("Empleo").WithSummary("¿Es válido este número de organización?");
    }
}

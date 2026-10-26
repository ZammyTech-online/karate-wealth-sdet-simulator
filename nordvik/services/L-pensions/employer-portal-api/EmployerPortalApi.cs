using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record EmployerRequest(string? OrgNumber, string? Name, string? Agreement);
public sealed record EmployeeRequest(string? PersonalNumber, List<PlanAllocationRequestLine>? Allocation);
public sealed record PlanAllocationRequestLine(string? Isin, decimal Percent);

/// <summary>
/// employer-portal-api: la API de las empresas clientes. Admin da de alta a la empresa y le entrega una API key (solo una vez;
/// aquí se guarda su hash). Con la cabecera X-Employer-Key la empresa da de alta empleados (que ya sean clientes de Nordvik): se crea su
/// plan OCCUPATIONAL con el convenio de la empresa. La empresa ve a sus empleados con el número enmascarado, nunca a los de otra.
/// </summary>
public sealed class EmployerPortalApi : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "employer-portal-api", "L-pensions", 7102, 3, "Pensiones",
        "API de empresas con API key (X-Employer-Key): alta de empleados con su plan OCCUPATIONAL por convenio, consulta enmascarada y aportaciones del mes.",
        DependsOn: new[] { "pension-plan-service", "customer-registry", "contribution-service", "clock-service" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS employers (org_number TEXT PRIMARY KEY, name TEXT, agreement TEXT, key_hash TEXT, created_at TEXT);
                     CREATE TABLE IF NOT EXISTS employees (org_number TEXT, customer_id TEXT, plan_id TEXT, masked TEXT, added_at TEXT, PRIMARY KEY (org_number, customer_id))");
    }

    /// <summary>Número de organización sueco: NNNNNN-NNNN, tercer dígito ≥ 2 y Luhn (igual que employment-service).</summary>
    public static bool OrgNumberValid(string? org)
    {
        if (org is null || !System.Text.RegularExpressions.Regex.IsMatch(org, @"^\d{6}-\d{4}$")) return false;
        var digits = org.Replace("-", "");
        if (digits[2] < '2') return false;
        int sum = 0; bool dbl = false;
        for (var i = digits.Length - 1; i >= 0; i--) { var d = digits[i] - '0'; if (dbl) { d *= 2; if (d > 9) d -= 9; } sum += d; dbl = !dbl; }
        return sum % 10 == 0;
    }

    private static (string Org, string Agreement)? Employer(HttpContext http, ServiceContext ctx)
    {
        var key = http.Request.Headers["X-Employer-Key"].FirstOrDefault();
        if (key is null) return null;
        using var db = ctx.OpenDb();
        var r = db.QuerySingleOrDefault<(string Org, string Agr)>("SELECT org_number, agreement FROM employers WHERE key_hash = @h", new { h = Canonical.Sha256Hex(key) });
        return r.Org is null ? null : (r.Org, r.Agr);
    }

    private static IResult NoKey(HttpContext http) =>
        Problems.Of(http, 401, "EMPLOYER_KEY_REQUIRED", "Empresa no identificada", "Falta X-Employer-Key o no es válida.", "Admin la emite al dar de alta la empresa: POST /api/v1/employers");

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/employers", async (HttpContext http, EmployerRequest b) =>
        {
            if (Auth.Require(http, clock, out _, roles: new[] { "Admin" }) is { } denied) return denied;
            if (b.OrgNumber is null || !OrgNumberValid(b.OrgNumber) || string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrWhiteSpace(b.Agreement))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "orgNumber (válido), name y agreement son obligatorios.", null);
            var a = await Downstream.Get(http, "collective-agreement-rules", $"/api/v1/agreements/{b.Agreement}", forwardAuth: false);
            if (!a.Ok) return Problems.Unprocessable(http, "AGREEMENT_NOT_FOUND", $"No existe el convenio {b.Agreement}.", null);
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM employers WHERE org_number = @o", new { o = b.OrgNumber }) > 0)
                return Problems.Conflict(http, "EMPLOYER_ALREADY_EXISTS", $"La empresa {b.OrgNumber} ya está dada de alta.", null);
            var key = "ek_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            db.Execute("INSERT INTO employers VALUES (@OrgNumber, @Name, @agr, @h, @at)", new { b.OrgNumber, b.Name, agr = b.Agreement.ToUpperInvariant(), h = Canonical.Sha256Hex(key), at = Nordic.Iso(clock.LocalNow) });
            return Results.Created($"/api/v1/employers/{b.OrgNumber}", new { orgNumber = b.OrgNumber, name = b.Name, agreement = b.Agreement.ToUpperInvariant(), apiKey = key, warning = "Guárdala ahora: no se vuelve a mostrar." });
        }).WithTags("Empresas").WithSummary("Alta de una empresa (Admin): devuelve su API key una sola vez");

        app.MapPost("/api/v1/employer/employees", async (HttpContext http, EmployeeRequest b) =>
        {
            if (Employer(http, ctx) is not { } emp) return NoKey(http);
            var check = NationalId.Check(b.PersonalNumber);
            if (!check.Valid) return Problems.BadRequest(http, "VALIDATION_FAILED", "personalNumber no válido.", null);
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/by-national-id/{check.Normalized}", forwardAuth: false);
            if (!cust.Ok) return Problems.Unprocessable(http, "EMPLOYEE_NOT_CUSTOMER", "Esa persona no es cliente de Nordvik: tiene que darse de alta primero.", null);
            var customerId = cust.Str("id")!;
            var alloc = b.Allocation is { Count: > 0 } ? b.Allocation : new List<PlanAllocationRequestLine> { new("SE0000000203", 100) };
            var plan = await Downstream.Send(http, "pension-plan-service", HttpMethod.Post, "/api/v1/pension-plans",
                new { customerId, type = "OCCUPATIONAL", employerOrgNumber = emp.Org, agreement = emp.Agreement, allocation = alloc.Select(l => new { isin = l.Isin, percent = l.Percent }) },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            if (!plan.Ok) return Results.Content(plan.Raw, "application/problem+json", System.Text.Encoding.UTF8, plan.Status);
            using var db = ctx.OpenDb();
            db.Execute("INSERT OR REPLACE INTO employees VALUES (@o, @customerId, @plan, @m, @at)", new { o = emp.Org, customerId, plan = plan.Str("id"), m = NationalId.Mask(check.Normalized!), at = Nordic.Iso(clock.LocalNow) });
            return Results.Created("/api/v1/employer/employees", new { personalNumberMasked = NationalId.Mask(check.Normalized!), planId = plan.Str("id"), agreement = emp.Agreement });
        }).WithTags("Portal de empresas").WithSummary("Dar de alta a un empleado (crea su plan OCCUPATIONAL)");

        app.MapGet("/api/v1/employer/employees", (HttpContext http) =>
        {
            if (Employer(http, ctx) is not { } emp) return NoKey(http);
            using var db = ctx.OpenDb();
            var items = db.Query<(string Plan, string Masked, string At)>("SELECT plan_id, masked, added_at FROM employees WHERE org_number = @o ORDER BY added_at", new { o = emp.Org })
                .Select(x => new { planId = x.Plan, personalNumberMasked = x.Masked, addedAt = x.At }).ToList();
            return Results.Ok(new { employerOrgNumber = emp.Org, items, count = items.Count });
        }).WithTags("Portal de empresas").WithSummary("Mis empleados (enmascarados)");

        app.MapGet("/api/v1/employer/contributions", async (HttpContext http, string? period) =>
        {
            if (Employer(http, ctx) is not { } emp) return NoKey(http);
            using var db = ctx.OpenDb();
            var plans = db.Query<(string Plan, string Masked)>("SELECT plan_id, masked FROM employees WHERE org_number = @o", new { o = emp.Org }).ToList();
            var items = new List<object>(); decimal total = 0;
            foreach (var (plan, masked) in plans)
            {
                var r = await Downstream.Get(http, "contribution-service", $"/api/v1/internal/contributions?planId={plan}&source=EMPLOYER{(period is null ? "" : "&period=" + period)}", forwardAuth: false);
                foreach (var c in r.Json.GetProperty("items").EnumerateArray())
                {
                    var amt = c.GetProperty("amount").GetProperty("amount").GetString()!;
                    total += decimal.Parse(amt, Money.Inv);
                    items.Add(new { personalNumberMasked = masked, period = c.GetProperty("period").GetString(), amount = amt, status = c.GetProperty("status").GetString() });
                }
            }
            return Results.Ok(new { employerOrgNumber = emp.Org, period, items, count = items.Count, total = new { amount = Money.Format(total, 2), currency = "SEK" } });
        }).WithTags("Portal de empresas").WithSummary("Aportaciones de la empresa (por mes)");
    }
}

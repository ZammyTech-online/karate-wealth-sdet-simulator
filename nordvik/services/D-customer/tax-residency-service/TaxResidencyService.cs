using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Residency(string? Country, string? Tin, bool? Primary, string? Since);
public sealed record ResidenciesRequest(Residency[]? Residencies);

/// <summary>
/// tax-residency-service: dónde paga impuestos el cliente y con qué número fiscal (TIN). Exactamente una residencia principal;
/// el formato del TIN se valida por país. Calcula las banderas que necesitan los informes: FATCA (EE. UU.) y CRS (resto de países).
/// </summary>
public sealed class TaxResidencyService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "tax-residency-service", "D-customer", 6308, 3, "Cliente",
        "Residencias fiscales y números fiscales (TIN) con validación por país, una principal obligatoria, y banderas FATCA y CRS.",
        DependsOn: new[] { "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "tax-residency.changed" });

    public static readonly Dictionary<string, (Regex Re, string Example)> Formats = new()
    {
        ["SE"] = (new Regex(@"^\d{8}-\d{4}$"), "19850312-1234 (número nacional)"),
        ["NO"] = (new Regex(@"^\d{11}$"), "11 dígitos"),
        ["DK"] = (new Regex(@"^\d{6}-?\d{4}$"), "DDMMAA-NNNN"),
        ["FI"] = (new Regex(@"^\d{6}[-+A]\d{3}[0-9A-Y]$"), "DDMMAA-NNNC"),
        ["DE"] = (new Regex(@"^\d{11}$"), "11 dígitos"),
        ["ES"] = (new Regex(@"^(\d{8}[A-Z]|[XYZ]\d{7}[A-Z])$"), "12345678Z o X1234567L"),
        ["GB"] = (new Regex(@"^[A-Z]{2}\d{6}[A-D]$"), "QQ123456C"),
        ["US"] = (new Regex(@"^\d{3}-?\d{2}-?\d{4}$"), "123-45-6789 (SSN/ITIN)"),
    };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS residencies (customer_id TEXT PRIMARY KEY, items TEXT, updated_at TEXT, updated_by TEXT, version INTEGER)");
    }

    private static object View(string customerId, string json, string updatedAt, long version)
    {
        var items = JsonSerializer.Deserialize<List<Residency>>(json, Canonical.Web)!;
        var countries = items.Select(i => i.Country!).ToList();
        return new
        {
            customerId, residencies = items.Select(i => new { country = i.Country, tin = i.Tin is null ? null : i.Tin[..Math.Min(4, i.Tin.Length)] + new string('*', Math.Max(0, i.Tin.Length - 4)), primary = i.Primary, since = i.Since }),
            primaryCountry = items.First(i => i.Primary == true).Country, fatcaReportable = countries.Contains("US"),
            crsReportableCountries = countries.Where(c => c != "SE" && c != "US").ToList(), updatedAt, version,
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/customers/{customerId}/tax-residencies", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Items, string At, long V)>("SELECT items, updated_at, version FROM residencies WHERE customer_id = @customerId", new { customerId });
            return r.Items is null
                ? Problems.NotFound(http, "TAX_RESIDENCY_NOT_FOUND", "El cliente no ha declarado su residencia fiscal.", "Se declara con PUT en esta misma ruta.")
                : Results.Ok(View(customerId, r.Items, r.At, r.V));
        }).WithTags("Residencia fiscal").WithSummary("Residencias fiscales (TIN enmascarado) y banderas FATCA/CRS");

        app.MapPut("/api/v1/customers/{customerId}/tax-residencies", async (HttpContext http, string customerId, ResidenciesRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            var items = (b.Residencies ?? Array.Empty<Residency>()).Select(r => r with { Country = r.Country?.ToUpperInvariant(), Tin = r.Tin?.Trim().ToUpperInvariant() }).ToList();
            var errors = new List<FieldError>();
            if (items.Count == 0) errors.Add(new FieldError("residencies", "al menos una"));
            if (items.Count(i => i.Primary == true) != 1) errors.Add(new FieldError("residencies", "exactamente una con primary: true"));
            if (items.Select(i => i.Country).Distinct().Count() != items.Count) errors.Add(new FieldError("residencies", "un país no puede repetirse"));
            for (var i = 0; i < items.Count; i++)
            {
                var r = items[i];
                if (r.Country is null || !Formats.ContainsKey(r.Country)) { errors.Add(new FieldError($"residencies[{i}].country", "uno de " + string.Join(", ", Formats.Keys))); continue; }
                if (string.IsNullOrWhiteSpace(r.Tin)) errors.Add(new FieldError($"residencies[{i}].tin", "obligatorio"));
                else if (!Formats[r.Country].Re.IsMatch(r.Tin)) errors.Add(new FieldError($"residencies[{i}].tin", $"formato de {r.Country}: {Formats[r.Country].Example}"));
                else if (r.Country == "SE" && !NationalId.Check(r.Tin).Valid) errors.Add(new FieldError($"residencies[{i}].tin", "número nacional sueco no válido (Luhn)"));
            }
            if (errors.Count > 0) return Problems.Unprocessable(http, "INVALID_TAX_RESIDENCY", "Las residencias fiscales no son válidas.", "Ejemplo: { \"residencies\": [ { \"country\": \"SE\", \"tin\": \"19850312-1234\", \"primary\": true } ] }", errors);
            var json = JsonSerializer.Serialize(items, Canonical.Web);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO residencies VALUES (@customerId, @json, @at, @by, 1) ON CONFLICT(customer_id) DO UPDATE SET items = @json, updated_at = @at, updated_by = @by, version = version + 1",
                new { customerId, json, at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "tax-residency.changed", customerId, new { customerId, countries = items.Select(i => i.Country), fatca = items.Any(i => i.Country == "US") }, tx);
            tx.Commit();
            var r2 = db.QuerySingle<(string Items, string At, long V)>("SELECT items, updated_at, version FROM residencies WHERE customer_id = @customerId", new { customerId });
            return Results.Ok(View(customerId, r2.Items, r2.At, r2.V));
        }).WithTags("Residencia fiscal").WithSummary("Declarar las residencias fiscales (sustituye las anteriores)");

        app.MapGet("/api/v1/tin-formats", () => Results.Ok(Formats.ToDictionary(f => f.Key, f => f.Value.Example))).WithTags("Residencia fiscal").WithSummary("Formatos de TIN admitidos por país");

        app.MapGet("/api/v1/internal/customers/{customerId}/tax-residencies", (string customerId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Items, string At, long V)>("SELECT items, updated_at, version FROM residencies WHERE customer_id = @customerId", new { customerId });
            return r.Items is null ? Results.NotFound() : Results.Ok(View(customerId, r.Items, r.At, r.V));
        }).WithTags("Interno").WithSummary("(interno) Residencias de un cliente");
    }
}

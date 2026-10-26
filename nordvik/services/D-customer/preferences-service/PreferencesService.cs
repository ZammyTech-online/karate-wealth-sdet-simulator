using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record QuietHours(string From, string To);
public sealed record Preferences(string CustomerId, string Language, string Channel, bool MarketingConsent, QuietHours QuietHours, bool IsDefault, string? UpdatedAt);
public sealed record SetPreferencesRequest(string? Language, string? Channel, bool? MarketingConsent, QuietHours? QuietHours);

/// <summary>preferences-service: idioma, canal preferido, consentimiento de marketing y horas de silencio del cliente.</summary>
public sealed class PreferencesService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "preferences-service", "D-customer", 6303, 1, "Clientes",
        "Preferencias de comunicación del cliente. Si nunca las guardó, devuelve los valores por defecto (isDefault=true).",
        DependsOn: new[] { "clock-service", "customer-registry", "permission-service" });

    private static readonly Regex Hour = new(@"^([01]\d|2[0-3]):[0-5]\d$");

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute("CREATE TABLE IF NOT EXISTS preferences (customer_id TEXT PRIMARY KEY, json TEXT, updated_at TEXT)");
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/customers/{customerId}/preferences").WithTags("Preferencias");

        g.MapGet("", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "preferences:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            var row = db.QuerySingleOrDefault<(string Json, string UpdatedAt)>("SELECT json, updated_at FROM preferences WHERE customer_id = @customerId", new { customerId });
            if (row.Json is null) return Results.Ok(new Preferences(customerId, "sv", "EMAIL", false, new QuietHours("22:00", "08:00"), true, null));
            var stored = JsonSerializer.Deserialize<Preferences>(row.Json)!;
            return Results.Ok(stored);
        }).WithSummary("Ver preferencias (o las de por defecto)").Produces<Preferences>();

        app.MapGet("/api/v1/internal/customers/{customerId}/preferences", (string customerId) =>
        {
            using var db = ctx.OpenDb();
            var row = db.QuerySingleOrDefault<(string Json, string UpdatedAt)>("SELECT json, updated_at FROM preferences WHERE customer_id = @customerId", new { customerId });
            return Results.Ok(row.Json is null ? new Preferences(customerId, "sv", "EMAIL", false, new QuietHours("22:00", "08:00"), true, null) : JsonSerializer.Deserialize<Preferences>(row.Json)!);
        }).WithTags("Interno").WithSummary("Preferencias para otros servicios (red interna, sin autenticación)").Produces<Preferences>();

        g.MapPut("", async (HttpContext http, IClock clock, string customerId, SetPreferencesRequest body) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (body.Language is not ("sv" or "en" or "es")) errors.Add(new FieldError("language", "sv, en o es (obligatorio)"));
            if (body.Channel is not ("EMAIL" or "SMS" or "PUSH" or "POST")) errors.Add(new FieldError("channel", "EMAIL, SMS, PUSH o POST (obligatorio)"));
            if (body.MarketingConsent is null) errors.Add(new FieldError("marketingConsent", "obligatorio: true o false"));
            if (body.QuietHours is not null && (!Hour.IsMatch(body.QuietHours.From ?? "") || !Hour.IsMatch(body.QuietHours.To ?? "")))
                errors.Add(new FieldError("quietHours", "horas HH:mm, por ejemplo { \"from\": \"22:00\", \"to\": \"08:00\" }"));
            if (errors.Count > 0)
                return Problems.BadRequest(http, "VALIDATION_FAILED", "Preferencias no válidas.", "PUT sustituye todo: envía language, channel y marketingConsent siempre.", errors);
            var guard = await CustomerGuard.Check(http, p, "preferences:write", customerId);
            if (guard is not null) return guard;
            var now = Nordic.Iso(clock.LocalNow);
            var prefs = new Preferences(customerId, body.Language!, body.Channel!, body.MarketingConsent!.Value, body.QuietHours ?? new QuietHours("22:00", "08:00"), false, now);
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO preferences VALUES (@customerId, @json, @now) ON CONFLICT(customer_id) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at",
                new { customerId, json = JsonSerializer.Serialize(prefs), now });
            return Results.Ok(prefs);
        }).WithSummary("Guardar preferencias (sustituye todas)").Produces<Preferences>();
    }
}

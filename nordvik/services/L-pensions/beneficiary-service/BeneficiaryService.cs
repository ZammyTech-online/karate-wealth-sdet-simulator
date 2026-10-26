using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record BeneficiaryLine(string? Name, string? Relation, decimal? Percent, string? NationalId, string? BirthDate);
public sealed record BeneficiariesRequest(BeneficiaryLine[]? Beneficiaries);

/// <summary>
/// beneficiary-service: quién cobra el plan si el titular fallece. Los porcentajes suman exactamente 100,00; solo familia cercana
/// (cónyuge o pareja, hijos); el número nacional se guarda tokenizado en pii-vault. Cambiar los beneficiarios exige el ETag (If-Match).
/// </summary>
public sealed class BeneficiaryService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "beneficiary-service", "L-pensions", 7107, 3, "Pensiones",
        "Beneficiarios de cada plan: porcentajes que suman 100,00, solo familia cercana, números nacionales tokenizados y cambios con If-Match.",
        DependsOn: new[] { "pension-plan-service", "pii-vault", "permission-service", "clock-service" }, Emits: new[] { "beneficiaries.updated" });

    public static readonly string[] Relations = { "SPOUSE", "PARTNER", "CHILD" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS designations (plan_id TEXT PRIMARY KEY, customer_id TEXT, items TEXT, version INTEGER, updated_at TEXT, updated_by TEXT)");
    }

    private static object View(string planId, string items, long version, string updatedAt) =>
        new { planId, beneficiaries = JsonDocument.Parse(items).RootElement, version, updatedAt };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        async Task<(IResult? Fail, string CustomerId)> Guard(HttpContext http, Principal p, string planId, string action)
        {
            var plan = await Downstream.Get(http, "pension-plan-service", $"/api/v1/internal/pension-plans/{planId}", forwardAuth: false);
            if (!plan.Ok) return (Problems.NotFound(http, "PLAN_NOT_FOUND", $"No existe el plan {planId}.", null), "");
            var cust = plan.Str("customerId")!;
            var d = await Permissions.Check(http, p, action, cust);
            return d.Allowed ? (null, cust) : (Permissions.Denied(http, p, d, "PLAN_NOT_FOUND", $"el plan {planId}"), cust);
        }

        app.MapGet("/api/v1/pension-plans/{planId}/beneficiaries", async (HttpContext http, string planId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var (fail, _) = await Guard(http, p, planId, "portfolio:read");
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Items, long V, string At)>("SELECT items, version, updated_at FROM designations WHERE plan_id = @planId", new { planId });
            http.Response.Headers.ETag = $"\"{(r.Items is null ? 0 : r.V)}\"";
            return Results.Ok(r.Items is null ? View(planId, "[]", 0, "") : View(planId, r.Items, r.V, r.At));
        }).WithTags("Beneficiarios").WithSummary("Beneficiarios del plan (con ETag para poder cambiarlos)");

        app.MapPut("/api/v1/pension-plans/{planId}/beneficiaries", async (HttpContext http, string planId, BeneficiariesRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            var (fail, customerId) = await Guard(http, p, planId, "portfolio:update");
            if (fail is not null) return fail;
            using var db = ctx.OpenDb();
            var current = db.ExecuteScalar<long?>("SELECT version FROM designations WHERE plan_id = @planId", new { planId }) ?? 0;
            var ifMatch = http.Request.Headers.IfMatch.ToString().Trim('"');
            if (string.IsNullOrEmpty(ifMatch)) return Problems.Of(http, 428, "PRECONDITION_REQUIRED", "Falta If-Match", "Cambiar beneficiarios exige el ETag de la última lectura.", "Haz GET y manda su ETag en If-Match.");
            if (ifMatch != current.ToString()) return Problems.Of(http, 412, "VERSION_MISMATCH", "Versión antigua", $"Tu versión es {ifMatch} y la actual es {current}.", "Alguien cambió los beneficiarios: vuelve a leerlos.");
            var lines = b.Beneficiaries ?? Array.Empty<BeneficiaryLine>();
            var errors = new List<FieldError>();
            for (var i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (string.IsNullOrWhiteSpace(l.Name)) errors.Add(new FieldError($"beneficiaries[{i}].name", "obligatorio"));
                if (l.Relation is null || !Relations.Contains(l.Relation.ToUpperInvariant())) errors.Add(new FieldError($"beneficiaries[{i}].relation", "solo familia cercana: " + string.Join(", ", Relations)));
                if (l.Percent is null or <= 0 or > 100 || decimal.Round(l.Percent.Value, 2) != l.Percent) errors.Add(new FieldError($"beneficiaries[{i}].percent", "entre 0,01 y 100, con 2 decimales como mucho"));
                if (l.NationalId is not null && !NationalId.Check(l.NationalId).Valid) errors.Add(new FieldError($"beneficiaries[{i}].nationalId", "no válido"));
            }
            if (lines.Length > 0 && errors.Count == 0 && lines.Sum(l => l.Percent!.Value) != 100m) errors.Add(new FieldError("beneficiaries", $"los porcentajes suman {lines.Sum(l => l.Percent!.Value)} y deben sumar exactamente 100"));
            if (lines.Count(l => l.Relation?.ToUpperInvariant() is "SPOUSE" or "PARTNER") > 1) errors.Add(new FieldError("beneficiaries", "solo un cónyuge o pareja"));
            if (errors.Count > 0) return Problems.Unprocessable(http, "INVALID_BENEFICIARIES", "La designación de beneficiarios no es válida.",
                "Ejemplo: { \"beneficiaries\": [ { \"name\": \"Elsa\", \"relation\": \"CHILD\", \"percent\": 50 }, { \"name\": \"Nils\", \"relation\": \"CHILD\", \"percent\": 50 } ] }", errors);
            var svc = await ServiceToken.Header(clock);
            var stored = new List<object>();
            foreach (var l in lines)
            {
                string? token = null, masked = null;
                if (l.NationalId is not null)
                {
                    var t = await Downstream.Send(http, "pii-vault", HttpMethod.Post, "/api/v1/tokens", new { type = "NATIONAL_ID", value = l.NationalId, customerId }, forwardAuth: false, headers: svc);
                    token = t.Str("token"); masked = t.Str("masked");
                }
                stored.Add(new { name = l.Name, relation = l.Relation!.ToUpperInvariant(), percent = l.Percent!.Value.ToString("F2", Money.Inv), nationalIdToken = token, nationalIdMasked = masked });
            }
            var json = JsonSerializer.Serialize(stored);
            var at = Nordic.Iso(clock.LocalNow);
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO designations VALUES (@planId, @customerId, @json, 1, @at, @by) ON CONFLICT(plan_id) DO UPDATE SET items = @json, version = version + 1, updated_at = @at, updated_by = @by",
                new { planId, customerId, json, at, by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "beneficiaries.updated", planId, new { planId, customerId, count = stored.Count }, tx);
            tx.Commit();
            var v = db.ExecuteScalar<long>("SELECT version FROM designations WHERE plan_id = @planId", new { planId });
            http.Response.Headers.ETag = $"\"{v}\"";
            return Results.Ok(View(planId, json, v, at));
        }).WithTags("Beneficiarios").WithSummary("Designar beneficiarios (sustituye la designación; If-Match obligatorio)");

        app.MapGet("/api/v1/internal/pension-plans/{planId}/beneficiaries", (string planId) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Items, long V, string At)>("SELECT items, version, updated_at FROM designations WHERE plan_id = @planId", new { planId });
            return Results.Ok(r.Items is null ? View(planId, "[]", 0, "") : View(planId, r.Items, r.V, r.At));
        }).WithTags("Interno").WithSummary("(interno) Beneficiarios de un plan");
    }
}

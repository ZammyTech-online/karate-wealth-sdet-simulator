using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record UnderwritingRequest(string? ApplicantRef, int? Age, string? Capital, bool? Smoker, int? Bmi, string[]? Conditions, bool? HospitalizedLast5y);
public sealed record InsurerPolicyRequest(string? ApplicantRef, string? Coverage, string? Capital, string? MonthlyPremium, string? UnderwritingId);
public sealed record InsurerClaimRequest(string? PolicyNumber, string? Type, string? EventDate, string? Amount);
public sealed record ClaimDecisionRequest(string? Decision, string? Amount);

/// <summary>
/// insurer-sim · sistema externo E11 (simulado): la aseguradora de vida e invalidez. OAuth2 client credentials: el token dura 300 s
/// (reloj virtual) y /__admin/tokens/expire-all los invalida todos para probar que el cliente lo renueva. Suscripción médica
/// (STANDARD / RATED con recargo / DECLINED) a partir de las respuestas de salud, que el simulador NO guarda. Pólizas y siniestros
/// (los siniestros quedan PENDING hasta que se deciden con /__admin/claims/{id}/decide).
/// </summary>
public sealed class InsurerSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "insurer-sim", "X-external", 7711, 4, "Sistema externo (simulado)",
        "E11 Aseguradora: OAuth2 client credentials con caducidad, suscripción médica sin guardar respuestas, pólizas y siniestros con decisión manual.",
        DependsOn: new[] { "clock-service" });

    public const string ClientId = "nordvik";
    public const string ClientSecret = "e11-insurer-client-secret-2026";
    private readonly ExternalBehaviour _behaviour = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly ConcurrentDictionary<string, object> _underwriting = new();
    private readonly ConcurrentDictionary<string, (string Ref, string Coverage, string Capital, string Premium, string At)> _policies = new();
    private readonly ConcurrentDictionary<string, (string Policy, string Type, string EventDate, string Amount, string Status, string? Paid)> _claims = new();
    private long _tokensIssued;

    /// <summary>Suscripción médica determinista: puntos de riesgo → STANDARD, RATED (+25/50/75 %) o DECLINED.</summary>
    public static (string Decision, int LoadingPct, string[] Reasons) Underwrite(int age, bool smoker, int bmi, string[] conditions, bool hospitalized)
    {
        var points = 0; var reasons = new List<string>();
        if (smoker) { points += 2; reasons.Add("SMOKER"); }
        if (bmi >= 35) { points += 2; reasons.Add("BMI_HIGH"); } else if (bmi >= 30) { points += 1; reasons.Add("BMI_ELEVATED"); }
        if (hospitalized) { points += 1; reasons.Add("RECENT_HOSPITALIZATION"); }
        foreach (var c in conditions.Select(x => x.ToUpperInvariant()))
            switch (c)
            {
                case "CANCER_ACTIVE": points += 10; reasons.Add(c); break;
                case "HEART_DISEASE": points += 3; reasons.Add(c); break;
                case "DIABETES": points += 2; reasons.Add(c); break;
                default: points += 1; reasons.Add(c); break;
            }
        if (age >= 60) points += 1;
        return points switch
        {
            0 => ("STANDARD", 0, reasons.ToArray()), 1 => ("RATED", 25, reasons.ToArray()), 2 or 3 => ("RATED", 50, reasons.ToArray()),
            4 or 5 => ("RATED", 75, reasons.ToArray()), _ => ("DECLINED", 0, reasons.ToArray()),
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var clock = app.Services.GetRequiredService<IClock>();

        IResult? Bearer(HttpContext http)
        {
            var h = http.Request.Headers.Authorization.FirstOrDefault();
            if (h is null || !h.StartsWith("Bearer ")) return Results.Json(new { error = "invalid_token", error_description = "missing bearer token" }, statusCode: 401);
            if (!_tokens.TryGetValue(h[7..], out var exp) || exp < clock.UtcNow) return Results.Json(new { error = "invalid_token", error_description = "token expired or unknown" }, statusCode: 401);
            return null;
        }

        app.MapPost("/oauth/token", async (HttpContext http) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            var form = await http.Request.ReadFormAsync();
            if (form["grant_type"] != "client_credentials") return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
            if (form["client_id"] != ClientId || form["client_secret"] != ClientSecret) return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            var token = "ins_" + Guid.NewGuid().ToString("N");
            _tokens[token] = clock.UtcNow.AddSeconds(300);
            Interlocked.Increment(ref _tokensIssued);
            return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = 300, scope = "underwriting policies claims" });
        }).WithTags("OAuth2").WithSummary("Token de cliente (client_credentials, form-urlencoded)");

        app.MapPost("/v1/underwriting", async (HttpContext http, UnderwritingRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Bearer(http) is { } no) return no;
            if (b.Age is null || b.Bmi is null || string.IsNullOrWhiteSpace(b.ApplicantRef)) return Results.Json(new { error = "BAD_REQUEST", message = "applicantRef, age and bmi are required" }, statusCode: 400);
            var u = Underwrite(b.Age.Value, b.Smoker ?? false, b.Bmi.Value, b.Conditions ?? Array.Empty<string>(), b.HospitalizedLast5y ?? false);
            var id = "uw_" + Guid.NewGuid().ToString("N")[..12];
            var result = new { underwritingId = id, applicantRef = b.ApplicantRef, decision = u.Decision, loadingPct = u.LoadingPct, reasonCodes = u.Reasons, answersRetained = false };
            _underwriting[id] = result;
            return Results.Ok(result);
        }).WithTags("Aseguradora v1").WithSummary("Suscripción médica (las respuestas no se guardan)");

        app.MapPost("/v1/policies", async (HttpContext http, InsurerPolicyRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Bearer(http) is { } no) return no;
            if (b.UnderwritingId is null || !_underwriting.TryGetValue(b.UnderwritingId, out var uw)) return Results.Json(new { error = "UNDERWRITING_NOT_FOUND" }, statusCode: 422);
            if (JsonSerializer.SerializeToElement(uw).GetProperty("decision").GetString() == "DECLINED") return Results.Json(new { error = "APPLICANT_DECLINED" }, statusCode: 422);
            var number = "INS-" + Random.Shared.Next(1000000, 9999999);
            _policies[number] = (b.ApplicantRef ?? "", b.Coverage ?? "LIFE", b.Capital ?? "0", b.MonthlyPremium ?? "0", clock.UtcNow.ToString("O"));
            return Results.Ok(new { policyNumber = number, status = "IN_FORCE" });
        }).WithTags("Aseguradora v1").WithSummary("Emitir una póliza");

        app.MapPost("/v1/claims", async (HttpContext http, InsurerClaimRequest b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Bearer(http) is { } no) return no;
            if (b.PolicyNumber is null || !_policies.TryGetValue(b.PolicyNumber, out var pol)) return Results.Json(new { error = "POLICY_NOT_FOUND" }, statusCode: 404);
            var id = "icl_" + Guid.NewGuid().ToString("N")[..12];
            _claims[id] = (b.PolicyNumber, (b.Type ?? "DEATH").ToUpperInvariant(), b.EventDate ?? "", b.Amount ?? pol.Capital, "PENDING", null);
            return Results.Ok(new { claimId = id, status = "PENDING" });
        }).WithTags("Aseguradora v1").WithSummary("Abrir un siniestro (queda PENDING)");

        app.MapGet("/v1/claims/{id}", async (HttpContext http, string id) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (Bearer(http) is { } no) return no;
            return _claims.TryGetValue(id, out var c)
                ? Results.Ok(new { claimId = id, policyNumber = c.Policy, type = c.Type, status = c.Status, approvedAmount = c.Paid })
                : Results.Json(new { error = "CLAIM_NOT_FOUND" }, statusCode: 404);
        }).WithTags("Aseguradora v1").WithSummary("Estado de un siniestro");

        app.MapPost("/__admin/claims/{id}/decide", (string id, ClaimDecisionRequest b) =>
        {
            if (!_claims.TryGetValue(id, out var c)) return Results.Json(new { error = "CLAIM_NOT_FOUND" }, statusCode: 404);
            var d = (b.Decision ?? "APPROVED").ToUpperInvariant();
            if (d is not ("APPROVED" or "REJECTED")) return Results.Json(new { error = "BAD_REQUEST", message = "decision APPROVED or REJECTED" }, statusCode: 400);
            _claims[id] = c with { Status = d, Paid = d == "APPROVED" ? b.Amount ?? c.Amount : null };
            return Results.Ok(new { claimId = id, status = d, approvedAmount = _claims[id].Paid });
        }).WithTags("Administración del simulador").WithSummary("Decidir un siniestro (APPROVED con importe o REJECTED)");
        app.MapGet("/__admin/claims", () => Results.Ok(new { items = _claims.Select(k => new { claimId = k.Key, policyNumber = k.Value.Policy, status = k.Value.Status }) }))
            .WithTags("Administración del simulador").WithSummary("Siniestros recibidos");
        app.MapPost("/__admin/tokens/expire-all", () => { var n = _tokens.Count; _tokens.Clear(); return Results.Ok(new { expired = n }); })
            .WithTags("Administración del simulador").WithSummary("Invalidar todos los tokens (el cliente debe renovarlo)");
        app.MapGet("/__admin/tokens", () => Results.Ok(new { issued = Interlocked.Read(ref _tokensIssued), active = _tokens.Count(t => t.Value >= clock.UtcNow) }))
            .WithTags("Administración del simulador").WithSummary("Tokens emitidos y activos");
        app.MapGet("/__admin/underwriting", () => Results.Ok(new { items = _underwriting.Values })).WithTags("Administración del simulador").WithSummary("Suscripciones (solo resultados)");
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record ProposalRequest(int? RiskProfile, int? HorizonYears, bool? Esg);
public sealed record ProposedLine(string Isin, string Name, string Category, decimal Percent);
public sealed record Proposal(int RiskProfile, int HorizonYears, bool Esg, decimal EquityPercent, IReadOnlyList<ProposedLine> Allocation, IReadOnlyList<string> Rationale);

/// <summary>allocation-engine: propone una asignación de fondos según perfil de riesgo (1-7), horizonte y preferencia ESG.</summary>
public sealed class AllocationEngine : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "allocation-engine", "J-investment", 6901, 1, "Inversiones",
        "Reglas de asignación: porcentaje en renta variable según riesgo, tope por horizonte corto y sustitución ESG. Siempre suma 100.",
        DependsOn: new[] { "product-catalog" });

    private static readonly decimal[] EquityByRisk = { 0, 0, 10, 30, 50, 70, 85, 100 };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapPost("/api/v1/allocations/propose", async (HttpContext http, ProposalRequest body) =>
        {
            var errors = new List<FieldError>();
            if (body.RiskProfile is null or < 1 or > 7) errors.Add(new FieldError("riskProfile", "de 1 (muy prudente) a 7 (muy arriesgado)"));
            if (body.HorizonYears is null or < 1 or > 50) errors.Add(new FieldError("horizonYears", "de 1 a 50 años"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Faltan datos para proponer.", "Ejemplo: { \"riskProfile\": 4, \"horizonYears\": 10, \"esg\": false }", errors);
            var risk = body.RiskProfile!.Value; var horizon = body.HorizonYears!.Value; var esg = body.Esg ?? false;
            var why = new List<string>();
            var equity = EquityByRisk[risk];
            why.Add($"Perfil {risk}: {equity} % en renta variable.");
            if (horizon < 3 && equity > 30) { equity = 30; why.Add("Horizonte menor de 3 años: la renta variable se limita al 30 %."); }
            var list = await Downstream.Get(http, "product-catalog", "/api/v1/funds?status=OPEN&page_size=100", forwardAuth: false);
            var funds = list.Json.GetProperty("items").EnumerateArray().ToDictionary(f => f.GetProperty("isin").GetString()!, f => (Name: f.GetProperty("name").GetString()!, Category: f.GetProperty("category").GetString()!));
            var lines = new List<(string Isin, decimal Pct)>();
            if (equity > 0)
            {
                var eq = esg ? new List<string> { "SE0000000005", "SE0000000002" } : new List<string> { "SE0000000001", "SE0000000002" };
                if (esg) why.Add("Preferencia ESG: Nordvik Europe ESG sustituye a Nordvik Global Equity.");
                if (risk >= 6) { eq.Add("SE0000000003"); why.Add("Riesgo alto: se añade Nordic Small Cap."); }
                if (risk == 7) { eq.Add("SE0000000006"); why.Add("Riesgo máximo: se añade Tech Growth."); }
                Split(lines, eq, equity);
            }
            var rest = 100 - equity;
            if (rest > 0)
            {
                var fixedIncome = risk == 1 ? new List<string> { "SE0000000301", "SE0000000101" } : new List<string> { "SE0000000101", "SE0000000102" };
                if (risk == 1) why.Add("Perfil 1: parte en el fondo monetario.");
                Split(lines, fixedIncome, rest);
            }
            var allocation = lines.Where(l => l.Pct > 0).Select(l => new ProposedLine(l.Isin, funds[l.Isin].Name, funds[l.Isin].Category, l.Pct)).ToList();
            return Results.Ok(new Proposal(risk, horizon, esg, equity, allocation, why));
        }).WithTags("Asignación").WithSummary("Proponer una asignación que suma exactamente 100").Produces<Proposal>();
    }

    /// <summary>Reparte un total entre fondos a partes iguales con 2 decimales; el último absorbe el redondeo.</summary>
    private static void Split(List<(string, decimal)> lines, List<string> isins, decimal total)
    {
        var each = Math.Round(total / isins.Count, 2, MidpointRounding.ToZero);
        for (var i = 0; i < isins.Count; i++) lines.Add((isins[i], i == isins.Count - 1 ? total - each * (isins.Count - 1) : each));
    }
}

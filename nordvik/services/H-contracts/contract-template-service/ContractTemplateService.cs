using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record TemplateClause(int ClauseNo, string Code, string Title, string Text, string Condition);
public sealed record ContractTemplate(string Type, int Version, string Status, string EffectiveFrom, string Language, string Title, IReadOnlyList<string> Parties, IReadOnlyList<TemplateClause> Clauses);
public sealed record TemplateSummary(string Type, int CurrentVersion, IReadOnlyList<int> Versions, IReadOnlyList<string> Parties);

/// <summary>
/// contract-template-service: plantillas de contrato versionadas. Un contrato firmado queda fijado a su versión aunque salga otra:
/// por eso las versiones antiguas siguen disponibles (RETIRED = ya no se usa para contratos nuevos).
/// Cada cláusula tiene una condición que evalúa clause-engine (always, age >= 65, taxResidency != 'SE'...).
/// </summary>
public sealed class ContractTemplateService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "contract-template-service", "H-contracts", 6701, 2, "Contratos y documentos",
        "Plantillas versionadas: contrato marco (v1-v3), mandato de asesoramiento (v1-v2), gestión discrecional y consentimiento de grabación.");

    private static TemplateClause C(int n, string code, string title, string text, string cond = "always") => new(n, code, title, text, cond);

    public static readonly IReadOnlyList<ContractTemplate> All = new List<ContractTemplate>
    {
        new("MASTER_AGREEMENT", 1, "RETIRED", "2019-01-01", "sv", "Contrato marco de cliente", new[] { "CUSTOMER" }, new[]
        {
            C(1, "GEN-01", "Objeto", "Nordvik presta servicios de inversión al cliente según estas condiciones generales."),
            C(2, "GEN-02", "Comisiones", "Las comisiones son las publicadas en la ficha de cada fondo."),
            C(3, "TAX-SE", "Fiscalidad sueca", "El cliente declara ser residente fiscal en Suecia.", "taxResidency == 'SE'"),
        }),
        new("MASTER_AGREEMENT", 2, "RETIRED", "2022-03-01", "sv", "Contrato marco de cliente", new[] { "CUSTOMER" }, new[]
        {
            C(1, "GEN-01", "Objeto", "Nordvik presta servicios de inversión y de pensiones al cliente según estas condiciones generales."),
            C(2, "GEN-02", "Comisiones", "Las comisiones son las publicadas en la ficha de cada fondo y en el folleto."),
            C(3, "TAX-SE", "Fiscalidad sueca", "El cliente declara ser residente fiscal en Suecia.", "taxResidency == 'SE'"),
            C(4, "TAX-FOREIGN", "Residencia fiscal extranjera", "El cliente informará a Nordvik de su residencia fiscal (CRS/FATCA).", "taxResidency != 'SE'"),
            C(5, "ISK-01", "Cuenta ISK", "Investeringssparkonto: tributación estándar anual sobre el valor.", "accountType == 'ISK'"),
        }),
        new("MASTER_AGREEMENT", 3, "ACTIVE", "2025-06-01", "sv", "Contrato marco de cliente", new[] { "CUSTOMER" }, new[]
        {
            C(1, "GEN-01", "Objeto", "Nordvik presta servicios de inversión y de pensiones al cliente según estas condiciones generales."),
            C(2, "GEN-02", "Comisiones", "Las comisiones son las publicadas en la ficha de cada fondo y en el folleto vigente."),
            C(3, "GDPR-01", "Protección de datos", "Nordvik trata los datos personales del cliente según el RGPD; el cliente puede pedir acceso, portabilidad y supresión."),
            C(4, "TAX-SE", "Fiscalidad sueca", "El cliente declara ser residente fiscal en Suecia.", "taxResidency == 'SE'"),
            C(5, "TAX-FOREIGN", "Residencia fiscal extranjera", "El cliente informará a Nordvik de su residencia fiscal (CRS/FATCA).", "taxResidency != 'SE'"),
            C(6, "ISK-01", "Cuenta ISK", "Investeringssparkonto: tributación estándar anual sobre el valor.", "accountType == 'ISK'"),
            C(7, "KF-01", "Seguro de capitalización", "Kapitalförsäkring: el titular formal es la aseguradora.", "accountType == 'KF'"),
            C(8, "SENIOR-01", "Clientes mayores de 65", "Nordvik ofrece una revisión anual de idoneidad gratuita.", "age >= 65"),
            C(9, "PEP-01", "Persona políticamente expuesta", "El cliente acepta controles reforzados de origen de fondos.", "isPep == true"),
            C(10, "FUNDS-ORIGIN", "Origen de los fondos", "El cliente declara el origen lícito de los fondos (cláusula cifrada en el expediente KYC)."),
        }),
        new("ADVISORY_MANDATE", 1, "RETIRED", "2020-01-01", "sv", "Mandato de asesoramiento", new[] { "CUSTOMER", "ADVISOR" }, new[]
        {
            C(1, "ADV-01", "Servicio", "El asesor recomienda; el cliente decide cada operación."),
            C(2, "ADV-02", "Idoneidad", "Antes de recomendar se evalúa la idoneidad del cliente."),
        }),
        new("ADVISORY_MANDATE", 2, "ACTIVE", "2024-09-01", "sv", "Mandato de asesoramiento", new[] { "CUSTOMER", "ADVISOR" }, new[]
        {
            C(1, "ADV-01", "Servicio", "El asesor recomienda; el cliente decide cada operación."),
            C(2, "ADV-02", "Idoneidad", "Antes de recomendar se evalúa la idoneidad del cliente; el test se adjunta solo como hash."),
            C(3, "ADV-03", "Tarifa", "La tarifa de asesoramiento es la del anexo FEE_SCHEDULE."),
            C(4, "SENIOR-01", "Clientes mayores de 65", "Las recomendaciones se confirman por escrito.", "age >= 65"),
        }),
        new("DISCRETIONARY_MANDATE", 1, "ACTIVE", "2023-01-01", "sv", "Mandato de gestión discrecional", new[] { "CUSTOMER", "ADVISOR", "COMPLIANCE" }, new[]
        {
            C(1, "DIS-01", "Gestión", "El gestor decide las operaciones dentro de la política de inversión."),
            C(2, "DIS-02", "Límites de riesgo", "La cartera respeta los límites de riesgo del anexo."),
            C(3, "DIS-03", "Cuatro ojos", "Compliance aprueba el mandato antes de activarlo."),
        }),
        new("RECORDING_CONSENT", 1, "ACTIVE", "2024-01-01", "sv", "Acuerdo de grabación de llamadas", new[] { "CUSTOMER" }, new[]
        {
            C(1, "REC-01", "Grabación", "Las llamadas con el asesor se graban y se transcriben."),
            C(2, "REC-02", "Revocación", "El cliente puede revocar este consentimiento en cualquier momento."),
        }),
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        app.MapGet("/api/v1/contract-templates", () => Results.Ok(All.GroupBy(t => t.Type).Select(g => new TemplateSummary(g.Key,
                g.Where(t => t.Status == "ACTIVE").Max(t => t.Version), g.Select(t => t.Version).OrderBy(v => v).ToList(), g.First().Parties)).ToList()))
            .WithTags("Plantillas").WithSummary("Tipos de contrato y sus versiones").Produces<List<TemplateSummary>>();

        app.MapGet("/api/v1/contract-templates/{type}/versions", (HttpContext http, string type) =>
        {
            var list = All.Where(t => t.Type == type).OrderBy(t => t.Version).ToList();
            return list.Count == 0 ? Unknown(http, type) : Results.Ok(list);
        }).WithTags("Plantillas").WithSummary("Todas las versiones de un tipo").Produces<List<ContractTemplate>>();

        app.MapGet("/api/v1/contract-templates/{type}/versions/{version:int}", (HttpContext http, string type, int version) =>
        {
            var t = All.FirstOrDefault(x => x.Type == type && x.Version == version);
            if (t is null && All.Any(x => x.Type == type))
                return Problems.NotFound(http, "TEMPLATE_VERSION_NOT_FOUND", $"{type} no tiene versión {version}.", "Versiones: " + string.Join(", ", All.Where(x => x.Type == type).Select(x => x.Version)));
            return t is null ? Unknown(http, type) : Results.Ok(t);
        }).WithTags("Plantillas").WithSummary("Una versión concreta (también las retiradas)").Produces<ContractTemplate>();

        app.MapGet("/api/v1/contract-templates/{type}/current", (HttpContext http, string type) =>
        {
            var t = All.Where(x => x.Type == type && x.Status == "ACTIVE").MaxBy(x => x.Version);
            return t is null ? Unknown(http, type) : Results.Ok(t);
        }).WithTags("Plantillas").WithSummary("La versión vigente para contratos nuevos").Produces<ContractTemplate>();
    }

    private static IResult Unknown(HttpContext http, string type) =>
        Problems.NotFound(http, "TEMPLATE_NOT_FOUND", $"No existe el tipo de contrato {type}.", "Tipos: " + string.Join(", ", All.Select(t => t.Type).Distinct()));
}

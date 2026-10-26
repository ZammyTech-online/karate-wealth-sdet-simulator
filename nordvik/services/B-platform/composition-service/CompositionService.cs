using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record Source(string Service, long Version, string FetchedAt, int Status, long Ms);
public sealed record MasterAgreementRequest(string? CustomerId);

/// <summary>
/// composition-service: une varias APIs en un solo JSON (casos C1-C10 del documento de contratos).
/// Reglas comunes: sources[] con servicio, versión y hora de cada origen; si un origen tarda más de 2 s o falla, la respuesta llega igual
/// con status PARTIAL y su nombre en missing (nunca un 500); X-Composition-Trace: true añade las respuestas crudas de cada origen.
/// Los casos de las oleadas 3 y 4 responden 501 hasta que existan sus servicios.
/// </summary>
public sealed class CompositionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "composition-service", "B-platform", 6107, 2, "Plataforma",
        "Composición de APIs: contrato + anexo (C1), posiciones con fondo (C2), extracto mensual (C3), ficha 360 (C4), contrato marco con fusión profunda (C5), mandato (C6).",
        "2.0.0",
        DependsOn: new[] { "contract-service", "document-vault", "position-keeping", "product-catalog", "valuation-service", "ledger-service", "customer-registry",
            "preferences-service", "portfolio-service", "contract-template-service", "clock-service" });

    private sealed class Ctx
    {
        public required HttpContext Http;
        public required IClock Clock;
        public List<Source> Sources { get; } = new();
        public List<string> Missing { get; } = new();
        public JsonObject Raw { get; } = new();

        public async Task<JsonElement?> Fetch(string name, string service, string path, bool forwardAuth = true)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await Downstream.Get(Http, service, path, forwardAuth, TimeSpan.FromSeconds(2));
                sw.Stop();
                Raw[name] = r.Body is null ? JsonValue.Create(r.Raw) : JsonNode.Parse(r.Raw);
                if (!r.Ok) { Missing.Add(name); Sources.Add(new Source(service, 0, Nordic.Iso(Clock.LocalNow), r.Status, sw.ElapsedMilliseconds)); return null; }
                var version = r.Json.ValueKind == JsonValueKind.Object && r.Json.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 1;
                Sources.Add(new Source(service, version, Nordic.Iso(Clock.LocalNow), r.Status, sw.ElapsedMilliseconds));
                return r.Json;
            }
            catch (DependencyUnavailableException)
            {
                Missing.Add(name);
                Sources.Add(new Source(service, 0, Nordic.Iso(Clock.LocalNow), 504, sw.ElapsedMilliseconds));
                return null;
            }
        }

        public IResult Result(object bundle, string? note = null)
        {
            var body = new Dictionary<string, object?>
            {
                ["bundle"] = bundle, ["status"] = Missing.Count == 0 ? "COMPLETE" : "PARTIAL", ["missing"] = Missing, ["sources"] = Sources,
            };
            if (note is not null) body["note"] = note;
            if (Http.Request.Headers["X-Composition-Trace"] == "true") body["trace"] = Raw;
            return Results.Json(body);
        }
    }

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>Fusión profunda con reglas (caso C5): los objetos se mezclan campo a campo; las listas de cláusulas se cruzan por clauseNo; gana 'b' salvo regla contraria.</summary>
    public static JsonNode? DeepMerge(JsonNode? a, JsonNode? b, string key = "clauseNo")
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            var r = new JsonObject();
            foreach (var (k, v) in oa) r[k] = v?.DeepClone();
            foreach (var (k, v) in ob) r[k] = r.ContainsKey(k) ? DeepMerge(r[k], v, key) : v?.DeepClone();
            return r;
        }
        if (a is JsonArray aa && b is JsonArray ab && aa.All(x => x is JsonObject o && o.ContainsKey(key)) && ab.All(x => x is JsonObject o && o.ContainsKey(key)))
        {
            var byKey = new SortedDictionary<int, JsonNode?>();
            foreach (var x in aa) byKey[x![key]!.GetValue<int>()] = x.DeepClone();
            foreach (var x in ab) { var k = x![key]!.GetValue<int>(); byKey[k] = byKey.TryGetValue(k, out var prev) ? DeepMerge(prev, x, key) : x.DeepClone(); }
            return new JsonArray(byKey.Values.ToArray());
        }
        return b?.DeepClone();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();
        var g = app.MapGroup("/api/v2/compositions").WithTags("Composiciones");

        IResult? NeedAuth(HttpContext http) => Auth.Require(http, clock, out _);

        // C1 · contrato + su anexo (agregación)
        g.MapGet("/contract-bundle/{contractId}", async (HttpContext http, string contractId) =>
        {
            if (NeedAuth(http) is { } d) return d;
            var c = new Ctx { Http = http, Clock = clock };
            var contract = await c.Fetch("contract", "contract-service", $"/api/v2/contracts/{contractId}");
            if (contract is null && c.Sources[0].Status is 401 or 403 or 404)
                return Results.Content(c.Raw["contract"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            JsonElement? annex = null;
            if (contract is { } ct && ct.GetProperty("annexes").GetArrayLength() > 0)
                annex = await c.Fetch("annex", "document-vault", $"/api/v1/documents/{ct.GetProperty("annexes")[0].GetProperty("documentId").GetString()}");
            else c.Missing.Add("annex");
            return c.Result(new { contract, annex }, contract is { } x && x.GetProperty("annexes").GetArrayLength() == 0 ? "El contrato no tiene anexos." : null);
        }).WithSummary("C1 · Contrato con su anexo en un solo JSON");

        // C2 · posiciones enriquecidas con el nombre y el SRI del fondo
        g.MapGet("/holdings-enriched/{portfolioId}", async (HttpContext http, string portfolioId) =>
        {
            if (NeedAuth(http) is { } d) return d;
            var c = new Ctx { Http = http, Clock = clock };
            var pos = await c.Fetch("positions", "position-keeping", $"/api/v1/portfolios/{portfolioId}/positions");
            if (pos is null) return Results.Content(c.Raw["positions"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            var items = new List<object>();
            foreach (var p in pos.Value.GetProperty("positions").EnumerateArray())
            {
                var isin = p.GetProperty("isin").GetString()!;
                var fund = await c.Fetch("fund:" + isin, "product-catalog", $"/api/v1/funds/{isin}", forwardAuth: false);
                items.Add(new
                {
                    isin, units = p.GetProperty("units").GetString(), costBasisSek = p.GetProperty("costBasisSek").GetString(),
                    fundName = fund?.GetProperty("name").GetString(), sri = fund is null ? (int?)null : fund.Value.GetProperty("sri").GetInt32(),
                });
            }
            return c.Result(new { portfolioId, asOf = pos.Value.GetProperty("asOf").GetString(), positions = items });
        }).WithSummary("C2 · Posiciones con fundName y sri del catálogo (enriquecimiento)");

        // C3 · extracto mensual: valoración a fin de mes + movimientos de efectivo del ledger
        g.MapGet("/monthly-statement/{portfolioId}", async (HttpContext http, string portfolioId, string? month) =>
        {
            if (NeedAuth(http) is { } d) return d;
            if (month is null || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first))
                return Problems.BadRequest(http, "VALIDATION_FAILED", "month es obligatorio (AAAA-MM).", null);
            var last = first.AddMonths(1).AddDays(-1);
            if (last > clock.Today) last = clock.Today;
            var c = new Ctx { Http = http, Clock = clock };
            var val = await c.Fetch("valuation", "valuation-service", $"/api/v1/portfolios/{portfolioId}/valuation?date={Money.Date(last)}");
            if (val is null && c.Sources[0].Status is 401 or 403 or 404)
                return Results.Content(c.Raw["valuation"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            var customerId = val?.GetProperty("customerId").GetString();
            var entries = customerId is null ? null : await c.Fetch("cashEntries", "ledger-service", $"/api/v1/accounts/customer:{customerId}:cash/entries", forwardAuth: false);
            var account = $"customer:{customerId}:cash";
            var movements = entries?.EnumerateArray()
                .Where(e => string.CompareOrdinal(e.GetProperty("valueDate").GetString(), Money.Date(first)) >= 0 && string.CompareOrdinal(e.GetProperty("valueDate").GetString(), Money.Date(last)) <= 0)
                .Select(e =>
                {
                    var line = e.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("account").GetString() == account);
                    var amount = D(line.GetProperty("credit").GetString()!) - D(line.GetProperty("debit").GetString()!);
                    return new { date = e.GetProperty("valueDate").GetString(), reference = e.GetProperty("reference").GetString(), description = e.GetProperty("description").GetString(), amountSek = Money.Format(amount, 2) };
                }).ToList();
            var balance = customerId is null ? null : await c.Fetch("cashBalance", "ledger-service", $"/api/v1/accounts/{account}/balance?asOf={Money.Date(last)}", forwardAuth: false);
            var holdings = val?.GetProperty("lines").EnumerateArray().Select(l => new
            {
                isin = l.GetProperty("isin").GetString(), units = l.GetProperty("units").GetString(), nav = l.GetProperty("nav").GetString(), navDate = l.GetProperty("navDate").GetString(),
                fxRate = l.GetProperty("fxRate").GetString(), valueSek = l.GetProperty("valueSek").GetString(),
                check = Money.Format(Math.Round(D(l.GetProperty("units").GetString()!) * D(l.GetProperty("nav").GetString()!) * D(l.GetProperty("fxRate").GetString()!), 2, MidpointRounding.ToEven), 2) == l.GetProperty("valueSek").GetString(),
            }).ToList();
            var holdingsValue = val is null ? 0 : D(val.Value.GetProperty("totalValueSek").GetString()!);
            var cash = balance is null ? 0 : D(balance.Value.GetProperty("balance").GetString()!);
            return c.Result(new
            {
                portfolioId, month, periodFrom = Money.Date(first), periodTo = Money.Date(last), priceDate = val?.GetProperty("priceDate").GetString(),
                holdings, holdingsValueSek = Money.Format(holdingsValue, 2), cashMovements = movements, closingCashSek = Money.Format(cash, 2), totalAssetsSek = Money.Format(holdingsValue + cash, 2),
            });
        }).WithSummary("C3 · Extracto mensual: participaciones × NAV del último día hábil + movimientos y saldo del ledger");

        // C4 · ficha 360 del cliente (campos según el rol)
        g.MapGet("/customer-360/{customerId}", async (HttpContext http, string customerId) =>
        {
            if (NeedAuth(http) is { } d) return d;
            var c = new Ctx { Http = http, Clock = clock };
            var customer = await c.Fetch("customer", "customer-registry", $"/api/v1/customers/{customerId}");
            if (customer is null) return Results.Content(c.Raw["customer"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            var prefs = await c.Fetch("preferences", "preferences-service", $"/api/v1/customers/{customerId}/preferences");
            var portfolios = await c.Fetch("portfolios", "portfolio-service", $"/api/v1/customers/{customerId}/portfolios");
            var contracts = await c.Fetch("contracts", "contract-service", $"/api/v2/contracts?customerId={customerId}&limit=50");
            // Oleada 3: si el servicio no está en el catálogo, la parte sale en missing (la composición nunca falla por eso)
            JsonElement? consents = null, kyc = null;
            if (ServiceDirectory.Knows("consent-ledger")) consents = await c.Fetch("consents", "consent-ledger", $"/api/v1/customers/{customerId}/consents");
            else c.Missing.Add("consents");
            if (ServiceDirectory.Knows("kyc-aml-service")) kyc = await c.Fetch("kyc", "kyc-aml-service", $"/api/v1/customers/{customerId}/kyc");
            else c.Missing.Add("kyc");
            return c.Result(new
            {
                customer, preferences = prefs, portfolios,
                contracts = contracts?.GetProperty("items").EnumerateArray().Select(x => new { id = x.GetProperty("id").GetString(), type = x.GetProperty("type").GetString(), status = x.GetProperty("status").GetString() }).ToList(),
                consents = consents?.GetProperty("consents"), kyc,
            }, c.Missing.Count == 0 ? null : "Las partes de missing llegan con la oleada 3 o su servicio no respondió.");
        }).WithSummary("C4 · Ficha 360: cliente + preferencias + carteras + contratos (cada origen aplica sus propios permisos)");

        // C5 · contrato marco completo: fusión profunda contrato + plantilla v3 + preferencias del cliente
        g.MapPost("/master-agreement", async (HttpContext http, MasterAgreementRequest b) =>
        {
            if (NeedAuth(http) is { } d) return d;
            if (string.IsNullOrWhiteSpace(b.CustomerId)) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta customerId.", null);
            var c = new Ctx { Http = http, Clock = clock };
            var list = await c.Fetch("contracts", "contract-service", $"/api/v2/contracts?customerId={b.CustomerId}&limit=100");
            if (list is null) return Results.Content(c.Raw["contracts"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            var contract = list.Value.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("type").GetString() == "MASTER_AGREEMENT").LastOrDefault();
            if (contract.ValueKind == JsonValueKind.Undefined) return Problems.NotFound(http, "CONTRACT_NOT_FOUND", "El cliente no tiene contrato marco.", null);
            var template = await c.Fetch("template", "contract-template-service", $"/api/v1/contract-templates/MASTER_AGREEMENT/versions/3", forwardAuth: false);
            var prefs = await c.Fetch("preferences", "preferences-service", $"/api/v1/customers/{b.CustomerId}/preferences");
            // Regla 1: los datos de la plantilla son la base; el contrato los concreta; el cliente gana en language.
            JsonNode? merged = template is null ? new JsonObject() : JsonNode.Parse(template.Value.GetRawText());
            merged = DeepMerge(merged, JsonNode.Parse(contract.GetProperty("content").GetRawText()));
            if (prefs is { } p) merged!["language"] = p.GetProperty("language").GetString();
            merged!.AsObject().Remove("status");
            return c.Result(new { contractId = contract.GetProperty("id").GetString(), contractStatus = contract.GetProperty("status").GetString(), merged },
                "Reglas: plantilla < contrato < preferencias del cliente (language); cláusulas fusionadas por clauseNo y ordenadas.");
        }).WithSummary("C5 · Contrato marco completo con fusión profunda por reglas");

        // C6 · mandato de asesoramiento: contrato + tarifa del catálogo; el test de idoneidad (advisor-desk, oleada 3) solo como hash
        g.MapGet("/advisory-mandate/{contractId}", async (HttpContext http, string contractId, string? isin) =>
        {
            if (NeedAuth(http) is { } d) return d;
            var c = new Ctx { Http = http, Clock = clock };
            var contract = await c.Fetch("contract", "contract-service", $"/api/v2/contracts/{contractId}");
            if (contract is null) return Results.Content(c.Raw["contract"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            if (contract.Value.GetProperty("type").GetString() != "ADVISORY_MANDATE") return Problems.Unprocessable(http, "NOT_AN_ADVISORY_MANDATE", "Ese contrato no es un mandato de asesoramiento.", null);
            var fees = await c.Fetch("fees", "product-catalog", $"/api/v1/funds/{isin ?? "SE0000000001"}/fees", forwardAuth: false);
            var suitability = contract.Value.GetProperty("annexes").EnumerateArray().FirstOrDefault(a => a.GetProperty("kind").GetString() == "SUITABILITY");
            if (!ServiceDirectory.Knows("advisor-desk")) c.Missing.Add("advisorDesk");
            return c.Result(new
            {
                contract, fees,
                assessmentHash = suitability.ValueKind == JsonValueKind.Undefined ? null : suitability.GetProperty("contentHash").GetString(),
            }, "El test de idoneidad nunca viaja en claro: solo su assessmentHash.");
        }).WithSummary("C6 · Mandato de asesoramiento (contrato + tarifa + hash del test de idoneidad)");

        // C9 · resumen de llamada: llamada + resumen + datos confirmados + (si el cliente citó una cláusula) el texto de esa cláusula de su mandato
        g.MapGet("/call-summary/{callId}", async (HttpContext http, string callId) =>
        {
            if (NeedAuth(http) is { } d) return d;
            var c = new Ctx { Http = http, Clock = clock };
            var call = await c.Fetch("call", "voice-service", $"/api/v2/calls/{callId}");
            if (call is null) return Results.Content(c.Raw["call"]!.ToJsonString(), "application/problem+json; charset=utf-8", System.Text.Encoding.UTF8, c.Sources[0].Status);
            var summary = await c.Fetch("summary", "voice-service", $"/api/v2/calls/{callId}/summary");
            JsonElement? insights = null;
            if (http.Items[Auth.PrincipalKey] is not Principal { Role: "Customer" }) insights = await c.Fetch("insights", "voice-service", $"/api/v2/calls/{callId}/insights");
            object? clause = null;
            var cited = insights?.GetProperty("items").EnumerateArray().FirstOrDefault(i => i.GetProperty("field").GetString() == "contractClause");
            if (cited is { ValueKind: JsonValueKind.Object } ci)
            {
                var no = ci.GetProperty("value").GetProperty("clauseNo").GetInt32();
                var customerId = call.Value.GetProperty("customerId").GetString();
                var list = await c.Fetch("contracts", "contract-service", $"/api/v2/contracts?customerId={customerId}&limit=50");
                var mandate = list?.GetProperty("items").EnumerateArray().FirstOrDefault(x => x.GetProperty("type").GetString() == "ADVISORY_MANDATE");
                if (mandate is { ValueKind: JsonValueKind.Object } m)
                {
                    var full = await c.Fetch("mandate", "contract-service", $"/api/v2/contracts/{m.GetProperty("id").GetString()}");
                    var cl = full?.GetProperty("content").GetProperty("clauses").EnumerateArray().FirstOrDefault(x => x.GetProperty("clauseNo").GetInt32() == no);
                    clause = cl is { ValueKind: JsonValueKind.Object } found
                        ? new { clauseNo = no, contractId = m.GetProperty("id").GetString(), code = found.GetProperty("code").GetString(), title = found.GetProperty("title").GetString(), text = found.GetProperty("text").GetString(), citedAt = ci.GetProperty("evidence") }
                        : new { clauseNo = no, contractId = m.GetProperty("id").GetString(), code = (string?)null, title = (string?)null, text = (string?)"El mandato no tiene esa cláusula.", citedAt = ci.GetProperty("evidence") };
                }
                else c.Missing.Add("mandate");
            }
            return c.Result(new { call, summary, insights = insights?.GetProperty("items"), citedClause = clause });
        }).WithSummary("C9 · Resumen de llamada + datos confirmados + la cláusula del mandato que citó el cliente");

        foreach (var (path, wave, name) in new[] { ("/pension-plan/{id}", 3, "C7"), ("/transfer-dossier/{id}", 3, "C8"), ("/portability-export/{id}", 4, "C10") })
            g.MapMethods(path, new[] { "GET", "POST" }, (HttpContext http, string id) =>
                Problems.Of(http, 501, "COMPOSITION_NOT_AVAILABLE_YET", "Aún no disponible", $"La composición {name} llega con la oleada {wave}.", "Mira el plan de oleadas en el documento del sistema."))
                .WithSummary($"{name} · llega con la oleada {wave}");
    }
}

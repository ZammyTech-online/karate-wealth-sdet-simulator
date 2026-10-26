using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

/// <summary>
/// legacy-pensions-soap: el sistema de pensiones antiguo, todavía dueño de los planes contratados antes de 2015. Solo habla SOAP 1.1:
/// sobre XML, cabecera SOAPAction (GetLegacyPlans o GetLegacyPlan) y errores como SOAP Fault con HTTP 500. Tiene WSDL (?wsdl).
/// Los planes son deterministas: cualquier persona nacida antes de 1975 tiene 1 o 2 planes antiguos con su valor.
/// </summary>
public sealed class LegacyPensionsSoap : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "legacy-pensions-soap", "P-integrations", 7510, 4, "Integraciones",
        "Sistema de pensiones heredado en SOAP 1.1 (planes anteriores a 2015): sobre XML, SOAPAction, SOAP Faults con HTTP 500 y WSDL.",
        DependsOn: new[] { "clock-service" });

    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    public static readonly XNamespace Leg = "urn:nordvik:legacy:pensions:v1";

    public sealed record LegacyPlan(string PlanNumber, string Product, int StartYear, decimal Value, string Guarantee);

    public static List<LegacyPlan> PlansFor(string normalizedPnr)
    {
        var birthYear = int.Parse(normalizedPnr[..4]);
        if (birthYear >= 1975) return new();
        var h = Canonical.Sha256Hex("legacy:" + normalizedPnr);
        var n = 1 + Convert.ToInt32(h[..1], 16) % 2;
        return Enumerable.Range(0, n).Select(i =>
        {
            var start = 1995 + Convert.ToInt32(h.Substring(2 + i * 4, 2), 16) % 20;
            var value = 50_000m + Convert.ToInt32(h.Substring(4 + i * 4, 4), 16) % 900_000;
            return new LegacyPlan($"LP-{start}-{h.Substring(10 + i * 4, 4).ToUpperInvariant()}", i == 0 ? "TRAD_PENSION_GARANTI" : "FONDFORSAKRING_K", start, value, i == 0 ? "3.0" : "0.0");
        }).ToList();
    }

    private static IResult Xml(XElement body, int status = 200) =>
        Results.Content(new XDocument(new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soapenv", Soap), new XAttribute(XNamespace.Xmlns + "leg", Leg),
            new XElement(Soap + "Body", body))).ToString(SaveOptions.DisableFormatting), "text/xml; charset=utf-8", Encoding.UTF8, status);

    public static IResult Fault(string code, string text, string detailCode) =>
        Xml(new XElement(Soap + "Fault", new XElement("faultcode", "soapenv:" + code), new XElement("faultstring", text),
            new XElement("detail", new XElement(Leg + "errorCode", detailCode))), 500);

    private static XElement PlanXml(LegacyPlan p) => new(Leg + "plan",
        new XElement(Leg + "planNumber", p.PlanNumber), new XElement(Leg + "product", p.Product), new XElement(Leg + "startYear", p.StartYear),
        new XElement(Leg + "value", p.Value.ToString("F2", Money.Inv)), new XElement(Leg + "currency", "SEK"), new XElement(Leg + "guaranteedRatePct", p.Guarantee));

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/soap/LegacyPensionService", (HttpContext http) =>
            http.Request.Query.ContainsKey("wsdl") ? Results.Content(Wsdl(ServiceDirectory.UrlOf("legacy-pensions-soap")), "text/xml; charset=utf-8", Encoding.UTF8)
                : Fault("Client", "Use POST with a SOAP envelope, or ?wsdl", "USE_POST"))
            .WithTags("SOAP").WithSummary("WSDL del servicio (?wsdl)");

        app.MapPost("/soap/LegacyPensionService", async (HttpContext http) =>
        {
            var action = http.Request.Headers["SOAPAction"].FirstOrDefault()?.Trim('"');
            if (string.IsNullOrEmpty(action)) return Fault("Client", "SOAPAction header is required", "NO_SOAPACTION");
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            XDocument doc;
            try { doc = XDocument.Parse(await reader.ReadToEndAsync()); }
            catch { return Fault("Client", "Request is not well-formed XML", "MALFORMED_XML"); }
            var body = doc.Root?.Element(Soap + "Body")?.Elements().FirstOrDefault();
            if (doc.Root?.Name != Soap + "Envelope" || body is null) return Fault("Client", "Missing soapenv:Envelope/soapenv:Body", "NO_ENVELOPE");
            switch (action)
            {
                case "GetLegacyPlans":
                {
                    if (body.Name != Leg + "GetLegacyPlansRequest") return Fault("Client", "SOAPAction and body element do not match", "ACTION_MISMATCH");
                    var check = NationalId.Check(body.Element(Leg + "personalNumber")?.Value);
                    if (!check.Valid) return Fault("Client", "Invalid personal number", "INVALID_PNR");
                    var plans = PlansFor(check.Normalized!);
                    return Xml(new XElement(Leg + "GetLegacyPlansResponse", new XElement(Leg + "count", plans.Count), plans.Select(PlanXml)));
                }
                case "GetLegacyPlan":
                {
                    if (body.Name != Leg + "GetLegacyPlanRequest") return Fault("Client", "SOAPAction and body element do not match", "ACTION_MISMATCH");
                    var check = NationalId.Check(body.Element(Leg + "personalNumber")?.Value);
                    var number = body.Element(Leg + "planNumber")?.Value;
                    if (!check.Valid) return Fault("Client", "Invalid personal number", "INVALID_PNR");
                    var plan = PlansFor(check.Normalized!).FirstOrDefault(p => p.PlanNumber == number);
                    return plan is null ? Fault("Client", $"Plan {number} not found for this person", "PLAN_NOT_FOUND")
                        : Xml(new XElement(Leg + "GetLegacyPlanResponse", PlanXml(plan), new XElement(Leg + "asOf", clock.Today.ToString("yyyy-MM-dd"))));
                }
                default: return Fault("Client", $"Unknown SOAPAction {action}", "UNKNOWN_ACTION");
            }
        }).WithTags("SOAP").WithSummary("Endpoint SOAP 1.1 (SOAPAction: GetLegacyPlans o GetLegacyPlan)");

        app.MapGet("/api/v1/internal/legacy-plans", (HttpContext http, string personalNumber) =>
        {
            var c = NationalId.Check(personalNumber);
            if (!c.Valid) return Problems.BadRequest(http, "VALIDATION_FAILED", "personalNumber no válido.", null);
            var plans = PlansFor(c.Normalized!);
            return Results.Ok(new { items = plans.Select(p => new { planNumber = p.PlanNumber, product = p.Product, startYear = p.StartYear, value = p.Value.ToString("F2", Money.Inv), currency = "SEK" }), count = plans.Count });
        }).WithTags("Interno").WithSummary("(interno) Fachada JSON de los planes heredados");
    }

    private static string Wsdl(string url) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<definitions xmlns=""http://schemas.xmlsoap.org/wsdl/"" xmlns:soap=""http://schemas.xmlsoap.org/wsdl/soap/"" xmlns:tns=""urn:nordvik:legacy:pensions:v1"" xmlns:xs=""http://www.w3.org/2001/XMLSchema"" targetNamespace=""urn:nordvik:legacy:pensions:v1"" name=""LegacyPensionService"">
  <types><xs:schema targetNamespace=""urn:nordvik:legacy:pensions:v1"" elementFormDefault=""qualified"">
    <xs:element name=""GetLegacyPlansRequest""><xs:complexType><xs:sequence><xs:element name=""personalNumber"" type=""xs:string""/></xs:sequence></xs:complexType></xs:element>
    <xs:element name=""GetLegacyPlanRequest""><xs:complexType><xs:sequence><xs:element name=""personalNumber"" type=""xs:string""/><xs:element name=""planNumber"" type=""xs:string""/></xs:sequence></xs:complexType></xs:element>
  </xs:schema></types>
  <message name=""GetLegacyPlansIn""><part name=""parameters"" element=""tns:GetLegacyPlansRequest""/></message>
  <message name=""GetLegacyPlanIn""><part name=""parameters"" element=""tns:GetLegacyPlanRequest""/></message>
  <portType name=""LegacyPensionPort"">
    <operation name=""GetLegacyPlans""><input message=""tns:GetLegacyPlansIn""/></operation>
    <operation name=""GetLegacyPlan""><input message=""tns:GetLegacyPlanIn""/></operation>
  </portType>
  <binding name=""LegacyPensionBinding"" type=""tns:LegacyPensionPort""><soap:binding transport=""http://schemas.xmlsoap.org/soap/http"" style=""document""/>
    <operation name=""GetLegacyPlans""><soap:operation soapAction=""GetLegacyPlans""/></operation>
    <operation name=""GetLegacyPlan""><soap:operation soapAction=""GetLegacyPlan""/></operation>
  </binding>
  <service name=""LegacyPensionService""><port name=""LegacyPensionPort"" binding=""tns:LegacyPensionBinding""><soap:address location=""{url}/soap/LegacyPensionService""/></port></service>
</definitions>";
}

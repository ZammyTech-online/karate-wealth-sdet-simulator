using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record RedactRequest(string? Text, string? Mode, string? CustomerId, string[]? Types);
public sealed record Finding(string Type, int Start, int End, string Replacement);

/// <summary>
/// redaction-service: quita datos personales de texto libre (mensajes, notas del asesor, transcripciones de voz).
/// Detecta número nacional (con Luhn), IBAN (mod 97), email, teléfono nórdico y tarjeta (Luhn). Solo marca lo que valida:
/// "20260101-0000" con el dígito de control mal NO es un número nacional, y eso también se prueba.
/// </summary>
public sealed class RedactionService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "redaction-service", "F-privacy", 6505, 3, "Privacidad y protección de datos",
        "Redacta PII de texto libre: número nacional, IBAN, email, teléfono y tarjeta, solo si validan. Modos MASK, REMOVE y TOKENIZE (vía pii-vault).",
        DependsOn: new[] { "pii-vault", "clock-service" });

    public static readonly string[] Modes = { "MASK", "REMOVE", "TOKENIZE" };
    public static readonly string[] Detectors = { "NATIONAL_ID", "IBAN", "EMAIL", "PHONE", "CARD" };

    private static readonly (string Type, Regex Re)[] Patterns =
    {
        ("EMAIL", new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")),
        ("IBAN", new Regex(@"\b(SE|NO|DK|FI)\d{2}(?:[ ]?[A-Z0-9]{2,4}){3,7}\b")),
        ("PHONE", new Regex(@"\+(?:46|47|45|358)(?:[ -]?\d){6,11}")),
        ("NATIONAL_ID", new Regex(@"\b(?:19|20)?\d{6}[-+]\d{4}\b|\b(?:19|20)\d{10}\b")),
        ("CARD", new Regex(@"\b(?:\d[ -]?){12,18}\d\b")),
    };

    public static bool Luhn(string digits)
    {
        int sum = 0; bool dbl = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (dbl) { d *= 2; if (d > 9) d -= 9; }
            sum += d; dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    public static string? NormalizeNationalId(string raw, int currentYear)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            var yy = int.Parse(digits[..2]);
            var century = raw.Contains('+') ? (yy + 2000 <= currentYear ? 19 : 18) : (yy + 2000 <= currentYear ? 20 : 19);
            digits = century + digits;
        }
        if (digits.Length != 12) return null;
        var candidate = digits[..8] + "-" + digits[8..];
        return NationalId.Check(candidate).Valid ? candidate : null;
    }

    public static List<(string Type, int Start, int End, string Value)> Detect(string text, ISet<string> wanted, int year)
    {
        var found = new List<(string, int, int, string)>();
        bool Overlaps(int s, int e) => found.Any(f => s < f.Item3 && e > f.Item2);
        foreach (var (type, re) in Patterns)
        {
            if (!wanted.Contains(type)) continue;
            foreach (Match m in re.Matches(text))
            {
                var raw = m.Value;
                if (type == "IBAN")
                {
                    // Lo que va pegado detrás (" KR", " OK") no forma parte del IBAN: recorta grupos hasta que valide
                    while (!IbanMath.IsValid(IbanMath.Normalize(raw)) && raw.LastIndexOf(' ') > 4) raw = raw[..raw.LastIndexOf(' ')];
                }
                if (Overlaps(m.Index, m.Index + raw.Length)) continue;
                string? value = type switch
                {
                    "EMAIL" => raw.ToLowerInvariant(),
                    "IBAN" => IbanMath.IsValid(IbanMath.Normalize(raw)) ? IbanMath.Normalize(raw) : null,
                    "PHONE" => m.Value.Replace(" ", "").Replace("-", ""),
                    "NATIONAL_ID" => NormalizeNationalId(m.Value, year),
                    _ => Luhn(new string(m.Value.Where(char.IsDigit).ToArray())) ? new string(m.Value.Where(char.IsDigit).ToArray()) : null,
                };
                if (value is not null) found.Add((type, m.Index, m.Index + raw.Length, value));
            }
        }
        return found.OrderBy(f => f.Item2).ToList();
    }

    public static string MaskOf(string type, string v) => type switch
    {
        "NATIONAL_ID" => NationalId.Mask(v),
        "IBAN" => IbanMath.Mask(v),
        "EMAIL" => v[..1] + "***@" + v.Split('@')[1],
        "PHONE" => v[..3] + new string('*', v.Length - 5) + v[^2..],
        _ => "**** **** **** " + v[^4..],
    };

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/redact", async (HttpContext http, RedactRequest b) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            var mode = (b.Mode ?? "MASK").ToUpperInvariant();
            var errors = new List<FieldError>();
            if (b.Text is null) errors.Add(new FieldError("text", "obligatorio"));
            else if (b.Text.Length > 100_000) errors.Add(new FieldError("text", "máximo 100 000 caracteres"));
            if (!Modes.Contains(mode)) errors.Add(new FieldError("mode", "MASK, REMOVE o TOKENIZE"));
            var unknown = (b.Types ?? Array.Empty<string>()).Where(t => !Detectors.Contains(t.ToUpperInvariant())).ToList();
            if (unknown.Count > 0) errors.Add(new FieldError("types", "desconocidos: " + string.Join(", ", unknown)));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Petición de redacción no válida.", "Ejemplo: { \"text\": \"Mi IBAN es SE45 5000 ...\", \"mode\": \"MASK\" }", errors);
            var wanted = new HashSet<string>((b.Types is { Length: > 0 } ? b.Types.Select(t => t.ToUpperInvariant()) : Detectors));
            var found = Detect(b.Text!, wanted, clock.Today.Year);
            var sb = new StringBuilder(); var findings = new List<Finding>(); var pos = 0;
            foreach (var (type, start, end, value) in found)
            {
                sb.Append(b.Text!, pos, start - pos);
                string replacement;
                if (mode == "REMOVE" || (mode == "TOKENIZE" && type == "CARD")) replacement = $"[{type}]";
                else if (mode == "MASK") replacement = MaskOf(type, value);
                else
                {
                    var t = await Downstream.Post(http, "pii-vault", "/api/v1/tokens", new { type = type, value, customerId = b.CustomerId });
                    if (t.Status is 401 or 403) return Problems.Of(http, 403, "TOKENIZE_NOT_ALLOWED", "Prohibido", "Tu rol no puede tokenizar en pii-vault; usa MASK o REMOVE.", null);
                    if (!t.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "pii-vault respondió " + t.Status, null);
                    replacement = t.Str("token")!;
                }
                sb.Append(replacement);
                findings.Add(new Finding(type, start, end, replacement));
                pos = end;
            }
            sb.Append(b.Text!, pos, b.Text!.Length - pos);
            var counts = Detectors.ToDictionary(d => d, d => findings.Count(f => f.Type == d));
            return Results.Ok(new { mode, redacted = sb.ToString(), findings, counts, total = findings.Count, containsPii = findings.Count > 0 });
        }).WithTags("Redacción").WithSummary("Redactar PII de un texto (MASK, REMOVE o TOKENIZE)");

        app.MapPost("/api/v1/scan", (HttpContext http, RedactRequest b) =>
        {
            if (Auth.Require(http, clock, out _) is { } denied) return denied;
            if (b.Text is null) return Problems.BadRequest(http, "VALIDATION_FAILED", "Falta text.", null);
            var found = Detect(b.Text, new HashSet<string>(Detectors), clock.Today.Year);
            return Results.Ok(new { containsPii = found.Count > 0, findings = found.Select(f => new { type = f.Type, start = f.Start, end = f.End }), total = found.Count });
        }).WithTags("Redacción").WithSummary("Solo detectar: ¿hay PII y dónde? (no devuelve los valores)");

        app.MapGet("/api/v1/detectors", () => Results.Ok(new
        {
            detectors = new object[]
            {
                new { type = "NATIONAL_ID", rule = "AAAAMMDD-NNNN o AAMMDD-NNNN (+ si tiene más de 100 años) y dígito de control Luhn" },
                new { type = "IBAN", rule = "SE, NO, DK o FI con longitud y dígitos de control mod 97 correctos (admite espacios)" },
                new { type = "EMAIL", rule = "algo@dominio.tld" },
                new { type = "PHONE", rule = "+46, +47, +45 o +358 seguido de 7 a 12 dígitos" },
                new { type = "CARD", rule = "13 a 19 dígitos con Luhn válido" },
            },
            modes = Modes,
        })).WithTags("Redacción").WithSummary("Qué detecta y con qué reglas");
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Nordvik.Shared;

/// <summary>
/// JSON canónico de Nordvik: claves ordenadas (orden ordinal, igual que Array.sort de JavaScript),
/// sin espacios, números sin ceros sobrantes y caracteres no ASCII tal cual.
/// Es lo que se firma y se resume con SHA-256 (contentHash). Tu test lo puede reproducir con
/// nordvik.core.Canonical (Java) o con JSON.stringify de un objeto con las claves ordenadas.
/// </summary>
public static class Canonical
{
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    public static string Of(JsonElement e)
    {
        var sb = new StringBuilder();
        Write(sb, e);
        return sb.ToString();
    }

    public static string Of(object value) => Of(JsonSerializer.SerializeToElement(value, value.GetType(), Web));
    public static string OfJson(string json) => Of(JsonDocument.Parse(json).RootElement);

    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static void Write(StringBuilder sb, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(Str(p.Name)).Append(':');
                    Write(sb, p.Value);
                }
                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var i = 0;
                foreach (var item in e.EnumerateArray())
                {
                    if (i++ > 0) sb.Append(',');
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            case JsonValueKind.String: sb.Append(Str(e.GetString()!)); break;
            case JsonValueKind.Number: sb.Append(Number(e.GetRawText())); break;
            case JsonValueKind.True: sb.Append("true"); break;
            case JsonValueKind.False: sb.Append("false"); break;
            default: sb.Append("null"); break;
        }
    }

    private static string Str(string s) => "\"" + Encoder.Encode(s) + "\"";

    private static string Number(string raw)
    {
        if (raw.Contains('e') || raw.Contains('E') || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return raw;
        var s = d.ToString(CultureInfo.InvariantCulture);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s == "-0" ? "0" : s;
    }

    public static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>SHA-256 (hex en minúsculas) del JSON canónico.</summary>
    public static string Hash(JsonElement e) => Sha256Hex(Of(e));
    public static string Hash(object value) => Sha256Hex(Of(value));

    public static string HmacHex(string key, string message) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
}

using System.Numerics;
using System.Text;

namespace Nordvik.Shared;

/// <summary>Cálculo de IBAN (ISO 13616, mod 97) compartido por los servicios que lo necesitan sin llamar a iban-service.</summary>
public static class IbanMath
{
    public static readonly IReadOnlyDictionary<string, int> Lengths = new Dictionary<string, int> { ["SE"] = 24, ["NO"] = 15, ["DK"] = 18, ["FI"] = 18 };

    private static BigInteger Numeric(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetter(c) ? (char.ToUpperInvariant(c) - 'A' + 10).ToString() : c.ToString());
        return BigInteger.Parse(sb.ToString());
    }

    public static string Make(string country, string bban)
    {
        var check = 98 - (int)(Numeric(bban + country + "00") % 97);
        return country + check.ToString("00") + bban;
    }

    public static string Normalize(string? iban) => (iban ?? "").Replace(" ", "").ToUpperInvariant();

    public static bool IsValid(string? iban)
    {
        var s = Normalize(iban);
        if (s.Length < 5 || !Lengths.TryGetValue(s[..2], out var len) || s.Length != len || !s.All(char.IsLetterOrDigit)) return false;
        return Numeric(s[4..] + s[..4]) % 97 == 1;
    }

    public static string Print(string iban) => string.Join(" ", Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));

    /// <summary>Cuenta de clientes de Nordvik en Älvbanken (donde entran los ingresos y salen las retiradas).</summary>
    public static readonly string ClientMoneyAccount = Make("SE", "50000000058398257466");
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Nordvik.Shared;

/// <summary>Resultado de validar un número nacional sueco ficticio (personnummer).</summary>
public sealed record NationalIdCheck(bool Valid, string? Normalized, string? BirthDate, string? Gender, string? Reason);

/// <summary>
/// Número nacional sueco en formato AAAAMMDD-NNNC: fecha de nacimiento, 3 dígitos de serie (el tercero, par = mujer)
/// y un dígito de control Luhn calculado sobre AAMMDDNNN. Todos los números de Nordvik son sintéticos.
/// </summary>
public static class NationalId
{
    private static readonly Regex Format = new(@"^(\d{8})-?(\d{4})$");

    public static int LuhnDigit(string nineDigits)
    {
        var sum = 0;
        for (var i = 0; i < nineDigits.Length; i++)
        {
            var d = nineDigits[i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return (10 - sum % 10) % 10;
    }

    public static string Create(DateOnly birth, int serial, bool female)
    {
        serial = Math.Clamp(serial, 0, 99);
        var genderDigit = female ? (serial % 5) * 2 : (serial % 5) * 2 + 1;
        var series = $"{serial / 5 % 10}{serial % 10}{genderDigit}";
        var nine = birth.ToString("yyMMdd", CultureInfo.InvariantCulture) + series;
        return $"{birth:yyyyMMdd}-{series}{LuhnDigit(nine)}";
    }

    public static NationalIdCheck Check(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new(false, null, null, null, "vacío");
        var m = Format.Match(value.Trim());
        if (!m.Success) return new(false, null, null, null, "formato: AAAAMMDD-NNNN");
        if (!DateOnly.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
            return new(false, null, null, null, "la fecha de nacimiento no existe");
        var digits = m.Groups[2].Value;
        var nine = m.Groups[1].Value[2..] + digits[..3];
        if (LuhnDigit(nine) != digits[3] - '0') return new(false, null, null, null, "el dígito de control no cuadra (Luhn)");
        var gender = (digits[2] - '0') % 2 == 0 ? "F" : "M";
        return new(true, $"{m.Groups[1].Value}-{digits}", birth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), gender, null);
    }

    /// <summary>Enmascarado para quien no puede ver el dato completo: 198805**-****.</summary>
    public static string Mask(string normalized) => normalized.Length >= 13 ? normalized[..6] + "**-****" : "********-****";
}

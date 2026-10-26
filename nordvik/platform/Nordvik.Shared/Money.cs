using System.Globalization;

namespace Nordvik.Shared;

/// <summary>Importes como texto decimal ("50000.00"): nunca float. Redondeo bancario (mitad al par).</summary>
public static class Money
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Format(decimal value, int scale) =>
        Math.Round(value, scale, MidpointRounding.ToEven).ToString("F" + scale, Inv);

    public static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Inv, out value);

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", Inv, DateTimeStyles.None, out date);

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
}

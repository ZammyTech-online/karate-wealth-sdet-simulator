namespace Nordvik.Shared;

/// <summary>Fondos ficticios de Nordvik: semilla común de product-catalog y market-data (cada uno la copia a su propia base de datos).</summary>
public sealed record FundSeedRow(string Isin, string Name, string Category, string Currency, int Sri, string Status,
    string ManagementFee, string EntryFee, string ExitFee, string LaunchDate, string Manager, decimal StartNav, double Drift, double Volatility);

public static class FundSeed
{
    public static readonly IReadOnlyList<FundSeedRow> All = new List<FundSeedRow>
    {
        new("SE0000000001", "Nordvik Global Equity", "EQUITY", "SEK", 4, "OPEN", "0.45", "0.00", "0.00", "2012-03-01", "Equipo Renta Variable Global", 142.3817m, 0.00030, 0.009),
        new("SE0000000002", "Nordvik Sverige Index", "EQUITY", "SEK", 5, "OPEN", "0.20", "0.00", "0.00", "2009-09-15", "Equipo Indexados", 318.5520m, 0.00025, 0.011),
        new("SE0000000003", "Nordvik Nordic Small Cap", "EQUITY", "SEK", 6, "OPEN", "1.25", "1.00", "0.00", "2015-01-12", "Equipo Pequeñas Compañías", 87.1094m, 0.00035, 0.014),
        new("SE0000000004", "Nordvik Emerging Markets", "EQUITY", "USD", 6, "OPEN", "1.10", "0.50", "0.00", "2014-06-02", "Equipo Emergentes", 24.6631m, 0.00020, 0.013),
        new("SE0000000005", "Nordvik Europe ESG", "EQUITY", "EUR", 4, "OPEN", "0.60", "0.00", "0.00", "2019-11-04", "Equipo Sostenibilidad", 15.9072m, 0.00028, 0.010),
        new("SE0000000006", "Nordvik Tech Growth", "EQUITY", "USD", 7, "OPEN", "1.50", "1.00", "0.50", "2018-02-20", "Equipo Tecnología", 51.2280m, 0.00045, 0.019),
        new("SE0000000007", "Nordvik Frontier Markets", "EQUITY", "USD", 7, "CLOSED", "1.75", "2.00", "1.00", "2016-08-29", "Equipo Emergentes", 9.4410m, -0.00010, 0.016),
        new("SE0000000101", "Nordvik Obligation Kort", "FIXED_INCOME", "SEK", 2, "OPEN", "0.15", "0.00", "0.00", "2008-04-01", "Equipo Renta Fija", 104.2201m, 0.00008, 0.0015),
        new("SE0000000102", "Nordvik Obligation Lång", "FIXED_INCOME", "SEK", 3, "OPEN", "0.25", "0.00", "0.00", "2010-10-11", "Equipo Renta Fija", 121.7745m, 0.00010, 0.0030),
        new("SE0000000103", "Nordvik Euro Credit", "FIXED_INCOME", "EUR", 3, "OPEN", "0.40", "0.00", "0.00", "2017-05-15", "Equipo Crédito", 11.0388m, 0.00009, 0.0025),
        new("SE0000000201", "Nordvik Balanserad", "MIXED", "SEK", 4, "OPEN", "0.80", "0.00", "0.00", "2007-01-08", "Equipo Multiactivo", 213.6609m, 0.00018, 0.0060),
        new("SE0000000202", "Nordvik Försiktig", "MIXED", "SEK", 3, "OPEN", "0.60", "0.00", "0.00", "2011-03-21", "Equipo Multiactivo", 132.0457m, 0.00012, 0.0040),
        new("SE0000000203", "Nordvik Generation 2050", "MIXED", "SEK", 5, "OPEN", "0.35", "0.00", "0.00", "2020-01-15", "Equipo Pensiones", 18.7753m, 0.00026, 0.0085),
        new("SE0000000301", "Nordvik Penningmarknad", "MONEY_MARKET", "SEK", 1, "OPEN", "0.10", "0.00", "0.00", "2006-06-01", "Equipo Liquidez", 100.9312m, 0.00006, 0.0003),
    };

    public static readonly string[] Categories = { "EQUITY", "FIXED_INCOME", "MIXED", "MONEY_MARKET" };
}

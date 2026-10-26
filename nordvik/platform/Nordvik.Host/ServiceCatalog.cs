using Nordvik.Services;
using Nordvik.Shared;

namespace Nordvik.Host;

/// <summary>Todos los microservicios que este anfitrión sabe arrancar. Cada oleada añade los suyos aquí.</summary>
public static class ServiceCatalog
{
    public static IReadOnlyList<INordvikService> Create() => new INordvikService[]
    {
        // Oleada 0 · A-utilities
        new UuidService(),
        new SequenceService(),
        new ClockService(),
        new RandomSeedService(),
        // Oleada 0 · I-market
        new ProductCatalog(),
        new MarketData(),
        new FxRates(),
        new CalendarService(),
    };
}

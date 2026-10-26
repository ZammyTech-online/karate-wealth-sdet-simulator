using System.Collections.Concurrent;

namespace Nordvik.Shared;

/// <summary>Dónde vive cada servicio. El anfitrión lo rellena al arrancar con todos los servicios del catálogo.</summary>
public static class ServiceDirectory
{
    private static readonly ConcurrentDictionary<string, int> Ports = new();
    public static int Offset { get; set; }

    public static void Register(IEnumerable<NordvikServiceInfo> infos)
    {
        foreach (var i in infos) Ports[i.Name] = i.Port;
    }

    public static string UrlOf(string service) =>
        Ports.TryGetValue(service, out var port)
            ? $"http://localhost:{port + Offset}"
            : throw new InvalidOperationException("Servicio desconocido en el directorio: " + service);

    public static IReadOnlyDictionary<string, int> All => Ports;
}

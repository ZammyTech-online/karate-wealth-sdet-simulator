using System.Collections.Concurrent;

namespace Nordvik.Shared;

/// <summary>Dónde vive cada servicio. El anfitrión lo rellena al arrancar con todos los servicios del catálogo.</summary>
public static class ServiceDirectory
{
    private static readonly ConcurrentDictionary<string, int> Ports = new();
    private static readonly ConcurrentDictionary<string, NordvikServiceInfo> InfoByName = new();
    public static int Offset { get; set; }

    public static void Register(IEnumerable<NordvikServiceInfo> infos)
    {
        foreach (var i in infos) { Ports[i.Name] = i.Port; InfoByName[i.Name] = i; }
    }

    public static string UrlOf(string service) =>
        Ports.TryGetValue(service, out var port)
            ? $"http://localhost:{port + Offset}"
            : throw new InvalidOperationException("Servicio desconocido en el directorio: " + service);

    public static IReadOnlyDictionary<string, int> All => Ports;
    public static IReadOnlyCollection<NordvikServiceInfo> Infos => InfoByName.Values.ToList();
    public static bool Knows(string service) => Ports.ContainsKey(service);
}

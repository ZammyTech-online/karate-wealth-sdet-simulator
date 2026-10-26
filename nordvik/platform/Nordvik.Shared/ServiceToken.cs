namespace Nordvik.Shared;

/// <summary>
/// Token de máquina (OAuth2 client_credentials, cliente nordvik-internal) para las sagas que llaman a otros servicios sin un usuario delante.
/// Se cachea hasta 30 segundos antes de que caduque según el reloj virtual.
/// </summary>
public static class ServiceToken
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _token;
    private static long _exp;

    public static async Task<string> Get(IClock clock)
    {
        await Gate.WaitAsync();
        try
        {
            var now = clock.UtcNow.ToUnixTimeSeconds();
            if (_token is not null && now < _exp - 30) return _token;
            var body = "grant_type=client_credentials&client_id=nordvik-internal&client_secret=internal-secret-2026";
            var r = await Downstream.SendUrl(HttpMethod.Post, ServiceDirectory.UrlOf("iam-service") + "/api/v1/oauth/token",
                new FormBody(body), null, TimeSpan.FromSeconds(5));
            if (!r.Ok) throw new DependencyUnavailableException("iam-service", "iam-service no dio token de servicio: " + r.Status);
            _token = r.Str("access_token")!;
            _exp = now + r.Json.GetProperty("expires_in").GetInt64();
            return _token;
        }
        finally { Gate.Release(); }
    }

    public static async Task<Dictionary<string, string>> Header(IClock clock) => new() { ["Authorization"] = "Bearer " + await Get(clock) };
}

/// <summary>Cuerpo application/x-www-form-urlencoded para Downstream.SendUrl.</summary>
public sealed record FormBody(string Encoded);

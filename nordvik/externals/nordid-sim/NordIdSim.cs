using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record NordIdAuthRequest(string? PersonalNumber, string? EndUserIp, string? UserVisibleData);
public sealed record NordIdSignRequest(string? PersonalNumber, string? EndUserIp, string? UserVisibleData, string? UserNonVisibleData);
public sealed record NordIdOrderRef(string? OrderRef);
public sealed record NordIdStarted(string OrderRef, string AutoStartToken, string QrStartToken);
public sealed record NordIdUser(string PersonalNumber, string Name, string GivenName, string Surname);
public sealed record NordIdCompletion(NordIdUser User, string Signature, string OcspResponse, string SignedAt);
public sealed record NordIdCollect(string OrderRef, string Status, string? HintCode, NordIdCompletion? CompletionData);
public sealed record ScenarioRequest(string? PersonalNumber, string? Outcome, int? PendingPolls);

/// <summary>
/// nordid-sim · sistema externo E1 (simulado): identidad electrónica tipo BankID.
/// auth/sign devuelven un orderRef; collect hay que llamarlo varias veces (pending → complete).
/// Errores al estilo del proveedor: { errorCode, details } — no RFC 9457, porque no es nuestro.
/// </summary>
public sealed class NordIdSim : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "nordid-sim", "X-external", 7701, 2, "Sistema externo (simulado)",
        "E1 NordID: autenticación y firma electrónica con orderRef y sondeo de collect. Escenarios por personalNumber en /__admin/scenarios.",
        DependsOn: new[] { "clock-service" });

    private sealed class Order
    {
        public required string OrderRef, Type, PersonalNumber, Outcome;
        public string? UserVisibleData, SignedAt;
        public int Polls, PendingPolls;
        public required DateTimeOffset CreatedAt;
        public string? Status;
        public bool Cancelled;
    }

    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private readonly ConcurrentDictionary<string, (string Outcome, int PendingPolls)> _scenarios = new();
    private readonly ExternalBehaviour _behaviour = new();
    private static readonly string[] Outcomes = { "complete", "userCancel", "expiredTransaction", "certificateErr", "startFailed" };
    private static readonly string[] Given = { "Astrid", "Erik", "Ingrid", "Lars", "Maja", "Nils", "Sofia", "Oskar", "Elin", "Hugo" };
    private static readonly string[] Family = { "Lindqvist", "Berg", "Nyström", "Holm", "Sandberg", "Ekström", "Wallin", "Lund" };

    private static IResult Error(int status, string code, string details) => Results.Json(new { errorCode = code, details }, statusCode: status);

    private static NordIdUser UserFor(string pn)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(pn));
        var g = Given[h[0] % Given.Length]; var f = Family[h[1] % Family.Length];
        return new NordIdUser(pn, g + " " + f, g, f);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        _behaviour.Map(app);
        var g = app.MapGroup("/rp/v6").WithTags("NordID RP API v6");

        async Task<IResult> Start(HttpContext http, IClock clock, string type, string? pn, string? ip, string? visible)
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (string.IsNullOrWhiteSpace(ip)) return Error(400, "invalidParameters", "endUserIp is required");
            if (pn is not null && !NationalId.Check(pn).Valid) return Error(400, "invalidParameters", "personalNumber is not valid");
            if (type == "sign" && string.IsNullOrWhiteSpace(visible)) return Error(400, "invalidParameters", "userVisibleData is required for sign");
            if (visible is not null)
            {
                try { if (Convert.FromBase64String(visible).Length > 40000) return Error(400, "invalidParameters", "userVisibleData too large"); }
                catch (FormatException) { return Error(400, "invalidParameters", "userVisibleData must be base64"); }
            }
            var number = pn ?? "19900101-0017";
            if (_orders.Values.Any(o => o.PersonalNumber == number && o.Status == "pending" && !o.Cancelled && (clock.UtcNow - o.CreatedAt).TotalSeconds < 180))
                return Error(409, "alreadyInProgress", "An order for this user is already in progress");
            var sc = _scenarios.TryGetValue(number, out var s) ? s : ("complete", 2);
            var order = new Order { OrderRef = Guid.NewGuid().ToString(), Type = type, PersonalNumber = number, Outcome = sc.Item1, PendingPolls = sc.Item2,
                UserVisibleData = visible, CreatedAt = clock.UtcNow, Status = "pending" };
            _orders[order.OrderRef] = order;
            return Results.Ok(new NordIdStarted(order.OrderRef, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()));
        }

        g.MapPost("/auth", (HttpContext http, IClock clock, NordIdAuthRequest b) => Start(http, clock, "auth", b.PersonalNumber, b.EndUserIp, b.UserVisibleData))
            .WithSummary("Iniciar una autenticación");
        g.MapPost("/sign", (HttpContext http, IClock clock, NordIdSignRequest b) => Start(http, clock, "sign", b.PersonalNumber, b.EndUserIp, b.UserVisibleData))
            .WithSummary("Iniciar una firma (userVisibleData en base64 es lo que el usuario ve y firma)");

        g.MapPost("/collect", async (IClock clock, NordIdOrderRef b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (b.OrderRef is null || !_orders.TryGetValue(b.OrderRef, out var o)) return Error(400, "invalidParameters", "No such order");
            if (o.Cancelled) return Error(400, "invalidParameters", "No such order");
            if (o.Status is "complete" or "failed") return Results.Ok(Collect(o));
            if ((clock.UtcNow - o.CreatedAt).TotalSeconds > 180) { o.Status = "failed"; o.Outcome = "expiredTransaction"; return Results.Ok(Collect(o)); }
            o.Polls++;
            if (o.Polls > o.PendingPolls) o.Status = o.Outcome == "complete" ? "complete" : "failed";
            if (o.Status == "complete") o.SignedAt = Nordic.Iso(clock.LocalNow);
            return Results.Ok(Collect(o));
        }).WithSummary("Consultar el estado de un orderRef (llámalo cada 1-2 s hasta complete o failed)");

        g.MapPost("/cancel", async (NordIdOrderRef b) =>
        {
            if (await _behaviour.Apply() is { } fail) return fail;
            if (b.OrderRef is null || !_orders.TryGetValue(b.OrderRef, out var o) || o.Cancelled) return Error(400, "invalidParameters", "No such order");
            o.Cancelled = true;
            return Results.Ok(new { });
        }).WithSummary("Cancelar un orderRef");

        app.MapPost("/__admin/scenarios", (ScenarioRequest b) =>
        {
            if (b.PersonalNumber is null || b.Outcome is null || !Outcomes.Contains(b.Outcome))
                return Error(400, "invalidParameters", "personalNumber and outcome (" + string.Join(", ", Outcomes) + ") are required");
            _scenarios[b.PersonalNumber] = (b.Outcome, Math.Clamp(b.PendingPolls ?? 2, 0, 20));
            return Results.Ok(new { b.PersonalNumber, b.Outcome, pendingPolls = _scenarios[b.PersonalNumber].PendingPolls });
        }).WithTags("Administración del simulador").WithSummary("Decidir qué pasará con las órdenes de un personalNumber");
        app.MapDelete("/__admin/scenarios", () => { _scenarios.Clear(); _orders.Clear(); return Results.NoContent(); })
            .WithTags("Administración del simulador").WithSummary("Borrar escenarios y órdenes");
    }

    private static NordIdCollect Collect(Order o)
    {
        if (o.Status == "complete")
        {
            var sig = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes("nordid-sim"), Encoding.UTF8.GetBytes(o.OrderRef + "|" + o.PersonalNumber + "|" + (o.UserVisibleData ?? ""))));
            return new NordIdCollect(o.OrderRef, "complete", null, new NordIdCompletion(UserFor(o.PersonalNumber), sig, "MIIH" + o.OrderRef[..8], o.SignedAt ?? ""));
        }
        if (o.Status == "failed") return new NordIdCollect(o.OrderRef, "failed", o.Outcome, null);
        return new NordIdCollect(o.OrderRef, "pending", o.Polls <= 1 ? "outstandingTransaction" : "userSign", null);
    }
}

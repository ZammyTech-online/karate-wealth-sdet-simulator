using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record MemberRequest(string? Relation, string? Name, string? BirthDate, string? NationalId, bool? Dependent);
public sealed record HouseholdRequest(MemberRequest[]? Members);

/// <summary>
/// household-service: la unidad familiar del cliente (cónyuge o pareja, hijos, otros dependientes). El número nacional de cada miembro
/// nunca se guarda aquí: se tokeniza en pii-vault y solo queda el token y la máscara. Lo usan pensiones (beneficiarios) y asesoramiento.
/// </summary>
public sealed class HouseholdService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "household-service", "D-customer", 6306, 3, "Cliente",
        "Unidad familiar: cónyuge o pareja, hijos y dependientes, con reglas de coherencia (una sola pareja, hijos nacidos después que el cliente) y números nacionales tokenizados.",
        DependsOn: new[] { "customer-registry", "pii-vault", "permission-service", "clock-service" }, Emits: new[] { "household.updated" });

    public static readonly string[] Relations = { "SPOUSE", "PARTNER", "CHILD", "OTHER_DEPENDENT" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute("CREATE TABLE IF NOT EXISTS households (customer_id TEXT PRIMARY KEY, members TEXT, updated_at TEXT, updated_by TEXT, version INTEGER)");
    }

    private static object View(string customerId, string members, string at, long v, DateOnly today)
    {
        var list = JsonSerializer.Deserialize<List<JsonElement>>(members)!;
        int Age(JsonElement m) { var b = DateOnly.Parse(m.GetProperty("birthDate").GetString()!); var a = today.Year - b.Year; if (today < b.AddYears(a)) a--; return a; }
        return new
        {
            customerId, version = v, updatedAt = at,
            members = list.Select(m => new
            {
                id = m.GetProperty("id").GetString(), relation = m.GetProperty("relation").GetString(), name = m.GetProperty("name").GetString(), birthDate = m.GetProperty("birthDate").GetString(),
                age = Age(m), dependent = m.GetProperty("dependent").GetBoolean(), nationalIdToken = m.TryGetProperty("token", out var t) ? t.GetString() : null,
                nationalIdMasked = m.TryGetProperty("masked", out var mk) ? mk.GetString() : null,
            }),
            summary = new { size = list.Count + 1, children = list.Count(m => m.GetProperty("relation").GetString() == "CHILD"), minorChildren = list.Count(m => m.GetProperty("relation").GetString() == "CHILD" && Age(m) < 18),
                            dependents = list.Count(m => m.GetProperty("dependent").GetBoolean()) },
        };
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapGet("/api/v1/customers/{customerId}/household", async (HttpContext http, string customerId) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:read", customerId) is { } g) return g;
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string M, string At, long V)>("SELECT members, updated_at, version FROM households WHERE customer_id = @customerId", new { customerId });
            return Results.Ok(r.M is null ? View(customerId, "[]", "", 0, clock.Today) : View(customerId, r.M, r.At, r.V, clock.Today));
        }).WithTags("Unidad familiar").WithSummary("La unidad familiar (vacía si no se ha declarado)");

        app.MapPut("/api/v1/customers/{customerId}/household", async (HttpContext http, string customerId, HouseholdRequest b) =>
        {
            if (Auth.Require(http, clock, out var p) is { } denied) return denied;
            if (await CustomerGuard.Check(http, p, "customer:update", customerId) is { } g) return g;
            var cust = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var custBirth = DateOnly.Parse(cust.Str("birthDate")!);
            var members = b.Members ?? Array.Empty<MemberRequest>();
            var errors = new List<FieldError>();
            for (var i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m.Relation is null || !Relations.Contains(m.Relation.ToUpperInvariant())) errors.Add(new FieldError($"members[{i}].relation", string.Join(", ", Relations)));
                if (string.IsNullOrWhiteSpace(m.Name)) errors.Add(new FieldError($"members[{i}].name", "obligatorio"));
                if (!DateOnly.TryParse(m.BirthDate, out var bd)) errors.Add(new FieldError($"members[{i}].birthDate", "AAAA-MM-DD"));
                else
                {
                    if (bd > clock.Today) errors.Add(new FieldError($"members[{i}].birthDate", "en el futuro"));
                    if (m.Relation?.ToUpperInvariant() == "CHILD" && bd <= custBirth) errors.Add(new FieldError($"members[{i}].birthDate", "un hijo no puede ser mayor que el cliente"));
                }
                if (m.NationalId is not null && !NationalId.Check(m.NationalId).Valid) errors.Add(new FieldError($"members[{i}].nationalId", "número nacional no válido"));
            }
            if (members.Count(m => m.Relation?.ToUpperInvariant() is "SPOUSE" or "PARTNER") > 1) errors.Add(new FieldError("members", "solo puede haber un cónyuge o pareja"));
            if (members.Where(m => m.NationalId is not null).GroupBy(m => m.NationalId).Any(gr => gr.Count() > 1)) errors.Add(new FieldError("members", "un número nacional repetido"));
            if (errors.Count > 0) return Problems.Unprocessable(http, "INVALID_HOUSEHOLD", "La unidad familiar no es coherente.",
                "Ejemplo: { \"members\": [ { \"relation\": \"CHILD\", \"name\": \"Elsa\", \"birthDate\": \"2015-04-02\", \"dependent\": true } ] }", errors);
            var svc = await ServiceToken.Header(clock);
            var stored = new List<object>();
            foreach (var m in members)
            {
                string? token = null, masked = null;
                if (m.NationalId is not null)
                {
                    var t = await Downstream.Send(http, "pii-vault", HttpMethod.Post, "/api/v1/tokens", new { type = "NATIONAL_ID", value = m.NationalId, customerId }, forwardAuth: false, headers: svc);
                    if (!t.Ok) return Problems.Of(http, 502, "DEPENDENCY_ERROR", "Error en una dependencia", "pii-vault respondió " + t.Status, null);
                    token = t.Str("token"); masked = t.Str("masked");
                }
                var relation = m.Relation!.ToUpperInvariant();
                stored.Add(new { id = "mem_" + Guid.NewGuid().ToString("N")[..10], relation, name = m.Name, birthDate = DateOnly.Parse(m.BirthDate!).ToString("yyyy-MM-dd"),
                                 dependent = m.Dependent ?? relation == "CHILD", token, masked });
            }
            var json = JsonSerializer.Serialize(stored);
            using var db = ctx.OpenDb();
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO households VALUES (@customerId, @json, @at, @by, 1) ON CONFLICT(customer_id) DO UPDATE SET members = @json, updated_at = @at, updated_by = @by, version = version + 1",
                new { customerId, json, at = Nordic.Iso(clock.LocalNow), by = p.Sub }, tx);
            Outbox.Add(db, http, clock, "household.updated", customerId, new { customerId, size = stored.Count + 1 }, tx);
            tx.Commit();
            var r = db.QuerySingle<(string M, string At, long V)>("SELECT members, updated_at, version FROM households WHERE customer_id = @customerId", new { customerId });
            return Results.Ok(View(customerId, r.M, r.At, r.V, clock.Today));
        }).WithTags("Unidad familiar").WithSummary("Declarar la unidad familiar completa (sustituye la anterior)");
    }
}

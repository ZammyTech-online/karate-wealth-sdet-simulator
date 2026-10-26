using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record AddBankAccountRequest(string? Iban, string? Label);
public sealed record ReviewRequest(string? Decision, string? Comment);
public sealed record BankAccount(string Id, string CustomerId, string MaskedIban, string Country, string? Label, string Status, string? BankResult, string? Reason, string CreatedAt, string? ReviewedBy);

/// <summary>
/// bank-account-verification: el cliente solo puede retirar dinero a cuentas suyas. Valida el IBAN, pregunta al banco (E6) si el
/// titular coincide con el número nacional del cliente y guarda el resultado: VERIFIED, NEEDS_REVIEW (parecido) o REJECTED.
/// Añadir una cuenta como cliente exige step-up (token acr=high). El IBAN completo nunca sale en las respuestas públicas.
/// </summary>
public sealed class BankAccountVerification : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "bank-account-verification", "K-payments", 7002, 2, "Pagos y contabilidad",
        "Cuentas bancarias de destino del cliente: IBAN válido, titularidad confirmada por el banco, revisión manual si hay dudas y step-up para añadirlas.",
        DependsOn: new[] { "nordpay-adapter", "customer-registry", "permission-service", "clock-service" }, Emits: new[] { "bank-account.verified", "bank-account.rejected" });

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS accounts (id TEXT PRIMARY KEY, customer_id TEXT, iban TEXT, label TEXT, status TEXT, bank_result TEXT, reason TEXT, created_at TEXT, reviewed_by TEXT,
                     UNIQUE (customer_id, iban))");
    }


    private static BankAccount? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Cust, string Iban, string? Label, string Status, string? Bank, string? Reason, string Created, string? Reviewed)>(
            "SELECT id, customer_id, iban, label, status, bank_result, reason, created_at, reviewed_by FROM accounts WHERE id = @id", new { id });
        return r.Id is null ? null : new BankAccount(r.Id, r.Cust, IbanMath.Mask(r.Iban), r.Iban[..2], r.Label, r.Status, r.Bank, r.Reason, r.Created, r.Reviewed);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/customers/{customerId}/bank-accounts", async (HttpContext http, IClock clock, string customerId, AddBankAccountRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "bank-account:write", customerId);
            if (guard is not null) return guard;
            if (p.Role == "Customer" && !p.IsHighAssurance)
                return Problems.Of(http, 403, "STEP_UP_REQUIRED", "Hace falta re-autenticarse", "Añadir una cuenta de destino exige un token con acr=high.",
                    "Haz POST /api/v1/step-up en step-up-service (puerto 6205), completa NordID y usa el accessToken que devuelve.");
            var iban = IbanMath.Normalize(b.Iban);
            if (!IbanMath.IsValid(iban))
                return Problems.Unprocessable(http, "INVALID_IBAN", $"'{b.Iban}' no es un IBAN válido.", "Comprueba país, longitud y dígitos de control (iban-service, puerto 6007, lo valida).",
                    new[] { new FieldError("iban", "IBAN SE, NO, DK o FI con dígitos de control correctos") });
            using var db = ctx.OpenDb();
            if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM accounts WHERE customer_id = @customerId AND iban = @iban", new { customerId, iban }) > 0)
                return Problems.Conflict(http, "BANK_ACCOUNT_ALREADY_ADDED", "Esa cuenta ya está registrada para el cliente.", null);
            var c = await Downstream.Get(http, "customer-registry", $"/api/v1/internal/customers/{customerId}", forwardAuth: false);
            var v = await Downstream.Post(http, "nordpay-adapter", "/api/v1/account-verifications", new { iban, nationalId = c.Str("nationalId"), name = c.Str("firstName") + " " + c.Str("lastName") }, forwardAuth: false);
            if (v.Status == 503) return Problems.Of(http, 503, "BANK_UNAVAILABLE", "Banco no disponible", "No se pudo comprobar la titularidad: inténtalo más tarde.", null);
            var result = v.Ok ? v.Str("result") : "ERROR";
            var (status, reason) = result switch
            {
                "MATCH" => ("VERIFIED", (string?)null),
                "CLOSE_MATCH" => ("NEEDS_REVIEW", "El nombre del titular se parece pero no coincide del todo: lo revisa Operaciones."),
                "NO_MATCH" => ("REJECTED", "El titular de la cuenta no es el cliente."),
                "ACCOUNT_CLOSED" => ("REJECTED", "La cuenta está cerrada."),
                _ => ("REJECTED", "El banco no pudo verificar la cuenta."),
            };
            var id = "ba_" + Guid.NewGuid().ToString("N")[..16];
            using var tx = db.BeginTransaction();
            db.Execute("INSERT INTO accounts VALUES (@id, @customerId, @iban, @Label, @status, @result, @reason, @at, NULL)",
                new { id, customerId, iban, b.Label, status, result, reason, at = Nordic.Iso(clock.LocalNow) }, tx);
            if (status != "NEEDS_REVIEW") Outbox.Add(db, http, clock, status == "VERIFIED" ? "bank-account.verified" : "bank-account.rejected", id, new { bankAccountId = id, customerId, bankResult = result }, tx);
            tx.Commit();
            return Results.Created($"/api/v1/bank-accounts/{id}", Load(db, id));
        }).WithTags("Cuentas de destino").WithSummary("Añadir una cuenta (el cliente necesita token acr=high)").Produces<BankAccount>(201);

        app.MapGet("/api/v1/customers/{customerId}/bank-accounts", async (HttpContext http, IClock clock, string customerId) =>
        {
            var denied = Auth.Require(http, clock, out var p);
            if (denied is not null) return denied;
            var guard = await CustomerGuard.Check(http, p, "bank-account:read", customerId);
            if (guard is not null) return guard;
            using var db = ctx.OpenDb();
            return Results.Ok(db.Query<string>("SELECT id FROM accounts WHERE customer_id = @customerId ORDER BY created_at", new { customerId }).Select(i => Load(db, i)!).ToList());
        }).WithTags("Cuentas de destino").WithSummary("Cuentas de un cliente (IBAN enmascarado)").Produces<List<BankAccount>>();

        app.MapPost("/api/v1/bank-accounts/{id}/review", (HttpContext http, IClock clock, string id, ReviewRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Operations", "Admin" });
            if (denied is not null) return denied;
            if (b.Decision is not ("APPROVE" or "REJECT")) return Problems.BadRequest(http, "VALIDATION_FAILED", "decision: APPROVE o REJECT.", null);
            using var db = ctx.OpenDb();
            var a = Load(db, id);
            if (a is null) return Problems.NotFound(http, "BANK_ACCOUNT_NOT_FOUND", $"No existe la cuenta {id}.", null);
            if (a.Status != "NEEDS_REVIEW") return Problems.Conflict(http, "NOT_UNDER_REVIEW", $"La cuenta está {a.Status}: no hay nada que revisar.", null);
            var status = b.Decision == "APPROVE" ? "VERIFIED" : "REJECTED";
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE accounts SET status = @status, reason = @c, reviewed_by = @by WHERE id = @id", new { status, c = b.Comment, by = p.Sub, id }, tx);
            Outbox.Add(db, http, clock, status == "VERIFIED" ? "bank-account.verified" : "bank-account.rejected", id, new { bankAccountId = id, customerId = a.CustomerId, reviewedBy = p.Sub }, tx);
            tx.Commit();
            return Results.Ok(Load(db, id));
        }).WithTags("Cuentas de destino").WithSummary("Operaciones aprueba o rechaza una cuenta NEEDS_REVIEW").Produces<BankAccount>();

        app.MapGet("/api/v1/internal/bank-accounts/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var r = db.QuerySingleOrDefault<(string Id, string Cust, string Iban, string Status)>("SELECT id, customer_id, iban, status FROM accounts WHERE id = @id", new { id });
            return r.Id is null ? Problems.NotFound(http, "BANK_ACCOUNT_NOT_FOUND", $"No existe la cuenta {id}.", null)
                : Results.Ok(new { id = r.Id, customerId = r.Cust, iban = r.Iban, status = r.Status });
        }).WithTags("Interno").WithSummary("IBAN completo para payments-gateway (red interna)");
    }
}

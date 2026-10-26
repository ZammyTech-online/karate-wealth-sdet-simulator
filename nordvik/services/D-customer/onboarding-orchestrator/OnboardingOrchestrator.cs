using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record OnboardingPerson(string? FirstName, string? LastName, string? NationalId, string? Email, string? Phone, string? Language);
public sealed record OnboardingAddress(string? Street, string? Number, string? PostalCode, string? City);
public sealed record OnboardingRequest(OnboardingPerson? Person, OnboardingAddress? Address, string? Iban, int? RiskProfile, int? HorizonYears, bool? Esg, string? AccountType, string? ExternalRef);
public sealed record OnboardingStep(int No, string Name, string Status, string? ResourceId, string? Detail, string? At);
public sealed record Onboarding(string Id, string Status, string? CustomerId, string? CustomerNumber, string? PortfolioId, string? ContractId, string? BankAccountId,
    string? FailedStep, string? FailureReason, IReadOnlyList<OnboardingStep> Steps, string StartedBy, string StartedAt, string? FinishedAt);

/// <summary>
/// onboarding-orchestrator: el alta completa de un cliente (flujo F1) como saga de 8 pasos: cliente → domicilio → contacto → cuenta bancaria
/// (titularidad con el banco) → propuesta de asignación → cartera → contrato marco enviado a firma → bienvenida.
/// Si un paso falla, deshace los anteriores (compensación: cierra la cartera y el cliente). Termina COMPLETED cuando el cliente firma el contrato.
/// </summary>
public sealed class OnboardingOrchestrator : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "onboarding-orchestrator", "D-customer", 6305, 2, "Clientes",
        "Saga de alta F1 en 8 pasos con compensación; queda AWAITING_SIGNATURE hasta que el cliente firma el contrato marco (COMPLETED).",
        DependsOn: new[] { "customer-registry", "customer-address-service", "contact-service", "bank-account-verification", "allocation-engine", "portfolio-service",
            "contract-service", "notification-service", "iam-service", "event-bus", "clock-service" },
        Emits: new[] { "onboarding.completed", "onboarding.failed" }, Consumes: new[] { "contract.activated", "contract.expired" });

    private static readonly string[] StepNames = { "CUSTOMER", "ADDRESS", "CONTACT", "BANK_ACCOUNT", "ALLOCATION", "PORTFOLIO", "CONTRACT", "WELCOME" };

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS onboardings (id TEXT PRIMARY KEY, status TEXT, customer_id TEXT, customer_number TEXT, portfolio_id TEXT, contract_id TEXT, bank_account_id TEXT,
                        failed_step TEXT, failure TEXT, started_by TEXT, started_at TEXT, finished_at TEXT, external_ref TEXT);
                     CREATE TABLE IF NOT EXISTS steps (onboarding_id TEXT, no INTEGER, name TEXT, status TEXT, resource_id TEXT, detail TEXT, at TEXT, PRIMARY KEY (onboarding_id, no))");
    }

    public int ResetByPrefix(ServiceContext ctx, string prefix)
    {
        using var db = ctx.OpenDb();
        return db.Execute("DELETE FROM onboardings WHERE external_ref LIKE @p", new { p = prefix + "%" });
    }

    private static Onboarding? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var r = db.QuerySingleOrDefault<(string Id, string Status, string? Cust, string? Num, string? Pf, string? Ct, string? Ba, string? Fs, string? Fail, string By, string At, string? Fin)>(
            "SELECT id, status, customer_id, customer_number, portfolio_id, contract_id, bank_account_id, failed_step, failure, started_by, started_at, finished_at FROM onboardings WHERE id = @id", new { id });
        if (r.Id is null) return null;
        var steps = db.Query<(long No, string Name, string Status, string? Res, string? Detail, string? At)>("SELECT no, name, status, resource_id, detail, at FROM steps WHERE onboarding_id = @id ORDER BY no", new { id })
            .Select(s => new OnboardingStep((int)s.No, s.Name, s.Status, s.Res, s.Detail, s.At)).ToList();
        return new Onboarding(r.Id, r.Status, r.Cust, r.Num, r.Pf, r.Ct, r.Ba, r.Fs, r.Fail, steps, r.By, r.At, r.Fin);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);
        var clock = app.Services.GetRequiredService<IClock>();

        app.MapPost("/api/v1/onboardings", async (HttpContext http, OnboardingRequest b) =>
        {
            var denied = Auth.Require(http, clock, out var p, roles: new[] { "Advisor" });
            if (denied is not null) return denied;
            var errors = new List<FieldError>();
            if (b.Person is null) errors.Add(new FieldError("person", "obligatorio: firstName, lastName, nationalId, email"));
            if (b.Address is null) errors.Add(new FieldError("address", "obligatorio: street, number, postalCode, city"));
            if (string.IsNullOrWhiteSpace(b.Iban)) errors.Add(new FieldError("iban", "obligatorio: cuenta para ingresos y retiradas"));
            if (b.RiskProfile is null or < 1 or > 7) errors.Add(new FieldError("riskProfile", "de 1 a 7"));
            if (b.HorizonYears is null or < 1 or > 50) errors.Add(new FieldError("horizonYears", "de 1 a 50"));
            if (b.AccountType is not (null or "ISK" or "KF" or "DEPOT")) errors.Add(new FieldError("accountType", "ISK, KF o DEPOT"));
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Alta incompleta.", "Mira el ejemplo completo en la ficha de onboarding-orchestrator.", errors);
            var id = "onb_" + Guid.NewGuid().ToString("N")[..16];
            using var db = ctx.OpenDb();
            db.Execute("INSERT INTO onboardings (id, status, started_by, started_at, external_ref) VALUES (@id, 'IN_PROGRESS', @by, @at, @ExternalRef)",
                new { id, by = p.Sub, at = Nordic.Iso(clock.LocalNow), b.ExternalRef });
            for (var i = 0; i < StepNames.Length; i++) db.Execute("INSERT INTO steps VALUES (@id, @no, @name, 'PENDING', NULL, NULL, NULL)", new { id, no = i + 1, name = StepNames[i] });
            void Step(string name, string status, string? res, string? detail) =>
                db.Execute("UPDATE steps SET status = @status, resource_id = COALESCE(@res, resource_id), detail = @detail, at = @at WHERE onboarding_id = @id AND name = @name",
                    new { status, res, detail, at = Nordic.Iso(clock.LocalNow), id, name });

            string? customerId = null, portfolioId = null;
            async Task<IResult> Fail(string step, string reason)
            {
                Step(step, "FAILED", null, reason);
                // Compensación en orden inverso
                if (portfolioId is not null)
                {
                    var r = await Downstream.Send(http, "portfolio-service", HttpMethod.Delete, $"/api/v1/portfolios/{portfolioId}");
                    Step("PORTFOLIO", r.Ok ? "COMPENSATED" : "COMPENSATION_FAILED", null, r.Ok ? "cartera cerrada" : "no se pudo cerrar: " + r.Status);
                }
                if (customerId is not null)
                {
                    var r = await Downstream.Send(null, "customer-registry", HttpMethod.Delete, $"/api/v1/customers/{customerId}", null, forwardAuth: false, headers: await ServiceToken.Header(clock));
                    Step("CUSTOMER", r.Ok ? "COMPENSATED" : "COMPENSATION_FAILED", null, r.Ok ? "cliente cerrado" : "no se pudo cerrar: " + r.Status);
                }
                using var tx = db.BeginTransaction();
                db.Execute("UPDATE steps SET status = 'SKIPPED' WHERE onboarding_id = @id AND status = 'PENDING'", new { id }, tx);
                db.Execute("UPDATE onboardings SET status = 'FAILED', failed_step = @step, failure = @reason, finished_at = @at WHERE id = @id", new { step, reason, at = Nordic.Iso(clock.LocalNow), id }, tx);
                Outbox.Add(db, http, clock, "onboarding.failed", id, new { onboardingId = id, failedStep = step, reason }, tx);
                tx.Commit();
                return Results.Created($"/api/v1/onboardings/{id}", Load(db, id));
            }
            string Why(DownstreamReply r) => r.Str("code") is { } code ? $"{r.Status} {code}: {r.Str("detail")}" : $"HTTP {r.Status}";

            // 1 · cliente
            var c = await Downstream.Post(http, "customer-registry", "/api/v1/customers", new
            {
                firstName = b.Person!.FirstName, lastName = b.Person.LastName, nationalId = b.Person.NationalId, email = b.Person.Email, phone = b.Person.Phone,
                language = b.Person.Language ?? "sv", externalRef = b.ExternalRef,
            });
            if (!c.Ok) return await Fail("CUSTOMER", Why(c));
            customerId = c.Str("id");
            db.Execute("UPDATE onboardings SET customer_id = @customerId, customer_number = @n WHERE id = @id", new { customerId, n = c.Str("customerNumber"), id });
            Step("CUSTOMER", "DONE", customerId, c.Str("customerNumber"));
            // 2 · domicilio
            var a = await Downstream.Send(http, "customer-address-service", HttpMethod.Put, $"/api/v1/customers/{customerId}/address", new
            { street = b.Address!.Street, number = b.Address.Number, postalCode = b.Address.PostalCode, city = b.Address.City, country = "SE" });
            if (!a.Ok) return await Fail("ADDRESS", Why(a));
            Step("ADDRESS", "DONE", null, a.Str("city"));
            // 3 · contacto (queda pendiente de verificar: el cliente recibe el código)
            var ct = await Downstream.Post(http, "contact-service", $"/api/v1/customers/{customerId}/contacts", new { type = "EMAIL", value = b.Person.Email });
            if (!ct.Ok) return await Fail("CONTACT", Why(ct));
            Step("CONTACT", "DONE", ct.Str("id"), ct.Str("status"));
            // 4 · cuenta bancaria (lo hace la plataforma con su token de servicio)
            var ba = await Downstream.Send(null, "bank-account-verification", HttpMethod.Post, $"/api/v1/customers/{customerId}/bank-accounts", new { iban = b.Iban, label = "Alta" },
                forwardAuth: false, headers: await ServiceToken.Header(clock));
            if (!ba.Ok) return await Fail("BANK_ACCOUNT", Why(ba));
            if (ba.Str("status") == "REJECTED") return await Fail("BANK_ACCOUNT", $"cuenta rechazada por el banco ({ba.Str("bankResult")})");
            db.Execute("UPDATE onboardings SET bank_account_id = @b WHERE id = @id", new { b = ba.Str("id"), id });
            Step("BANK_ACCOUNT", "DONE", ba.Str("id"), ba.Str("status"));
            // 5 · propuesta de asignación
            var prop = await Downstream.Post(http, "allocation-engine", "/api/v1/allocations/propose", new { riskProfile = b.RiskProfile, horizonYears = b.HorizonYears, esg = b.Esg ?? false }, forwardAuth: false);
            if (!prop.Ok) return await Fail("ALLOCATION", Why(prop));
            Step("ALLOCATION", "DONE", null, $"{prop.Json.GetProperty("equityPercent").GetDecimal()} % renta variable");
            // 6 · cartera
            var pf = await Downstream.Post(http, "portfolio-service", "/api/v1/portfolios", new
            {
                customerId, name = "Cartera principal", type = b.AccountType ?? "ISK", externalRef = b.ExternalRef,
                targetAllocation = prop.Json.GetProperty("allocation").EnumerateArray().Select(l => new { isin = l.GetProperty("isin").GetString(), percent = l.GetProperty("percent").GetDecimal() }).ToList(),
            });
            if (!pf.Ok) return await Fail("PORTFOLIO", Why(pf));
            portfolioId = pf.Str("id");
            db.Execute("UPDATE onboardings SET portfolio_id = @portfolioId WHERE id = @id", new { portfolioId, id });
            Step("PORTFOLIO", "DONE", portfolioId, null);
            // 7 · contrato marco, enviado a firma
            var con = await Downstream.Post(http, "contract-service", "/api/v2/contracts", new { type = "MASTER_AGREEMENT", customerId, accountType = b.AccountType ?? "ISK" });
            if (!con.Ok) return await Fail("CONTRACT", Why(con));
            var sent = await Downstream.Post(http, "contract-service", $"/api/v2/contracts/{con.Str("id")}/send", null);
            if (!sent.Ok) return await Fail("CONTRACT", Why(sent));
            db.Execute("UPDATE onboardings SET contract_id = @c WHERE id = @id", new { c = con.Str("id"), id });
            Step("CONTRACT", "DONE", con.Str("id"), "PENDING_SIGNATURE");
            // 8 · bienvenida
            var w = await Downstream.Post(http, "notification-service", "/api/v1/notifications", new
            { customerId, templateKey = "welcome", vars = new { firstName = b.Person.FirstName, customerNumber = c.Str("customerNumber") }, reference = id }, forwardAuth: false);
            Step("WELCOME", w.Ok ? "DONE" : "FAILED", w.Ok ? w.Str("id") : null, w.Ok ? w.Str("status") : Why(w));
            db.Execute("UPDATE onboardings SET status = 'AWAITING_SIGNATURE' WHERE id = @id", new { id });
            return Results.Created($"/api/v1/onboardings/{id}", Load(db, id));
        }).WithTags("Altas").WithSummary("Alta completa de un cliente (asesor): saga de 8 pasos con compensación").Produces<Onboarding>(201);

        app.MapGet("/api/v1/onboardings/{id}", (HttpContext http, string id) =>
        {
            var denied = Auth.Require(http, clock, out _, roles: new[] { "Advisor", "Operations", "Admin", "Compliance" });
            if (denied is not null) return denied;
            using var db = ctx.OpenDb();
            var o = Load(db, id);
            return o is null ? Problems.NotFound(http, "ONBOARDING_NOT_FOUND", $"No existe el alta {id}.", null) : Results.Ok(o);
        }).WithTags("Altas").WithSummary("Estado de un alta y de cada paso").Produces<Onboarding>();

        EventConsumer.Start(app, ctx, new[] { "contract.activated", "contract.expired" }, ev =>
        {
            using var db = ctx.OpenDb();
            var id = db.ExecuteScalar<string?>("SELECT id FROM onboardings WHERE contract_id = @c AND status = 'AWAITING_SIGNATURE'", new { c = ev.Key });
            if (id is null) return Task.CompletedTask;
            var ok = ev.Topic == "contract.activated";
            using var tx = db.BeginTransaction();
            db.Execute("UPDATE onboardings SET status = @s, failure = @f, finished_at = @at WHERE id = @id",
                new { s = ok ? "COMPLETED" : "FAILED", f = ok ? null : "El cliente no firmó el contrato en 14 días.", at = Nordic.Iso(clock.LocalNow), id }, tx);
            Outbox.Add(db, null, clock, ok ? "onboarding.completed" : "onboarding.failed", id, new { onboardingId = id, contractId = ev.Key }, tx, correlationId: ev.CorrelationId);
            tx.Commit();
            return Task.CompletedTask;
        });
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record LineRequest(string? Account, string? Debit, string? Credit);
public sealed record EntryRequest(string? Reference, string? Description, string? ValueDate, List<LineRequest>? Lines);
public sealed record ReverseRequest(string? Reason);
public sealed record EntryLine(string Account, string Debit, string Credit);
public sealed record JournalEntry(string Id, string Reference, string? Description, string ValueDate, string PostedAt, string? ReversalOf, string? ReversedBy, IReadOnlyList<EntryLine> Lines, string TotalDebit, string TotalCredit);
public sealed record AccountBalance(string Account, string Type, string Debits, string Credits, string Balance, string AsOf, long Entries);
public sealed record TrialBalance(string AsOf, IReadOnlyList<AccountBalance> Accounts, string TotalDebits, string TotalCredits, bool Balanced);

/// <summary>
/// ledger-service: libro de contabilidad de doble entrada. Cada asiento tiene al menos dos líneas y el debe cuadra con el haber.
/// Los asientos no se modifican nunca: se corrigen con un asiento de reversión. La referencia es única (protege de duplicados).
/// Cuentas: customer:{id}:cash (pasivo), bank:client-money y custody:* (activo), fees:income (ingreso), suspense:* (pendiente de aplicar).
/// </summary>
public sealed class LedgerService : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "ledger-service", "K-payments", 7003, 2, "Pagos y contabilidad",
        "Contabilidad de doble entrada: asientos cuadrados, inmutables, con reversión, saldos por cuenta y balance de comprobación.",
        DependsOn: new[] { "clock-service" }, Emits: new[] { "ledger.entry.posted" });

    private static readonly Regex AccountFormat = new(@"^(customer:[0-9a-fA-F-]{36}:cash|bank:[a-z-]+|custody:[a-z-]+|fees:[a-z-]+|suspense:[a-z-]+)$");
    private static readonly object Gate = new();

    public static string TypeOf(string account) => account.Split(':')[0] switch
    {
        "customer" => "LIABILITY", "bank" or "custody" => "ASSET", "fees" => "INCOME", _ => "SUSPENSE",
    };

    /// <summary>Saldo con el signo natural de la cuenta: activo = debe − haber; pasivo, ingreso y transitoria = haber − debe.</summary>
    public static decimal Natural(string account, decimal debits, decimal credits) => TypeOf(account) == "ASSET" ? debits - credits : credits - debits;

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        Outbox.Ensure(db);
        db.Execute(@"CREATE TABLE IF NOT EXISTS entries (id TEXT PRIMARY KEY, reference TEXT UNIQUE, description TEXT, value_date TEXT, posted_at TEXT, reversal_of TEXT, reversed_by TEXT, seq INTEGER);
                     CREATE TABLE IF NOT EXISTS lines (entry_id TEXT, line_no INTEGER, account TEXT, debit TEXT, credit TEXT, value_date TEXT, PRIMARY KEY (entry_id, line_no));
                     CREATE INDEX IF NOT EXISTS ix_lines_account ON lines(account)");
    }

    public static JournalEntry? Load(Microsoft.Data.Sqlite.SqliteConnection db, string id)
    {
        var e = db.QuerySingleOrDefault<(string Id, string Ref, string? Desc, string Vd, string Posted, string? RevOf, string? RevBy)>(
            "SELECT id, reference, description, value_date, posted_at, reversal_of, reversed_by FROM entries WHERE id = @id", new { id });
        if (e.Id is null) return null;
        var lines = db.Query<EntryLine>("SELECT account, debit, credit FROM lines WHERE entry_id = @id ORDER BY line_no", new { id }).ToList();
        return new JournalEntry(e.Id, e.Ref, e.Desc, e.Vd, e.Posted, e.RevOf, e.RevBy, lines,
            Money.Format(lines.Sum(l => D(l.Debit)), 2), Money.Format(lines.Sum(l => D(l.Credit)), 2));
    }

    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static AccountBalance Balance(Microsoft.Data.Sqlite.SqliteConnection db, string account, string asOf)
    {
        var rows = db.Query<(string Debit, string Credit)>("SELECT debit, credit FROM lines WHERE account = @account AND value_date <= @asOf", new { account, asOf }).ToList();
        decimal dr = rows.Sum(r => D(r.Debit)), cr = rows.Sum(r => D(r.Credit));
        return new AccountBalance(account, TypeOf(account), Money.Format(dr, 2), Money.Format(cr, 2), Money.Format(Natural(account, dr, cr), 2), asOf, rows.Count);
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        Outbox.MapFeed(app, ctx);

        app.MapPost("/api/v1/journal-entries", (HttpContext http, IClock clock, EntryRequest b) =>
        {
            var errors = new List<FieldError>();
            if (string.IsNullOrWhiteSpace(b.Reference) || b.Reference.Length > 80) errors.Add(new FieldError("reference", "obligatoria y única (máx. 80)"));
            if (b.ValueDate is not null && !Money.TryParseDate(b.ValueDate, out _)) errors.Add(new FieldError("valueDate", "AAAA-MM-DD"));
            if (b.Lines is not { Count: >= 2 }) errors.Add(new FieldError("lines", "al menos dos líneas (doble entrada)"));
            var lines = new List<(string Account, decimal Dr, decimal Cr)>();
            for (var i = 0; i < (b.Lines?.Count ?? 0); i++)
            {
                var l = b.Lines![i];
                if (l.Account is null || !AccountFormat.IsMatch(l.Account)) errors.Add(new FieldError($"lines[{i}].account", "customer:<uuid>:cash, bank:*, custody:*, fees:* o suspense:*"));
                var hasDr = Money.TryParse(l.Debit, out var dr); var hasCr = Money.TryParse(l.Credit, out var cr);
                if (hasDr == hasCr || (hasDr && (dr <= 0 || decimal.Round(dr, 2) != dr)) || (hasCr && (cr <= 0 || decimal.Round(cr, 2) != cr)))
                    errors.Add(new FieldError($"lines[{i}]", "exactamente uno de debit o credit, positivo y con 2 decimales como mucho"));
                lines.Add((l.Account ?? "", hasDr ? dr : 0, hasCr ? cr : 0));
            }
            if (errors.Count > 0) return Problems.BadRequest(http, "VALIDATION_FAILED", "Asiento no válido.",
                "Ejemplo: { \"reference\": \"DEP-1\", \"lines\": [ { \"account\": \"bank:client-money\", \"debit\": \"100.00\" }, { \"account\": \"customer:<id>:cash\", \"credit\": \"100.00\" } ] }", errors);
            var totalDr = lines.Sum(l => l.Dr); var totalCr = lines.Sum(l => l.Cr);
            if (totalDr != totalCr)
                return Problems.Unprocessable(http, "UNBALANCED_ENTRY", $"El debe ({Money.Format(totalDr, 2)}) no cuadra con el haber ({Money.Format(totalCr, 2)}).",
                    "En doble entrada la suma del debe y la del haber deben ser exactamente iguales.");
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM entries WHERE reference = @Reference", b) > 0)
                {
                    var existing = db.ExecuteScalar<string>("SELECT id FROM entries WHERE reference = @Reference", b);
                    return Problems.Conflict(http, "DUPLICATE_REFERENCE", $"Ya existe el asiento {existing} con la referencia {b.Reference}.", "La referencia evita contabilizar dos veces lo mismo.");
                }
                var id = Post(db, http, clock, b.Reference!, b.Description, b.ValueDate ?? Money.Date(clock.Today), lines, null);
                return Results.Created($"/api/v1/journal-entries/{id}", Load(db, id));
            }
        }).WithTags("Asientos").WithSummary("Contabilizar un asiento (debe = haber)").Produces<JournalEntry>(201);

        app.MapGet("/api/v1/journal-entries/{id}", (HttpContext http, string id) =>
        {
            using var db = ctx.OpenDb();
            var e = Load(db, id);
            return e is null ? Problems.NotFound(http, "ENTRY_NOT_FOUND", $"No existe el asiento {id}.", null) : Results.Ok(e);
        }).WithTags("Asientos").WithSummary("Un asiento").Produces<JournalEntry>();

        app.MapGet("/api/v1/journal-entries", (HttpContext http, string? reference, string? date, string? account, int? limit) =>
        {
            if (reference is null && date is null && account is null)
                return Problems.BadRequest(http, "FILTER_REQUIRED", "Filtra por reference, date o account.", "Ejemplo: ?date=2026-10-20");
            using var db = ctx.OpenDb();
            var ids = db.Query<string>(@"SELECT DISTINCT e.id FROM entries e JOIN lines l ON l.entry_id = e.id
                                         WHERE (@reference IS NULL OR e.reference = @reference) AND (@date IS NULL OR e.value_date = @date) AND (@account IS NULL OR l.account = @account)
                                         ORDER BY e.seq LIMIT @limit", new { reference, date, account, limit = Math.Clamp(limit ?? 500, 1, 5000) });
            return Results.Ok(ids.Select(i => Load(db, i)!).ToList());
        }).WithTags("Asientos").WithSummary("Buscar asientos").Produces<List<JournalEntry>>();

        app.MapPost("/api/v1/journal-entries/{id}/reverse", (HttpContext http, IClock clock, string id, ReverseRequest b) =>
        {
            lock (Gate)
            {
                using var db = ctx.OpenDb();
                var e = Load(db, id);
                if (e is null) return Problems.NotFound(http, "ENTRY_NOT_FOUND", $"No existe el asiento {id}.", null);
                if (e.ReversedBy is not null) return Problems.Conflict(http, "ALREADY_REVERSED", $"El asiento ya se revirtió con {e.ReversedBy}.", null);
                if (e.ReversalOf is not null) return Problems.Unprocessable(http, "CANNOT_REVERSE_A_REVERSAL", "No se revierte una reversión: contabiliza un asiento nuevo.", null);
                var lines = e.Lines.Select(l => (l.Account, D(l.Credit), D(l.Debit))).ToList();
                var rid = Post(db, http, clock, "REV-" + e.Reference, b.Reason ?? "Reversión de " + e.Id, Money.Date(clock.Today), lines, e.Id);
                db.Execute("UPDATE entries SET reversed_by = @rid WHERE id = @id", new { rid, id });
                return Results.Created($"/api/v1/journal-entries/{rid}", Load(db, rid));
            }
        }).WithTags("Asientos").WithSummary("Revertir un asiento (crea el asiento contrario; el original no cambia)").Produces<JournalEntry>(201);

        foreach (var m in new[] { "PUT", "PATCH", "DELETE" })
            app.MapMethods("/api/v1/journal-entries/{id}", new[] { m }, (HttpContext http, string id) =>
            {
                http.Response.Headers.Allow = "GET";
                return Problems.Of(http, 405, "ENTRY_IS_IMMUTABLE", "Método no permitido", "Un asiento contabilizado no se modifica ni se borra.", "Para corregirlo: POST /api/v1/journal-entries/{id}/reverse");
            }).WithTags("Asientos").WithSummary("No permitido: los asientos son inmutables");

        app.MapGet("/api/v1/accounts/{account}/balance", (HttpContext http, IClock clock, string account, string? asOf) =>
        {
            if (!AccountFormat.IsMatch(account)) return Problems.BadRequest(http, "INVALID_ACCOUNT", $"'{account}' no es una cuenta válida.", "Ejemplo: customer:<uuid>:cash o bank:client-money");
            using var db = ctx.OpenDb();
            return Results.Ok(Balance(db, account, asOf ?? Money.Date(clock.Today)));
        }).WithTags("Cuentas").WithSummary("Saldo de una cuenta a una fecha (signo natural de la cuenta)").Produces<AccountBalance>();

        app.MapGet("/api/v1/accounts/{account}/entries", (HttpContext http, string account) =>
        {
            using var db = ctx.OpenDb();
            var ids = db.Query<string>("SELECT DISTINCT e.id FROM entries e JOIN lines l ON l.entry_id = e.id WHERE l.account = @account ORDER BY e.seq", new { account });
            return Results.Ok(ids.Select(i => Load(db, i)!).ToList());
        }).WithTags("Cuentas").WithSummary("Asientos que tocan una cuenta").Produces<List<JournalEntry>>();

        app.MapGet("/api/v1/trial-balance", (IClock clock, string? asOf) =>
        {
            var d = asOf ?? Money.Date(clock.Today);
            using var db = ctx.OpenDb();
            var accounts = db.Query<string>("SELECT DISTINCT account FROM lines ORDER BY account").Select(a => Balance(db, a, d)).ToList();
            var dr = accounts.Sum(a => D(a.Debits)); var cr = accounts.Sum(a => D(a.Credits));
            return Results.Ok(new TrialBalance(d, accounts, Money.Format(dr, 2), Money.Format(cr, 2), dr == cr));
        }).WithTags("Cuentas").WithSummary("Balance de comprobación: total debe = total haber").Produces<TrialBalance>();
    }

    private static string Post(Microsoft.Data.Sqlite.SqliteConnection db, HttpContext http, IClock clock, string reference, string? description, string valueDate,
        List<(string Account, decimal Dr, decimal Cr)> lines, string? reversalOf)
    {
        var id = "je_" + Guid.NewGuid().ToString("N")[..16];
        var seq = (db.ExecuteScalar<long?>("SELECT MAX(seq) FROM entries") ?? 0) + 1;
        using var tx = db.BeginTransaction();
        db.Execute("INSERT INTO entries VALUES (@id, @reference, @description, @valueDate, @at, @reversalOf, NULL, @seq)",
            new { id, reference, description, valueDate, at = Nordic.Iso(clock.LocalNow), reversalOf, seq }, tx);
        for (var i = 0; i < lines.Count; i++)
            db.Execute("INSERT INTO lines VALUES (@id, @n, @a, @d, @c, @valueDate)",
                new { id, n = i + 1, a = lines[i].Account, d = Money.Format(lines[i].Dr, 2), c = Money.Format(lines[i].Cr, 2), valueDate }, tx);
        Outbox.Add(db, http, clock, "ledger.entry.posted", id, new { entryId = id, reference, valueDate, accounts = lines.Select(l => l.Account).Distinct().ToList(), reversalOf }, tx);
        tx.Commit();
        return id;
    }
}

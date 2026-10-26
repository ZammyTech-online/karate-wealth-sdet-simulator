using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Nordvik.Shared;

namespace Nordvik.Services;

public sealed record FundSummary(string Isin, string Name, string Category, string BaseCurrency, long Sri, string Status, string ManagementFee);
public sealed record Fee(string Type, string Percent);
public sealed record FundDetail(string Isin, string Name, string Category, string BaseCurrency, long Sri, string Status,
    string LaunchDate, string Manager, IReadOnlyList<Fee> Fees, string Description);
public sealed record FundPage(IReadOnlyList<FundSummary> Items, int Page, int PageSize, int Total, int TotalPages);
public sealed record FundFees(string Isin, IReadOnlyList<Fee> Fees);

internal sealed record FundRow(string Isin, string Name, string Category, string BaseCurrency, long Sri, string Status,
    string ManagementFee, string EntryFee, string ExitFee, string LaunchDate, string Manager);

/// <summary>product-catalog: el catálogo de fondos. Equipo de Inversiones, que usa snake_case (ADR-009).</summary>
public sealed class ProductCatalog : INordvikService
{
    public NordvikServiceInfo Info { get; } = new(
        "product-catalog", "I-market", 6801, 0, "Inversiones",
        "Catálogo de fondos de Nordvik: categoría, divisa, riesgo SRI (1-7), estado y comisiones.",
        Naming: JsonNaming.SnakeCase);

    private static readonly Regex IsinFormat = new("^[A-Z]{2}[A-Z0-9]{9}[0-9]$");
    private const string Select = "SELECT isin, name, category, base_currency AS BaseCurrency, sri, status, management_fee AS ManagementFee, entry_fee AS EntryFee, exit_fee AS ExitFee, launch_date AS LaunchDate, manager FROM funds";

    public void Initialize(ServiceContext ctx)
    {
        using var db = ctx.OpenDb();
        db.Execute(@"CREATE TABLE IF NOT EXISTS funds (isin TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL, base_currency TEXT NOT NULL,
            sri INTEGER NOT NULL, status TEXT NOT NULL, management_fee TEXT NOT NULL, entry_fee TEXT NOT NULL, exit_fee TEXT NOT NULL,
            launch_date TEXT NOT NULL, manager TEXT NOT NULL)");
        if (db.ExecuteScalar<long>("SELECT COUNT(*) FROM funds") > 0) return;
        using var tx = db.BeginTransaction();
        foreach (var f in FundSeed.All)
            db.Execute(@"INSERT INTO funds VALUES (@Isin, @Name, @Category, @Currency, @Sri, @Status, @ManagementFee, @EntryFee, @ExitFee, @LaunchDate, @Manager)", f, tx);
        tx.Commit();
    }

    public void MapEndpoints(WebApplication app, ServiceContext ctx)
    {
        var g = app.MapGroup("/api/v1/funds").WithTags("Fondos");

        g.MapGet("", (HttpContext http, string? category, string? currency, int? min_sri, int? max_sri, string? status,
            string? sort, string? order, int? page, int? page_size) =>
        {
            var errors = new List<FieldError>();
            if (category is not null && !FundSeed.Categories.Contains(category))
                errors.Add(new FieldError("category", "valores válidos: " + string.Join(", ", FundSeed.Categories)));
            var sortKey = sort ?? "name";
            if (sortKey is not ("name" or "fee" or "sri" or "launch_date")) errors.Add(new FieldError("sort", "valores válidos: name, fee, sri, launch_date"));
            var ord = order ?? "asc";
            if (ord is not ("asc" or "desc")) errors.Add(new FieldError("order", "asc o desc"));
            var p = page ?? 1; var size = page_size ?? 20;
            if (p < 1) errors.Add(new FieldError("page", "empieza en 1"));
            if (size is < 1 or > 100) errors.Add(new FieldError("page_size", "entre 1 y 100"));
            if (errors.Count > 0)
                return Problems.BadRequest(http, "INVALID_QUERY", "Algún parámetro de búsqueda no es válido.",
                    "Este equipo usa snake_case también en los parámetros: page_size, min_sri. Las categorías van en mayúsculas.", errors);

            using var db = ctx.OpenDb();
            IEnumerable<FundRow> rows = db.Query<FundRow>(Select);
            if (category is not null) rows = rows.Where(r => r.Category == category);
            if (currency is not null) rows = rows.Where(r => r.BaseCurrency.Equals(currency, StringComparison.OrdinalIgnoreCase));
            if (min_sri is not null) rows = rows.Where(r => r.Sri >= min_sri);
            if (max_sri is not null) rows = rows.Where(r => r.Sri <= max_sri);
            if (status is not null) rows = rows.Where(r => r.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            Func<FundRow, object> key = sortKey switch
            {
                "fee" => r => decimal.Parse(r.ManagementFee, Money.Inv),
                "sri" => r => r.Sri,
                "launch_date" => r => r.LaunchDate,
                _ => r => r.Name,
            };
            var list = (ord == "desc" ? rows.OrderByDescending(key) : rows.OrderBy(key)).ToList();
            var total = list.Count;
            var items = list.Skip((p - 1) * size).Take(size)
                .Select(r => new FundSummary(r.Isin, r.Name, r.Category, r.BaseCurrency, r.Sri, r.Status, r.ManagementFee)).ToList();
            return Results.Ok(new FundPage(items, p, size, total, (int)Math.Ceiling(total / (double)size)));
        }).WithSummary("Buscar fondos con filtros, orden y paginación").Produces<FundPage>();

        g.MapGet("/{isin}", (HttpContext http, string isin) =>
        {
            var row = Find(http, ctx, isin, out var problem);
            if (row is null) return problem!;
            return Results.Ok(new FundDetail(row.Isin, row.Name, row.Category, row.BaseCurrency, row.Sri, row.Status, row.LaunchDate, row.Manager,
                Fees(row), $"{row.Name} es un fondo de categoría {row.Category} en {row.BaseCurrency} con riesgo SRI {row.Sri} sobre 7."));
        }).WithSummary("Detalle de un fondo").Produces<FundDetail>();

        g.MapGet("/{isin}/fees", (HttpContext http, string isin) =>
        {
            var row = Find(http, ctx, isin, out var problem);
            return row is null ? problem! : Results.Ok(new FundFees(row.Isin, Fees(row)));
        }).WithSummary("Comisiones de un fondo").Produces<FundFees>();
    }

    private static IReadOnlyList<Fee> Fees(FundRow r) => new[]
    {
        new Fee("MANAGEMENT", r.ManagementFee), new Fee("ENTRY", r.EntryFee), new Fee("EXIT", r.ExitFee),
    };

    private static FundRow? Find(HttpContext http, ServiceContext ctx, string isin, out IResult? problem)
    {
        problem = null;
        if (!IsinFormat.IsMatch(isin))
        {
            problem = Problems.BadRequest(http, "INVALID_ISIN", $"'{isin}' no tiene formato de ISIN.",
                "Un ISIN tiene 12 caracteres: 2 letras de país, 9 alfanuméricos y 1 dígito de control. Ejemplo: SE0000000001. ¿Está en minúsculas?");
            return null;
        }
        using var db = ctx.OpenDb();
        var row = db.QuerySingleOrDefault<FundRow>(Select + " WHERE isin = @isin", new { isin });
        if (row is null)
            problem = Problems.NotFound(http, "FUND_NOT_FOUND", $"No existe ningún fondo con ISIN {isin}.",
                "El formato es correcto pero el fondo no está en el catálogo. Lista los existentes con GET /api/v1/funds.");
        return row;
    }
}

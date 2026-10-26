using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Nordvik.Shared;

/// <summary>
/// Construye una aplicación web completa para un microservicio: su propio puerto, su JSON, su Swagger,
/// /health, /info, errores comunes y log. Nordvik.Host llama a esto una vez por servicio del perfil (ADR-001).
/// </summary>
public static class ServiceRunner
{
    public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static WebApplication Build(INordvikService svc, HostSettings settings)
    {
        // Columnas snake_case de SQLite (customer_id) → propiedades CustomerId sin tener que poner alias en cada consulta.
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        var info = svc.Info;
        var ctx = new ServiceContext
        {
            Info = info,
            DataDir = settings.DataDir,
            LogDir = settings.LogDir,
            LearningMode = settings.LearningMode,
            ClockUrl = settings.ClockUrl,
            ProfileName = settings.ProfileName,
            PortOffset = settings.PortOffset,
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = info.Name,
            ContentRootPath = AppContext.BaseDirectory,
            Args = Array.Empty<string>(),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(ctx.ActualPort));

        var naming = info.Naming == JsonNaming.SnakeCase ? JsonNamingPolicy.SnakeCaseLower : JsonNamingPolicy.CamelCase;
        builder.Services.ConfigureHttpJsonOptions(o => Configure(o.SerializerOptions, naming));
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o => Configure(o.JsonSerializerOptions, naming));

        // Que los errores de lectura de la petición lleguen al middleware y salgan como RFC 9457.
        builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = info.Name,
                Version = info.Version,
                Description = $"{info.Description}\n\nDominio: {info.Domain} · Equipo: {info.Team} · Oleada: {info.Wave} · Puerto: {info.Port}" +
                              (info.DependsOn is { Length: > 0 } ? "\n\nDepende de: " + string.Join(", ", info.DependsOn) : ""),
            });
            c.CustomSchemaIds(t => t.Name);
        });

        builder.Services.AddSingleton(ctx);
        builder.Services.AddSingleton<IClock>(_ => new RemoteClock(settings.ClockUrl, settings.ClockCacheMs));
        svc.ConfigureServices(builder.Services, ctx);

        var app = builder.Build();
        app.UseMiddleware<NordvikMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI(c => c.DocumentTitle = info.Name + " · Nordvik");

        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        app.MapGet("/health", () => Results.Json(new Dictionary<string, object?>
        {
            ["status"] = "UP",
            ["service"] = info.Name,
            ["version"] = info.Version,
            ["uptimeSeconds"] = (long)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
        })).WithTags("Plataforma").WithSummary("Estado del servicio");
        app.MapGet("/info", () => Results.Json(new Dictionary<string, object?>
        {
            ["name"] = info.Name,
            ["domain"] = info.Domain,
            ["team"] = info.Team,
            ["description"] = info.Description,
            ["version"] = info.Version,
            ["wave"] = info.Wave,
            ["port"] = ctx.ActualPort,
            ["jsonNaming"] = info.Naming.ToString(),
            ["dependsOn"] = info.DependsOn ?? Array.Empty<string>(),
            ["optionalDependsOn"] = info.OptionalDependsOn ?? Array.Empty<string>(),
            ["emits"] = info.Emits ?? Array.Empty<string>(),
            ["consumes"] = info.Consumes ?? Array.Empty<string>(),
            ["profile"] = settings.ProfileName,
            ["learningMode"] = settings.LearningMode,
            ["openApi"] = $"http://localhost:{ctx.ActualPort}/swagger/v1/swagger.json",
        })).WithTags("Plataforma").WithSummary("Ficha del servicio");

        app.MapDelete("/_test/data", (HttpContext http, string? prefix) =>
        {
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 3)
                return Problems.BadRequest(http, "PREFIX_REQUIRED", "Indica un prefijo de al menos 3 caracteres.", "Ejemplo: DELETE /_test/data?prefix=RUN-20261026");
            return Results.Ok(new Dictionary<string, object?> { ["service"] = info.Name, ["prefix"] = prefix, ["deleted"] = svc.ResetByPrefix(ctx, prefix) });
        }).WithTags("Plataforma").WithSummary("Solo pruebas: borra los datos creados con un prefijo");

        svc.Initialize(ctx);
        svc.MapEndpoints(app, ctx);

        app.MapFallback((HttpContext http) => Problems.NotFound(http, "ROUTE_NOT_FOUND",
            $"La ruta {http.Request.Method} {http.Request.Path} no existe en {info.Name}.",
            $"Revisa la ruta exacta en http://localhost:{ctx.ActualPort}/swagger. ¿Falta el prefijo /api/v1? ¿Te has equivocado de servicio o de puerto?"))
            .ExcludeFromDescription();

        return app;
    }

    private static void Configure(JsonSerializerOptions o, JsonNamingPolicy naming)
    {
        o.PropertyNamingPolicy = naming;
        o.DictionaryKeyPolicy = null;
        o.Converters.Add(new JsonStringEnumConverter());
        o.WriteIndented = false;
    }
}

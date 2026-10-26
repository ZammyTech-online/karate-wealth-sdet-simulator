using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Nordvik.Shared;

/// <summary>Convención de nombres JSON del equipo dueño del servicio.</summary>
public enum JsonNaming { CamelCase, SnakeCase }

/// <summary>Ficha de identidad de un microservicio: nombre, dominio, puerto, oleada y equipo.</summary>
public sealed record NordvikServiceInfo(
    string Name,
    string Domain,
    int Port,
    int Wave,
    string Team,
    string Description,
    string Version = "1.0.0",
    JsonNaming Naming = JsonNaming.CamelCase,
    string[]? DependsOn = null);

/// <summary>Contrato que implementa cada microservicio para poder vivir dentro de Nordvik.Host.</summary>
public interface INordvikService
{
    NordvikServiceInfo Info { get; }

    /// <summary>Registra dependencias propias del servicio (opcional).</summary>
    void ConfigureServices(IServiceCollection services, ServiceContext ctx) { }

    /// <summary>Crea tablas y datos semilla. Se ejecuta antes de abrir el puerto.</summary>
    void Initialize(ServiceContext ctx) { }

    /// <summary>Declara los endpoints HTTP del servicio.</summary>
    void MapEndpoints(WebApplication app, ServiceContext ctx);
}

/// <summary>Todo lo que un servicio necesita saber de su entorno.</summary>
public sealed class ServiceContext
{
    public required NordvikServiceInfo Info { get; init; }
    public required string DataDir { get; init; }
    public required string LogDir { get; init; }
    public required bool LearningMode { get; init; }
    public required string ClockUrl { get; init; }
    public required string ProfileName { get; init; }
    public int PortOffset { get; init; }
    public int ActualPort => Info.Port + PortOffset;

    public string DbPath => Path.Combine(DataDir, Info.Name + ".db");
    public string ConnectionString => $"Data Source={DbPath};Cache=Shared";

    public SqliteConnection OpenDb()
    {
        var c = new SqliteConnection(ConnectionString);
        c.Open();
        return c;
    }
}

/// <summary>Ajustes del anfitrión que se pasan a cada servicio.</summary>
/// <param name="PortOffset">Se suma al puerto de cada servicio (las pruebas usan 10000 para no chocar con tu entorno).</param>
/// <param name="ClockCacheMs">Cada cuánto preguntan los servicios la hora a clock-service.</param>
public sealed record HostSettings(string Root, string DataDir, string LogDir, bool LearningMode, string ClockUrl, string ProfileName,
    int PortOffset = 0, int ClockCacheMs = 1000);

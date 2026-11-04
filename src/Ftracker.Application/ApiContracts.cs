using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ftracker.Application;

public static class ApiInfo
{
    /// <summary>Sube cuando cambia el contrato de la API de forma incompatible. El cliente la comprueba al conectar.</summary>
    public const string Version = "1";
}

public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PropertyNameCaseInsensitive = true;
        if (!o.Converters.OfType<JsonStringEnumConverter>().Any()) o.Converters.Add(new JsonStringEnumConverter());
    }
    private static JsonSerializerOptions Create() { var o = new JsonSerializerOptions(); Configure(o); return o; }
}

public sealed record ApiError(string Error);
public sealed record SaludDto(string App, string Version);
public sealed record ConfiguracionInicialDto(bool Requerida);
public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, UsuarioSesionDto Usuario);
public sealed record SetupRequest(string Nombre, string Email, string Password);
public sealed record NuevoUsuarioRequest(string Nombre, string Email, string Password, string Rol);
public sealed record EditarUsuarioRequest(string Nombre, string Email, string Rol);
public sealed record CambiarClaveRequest(string? Actual, string Nueva);
public sealed record NuevaRutaRequest(string Nombre, string Descripcion, string Puntos);
public sealed record NombreRequest(string Nombre);
public sealed record MoverPuntoRequest(int Direccion);
public sealed record CodigosRutaRequest(bool Activar, bool Regenerar);
public sealed record CerrarRondinRequest(string? Observaciones, FotoCierreCommand Fotografia);
public sealed record CancelarRondinRequest(string Motivo);

using Ftracker.Domain;

namespace Ftracker.Application;

public enum ReportKind { Rondines, Puntos, Incidencias }

// DTOs: lo que viaja entre la aplicación, la API y la base. Las fechas siempre van en UTC.
public sealed record PuntoCatalogo(Guid Id, int Orden, string Nombre, bool ConCodigo);
public sealed record UsuarioDto(string Id, string Nombre, string Correo, string Rol, bool Activo);
public sealed record UsuarioSesionDto(string Id, string Nombre, string Correo, string Rol);
public sealed record RutaDto(Guid Id, string Nombre, int Puntos, bool Activa);
public sealed record RondinDto(Guid Id, string Ruta, string Prefecto, DateTime ProgramadoPara, RondinEstado Estado, DateTime? CerradoEn, string PrefectoId, bool TieneHojaFirmas);
public sealed record IncidenciaPuntoDto(Guid Id, string Detalle);
public sealed record RegistroPuntoDto(Guid Id, Guid PuntoControlId, int Orden, string Nombre, RegistroResultado Resultado,
    string? Observaciones, bool Verificado, bool RequiereCodigo, IReadOnlyList<IncidenciaPuntoDto> IncidenciasConEvidencia);
public sealed record IncidenciaDto(Guid Id, string Ruta, string Tipo, string Descripcion, IncidenciaPrioridad Prioridad,
    IncidenciaEstado Estado, int Archivos, Guid RondinId);
public sealed record AuditoriaDto(Guid Id, string ActorId, string Accion, string Entidad, string EntidadId, DateTime Fecha, string? Detalle);
public sealed record EvidenciaDto(Guid Id, string NombreOriginal, string TipoMime, DateTime SubidaEn);
public sealed record ArchivoGenerado(byte[] Contenido, int Filas, string NombreSugerido);

/// <summary>
/// Todo lo que la interfaz necesita. Lo implementan la base local (<c>LocalStore</c>) y el cliente remoto (<c>RemoteStore</c>),
/// de modo que la aplicación funcione igual con datos locales o contra el servidor de prefectura.
/// Las rutas de archivo (path, zipPath) son siempre de la PC que ejecuta la aplicación.
/// </summary>
public interface IFtrackerStore
{
    ISession Session { get; }
    void SignOut();
    Task<UsuarioSesionDto?> GetCurrentUserAsync();

    Task<bool> NeedsSetupAsync();
    Task SetupAsync(string name, string email, string password);
    Task LoginAsync(string email, string password);
    Task ChangeOwnPasswordAsync(string current, string next);

    Task<IReadOnlyList<UsuarioDto>> GetUsersAsync();
    Task AddUserAsync(string name, string email, string password, string role);
    Task UpdateUserAsync(string id, string name, string email, string role);
    Task ToggleUserAsync(string id);
    Task DeleteUserAsync(string id);
    Task ResetPasswordAsync(string id, string next);

    Task<IReadOnlyList<RutaDto>> GetRoutesAsync();
    Task AddRouteAsync(string name, string description, string points);
    Task ToggleRouteAsync(Guid id);
    Task DeleteRouteAsync(Guid id);
    Task<IReadOnlyList<PuntoCatalogo>> GetRoutePointsAsync(Guid rutaId);
    Task AddPointAsync(Guid rutaId, string name);
    Task RenamePointAsync(Guid pointId, string name);
    Task MovePointAsync(Guid pointId, int direction);
    Task SetRouteCodesAsync(Guid rutaId, bool enabled, bool regenerate = false);
    Task<bool> RequiresCodeAsync(Guid registroId);
    Task<int> WriteCodeSheetAsync(Guid rutaId, string path);
    Task<IReadOnlyList<CroquisPunto>> GetRouteCroquisAsync(Guid rutaId);
    Task SaveRouteCroquisAsync(Guid rutaId, IReadOnlyList<CroquisPunto> points);
    Task<CroquisRecorrido> GetRoundCroquisAsync(Guid rondinId);

    Task<IReadOnlyList<RondinDto>> GetRoundsAsync();
    Task<IReadOnlyList<RegistroPuntoDto>> GetRoundPointsAsync(Guid rondinId);
    Task CreateRoundAsync(CrearRondinCommand command);
    Task StartRoundAsync(Guid rondinId);
    Task MarkPointAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command);
    /// <summary>Cierra el rondín con la fotografía de la hoja de firmas (obligatoria).</summary>
    Task CloseRoundAsync(Guid rondinId, string? observaciones, FotoCierreCommand fotografia);
    Task<FotoCierreInfo?> GetClosureEvidenceAsync(Guid roundId);
    Task<byte[]> ReadClosureEvidenceAsync(Guid roundId);
    Task CancelRoundAsync(Guid rondinId, string motivo);
    Task ReassignRoundAsync(Guid rondinId, ReasignarRondinCommand command);
    Task DeleteRoundAsync(Guid id);

    Task<IReadOnlyList<IncidenciaDto>> GetIncidentsAsync();
    Task<Guid> CreateIncidentAsync(CrearIncidenciaCommand command);
    Task ChangeIncidentStateAsync(Guid id, CambiarIncidenciaCommand command);
    Task<IReadOnlyList<EvidenciaDto>> ListEvidenceAsync(Guid incidentId);
    Task<byte[]> ReadEvidenceAsync(Guid evidenceId);
    Task SaveEvidenceAsync(Guid incidentId, string path);

    Task<IReadOnlyList<AuditoriaDto>> GetAuditAsync(int take = 200);
    Task<int> ExportReportAsync(ReportKind kind, string path, DateTime? desde, DateTime? hasta, string? prefectoId);
    Task CreateBackupAsync(string zipPath);
    Task RestoreBackupAsync(string zipPath);
}

/// <summary>Operaciones que solo necesita el servidor para mover archivos por HTTP (el cliente remoto no las implementa).</summary>
public interface IServerStore : IFtrackerStore
{
    Task SaveEvidenceBytesAsync(Guid incidentId, string fileName, byte[] content);
    Task<ArchivoGenerado> BuildReportAsync(ReportKind kind, DateTime? desde, DateTime? hasta, string? prefectoId);
    Task<ArchivoGenerado> BuildCodeSheetAsync(Guid rutaId);
    Task WriteBackupToAsync(Stream destination);
    Task RestoreBackupFromAsync(Stream source);
}

/// <summary>Crea un almacén por petición: cada solicitud HTTP trabaja con su propia sesión, nunca compartida.</summary>
public interface IStoreFactory
{
    IServerStore CreateAnonymous();
    /// <summary>Devuelve null si el usuario ya no existe o está desactivado (así un cambio de estado surte efecto de inmediato).</summary>
    Task<IServerStore?> CreateForUserAsync(string userId);
    Task RecordSecurityEventAsync(string action, string detail);
    /// <summary>Respaldo del servidor sin sesión de usuario (lo usa el respaldo programado).</summary>
    Task CreateBackupFileAsync(string zipPath);
}

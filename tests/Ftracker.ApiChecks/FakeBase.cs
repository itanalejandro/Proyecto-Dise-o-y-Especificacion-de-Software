// Generado: implementación base que lanza NotSupportedException; las pruebas sobreescriben solo lo necesario.
using Ftracker.Application;

namespace Ftracker.ApiChecks;

public abstract class FakeBase : IServerStore
{
    public abstract ISession Session { get; }
    public virtual void SignOut() => throw new NotSupportedException("SignOut");
    public virtual Task<UsuarioSesionDto?> GetCurrentUserAsync() => throw new NotSupportedException("GetCurrentUserAsync");
    public virtual Task<bool> NeedsSetupAsync() => throw new NotSupportedException("NeedsSetupAsync");
    public virtual Task SetupAsync(string name, string email, string password) => throw new NotSupportedException("SetupAsync");
    public virtual Task LoginAsync(string email, string password) => throw new NotSupportedException("LoginAsync");
    public virtual Task ChangeOwnPasswordAsync(string current, string next) => throw new NotSupportedException("ChangeOwnPasswordAsync");
    public virtual Task<IReadOnlyList<UsuarioDto>> GetUsersAsync() => throw new NotSupportedException("GetUsersAsync");
    public virtual Task AddUserAsync(string name, string email, string password, string role) => throw new NotSupportedException("AddUserAsync");
    public virtual Task UpdateUserAsync(string id, string name, string email, string role) => throw new NotSupportedException("UpdateUserAsync");
    public virtual Task ToggleUserAsync(string id) => throw new NotSupportedException("ToggleUserAsync");
    public virtual Task DeleteUserAsync(string id) => throw new NotSupportedException("DeleteUserAsync");
    public virtual Task ResetPasswordAsync(string id, string next) => throw new NotSupportedException("ResetPasswordAsync");
    public virtual Task<IReadOnlyList<RutaDto>> GetRoutesAsync() => throw new NotSupportedException("GetRoutesAsync");
    public virtual Task AddRouteAsync(string name, string description, string points) => throw new NotSupportedException("AddRouteAsync");
    public virtual Task ToggleRouteAsync(Guid id) => throw new NotSupportedException("ToggleRouteAsync");
    public virtual Task DeleteRouteAsync(Guid id) => throw new NotSupportedException("DeleteRouteAsync");
    public virtual Task<IReadOnlyList<PuntoCatalogo>> GetRoutePointsAsync(Guid rutaId) => throw new NotSupportedException("GetRoutePointsAsync");
    public virtual Task AddPointAsync(Guid rutaId, string name) => throw new NotSupportedException("AddPointAsync");
    public virtual Task RenamePointAsync(Guid pointId, string name) => throw new NotSupportedException("RenamePointAsync");
    public virtual Task MovePointAsync(Guid pointId, int direction) => throw new NotSupportedException("MovePointAsync");
    public virtual Task SetRouteCodesAsync(Guid rutaId, bool enabled, bool regenerate = false) => throw new NotSupportedException("SetRouteCodesAsync");
    public virtual Task<bool> RequiresCodeAsync(Guid registroId) => throw new NotSupportedException("RequiresCodeAsync");
    public virtual Task<int> WriteCodeSheetAsync(Guid rutaId, string path) => throw new NotSupportedException("WriteCodeSheetAsync");
    public virtual Task<IReadOnlyList<CroquisPunto>> GetRouteCroquisAsync(Guid rutaId) => throw new NotSupportedException("GetRouteCroquisAsync");
    public virtual Task SaveRouteCroquisAsync(Guid rutaId, IReadOnlyList<CroquisPunto> points) => throw new NotSupportedException("SaveRouteCroquisAsync");
    public virtual Task<CroquisRecorrido> GetRoundCroquisAsync(Guid rondinId) => throw new NotSupportedException("GetRoundCroquisAsync");
    public virtual Task<IReadOnlyList<RondinDto>> GetRoundsAsync() => throw new NotSupportedException("GetRoundsAsync");
    public virtual Task<IReadOnlyList<RegistroPuntoDto>> GetRoundPointsAsync(Guid rondinId) => throw new NotSupportedException("GetRoundPointsAsync");
    public virtual Task CreateRoundAsync(CrearRondinCommand command) => throw new NotSupportedException("CreateRoundAsync");
    public virtual Task StartRoundAsync(Guid rondinId) => throw new NotSupportedException("StartRoundAsync");
    public virtual Task MarkPointAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command) => throw new NotSupportedException("MarkPointAsync");
    public virtual Task CloseRoundAsync(Guid rondinId, string? observaciones, FotoCierreCommand fotografia) => throw new NotSupportedException("CloseRoundAsync");
    public virtual Task<FotoCierreInfo?> GetClosureEvidenceAsync(Guid roundId) => throw new NotSupportedException("GetClosureEvidenceAsync");
    public virtual Task<byte[]> ReadClosureEvidenceAsync(Guid roundId) => throw new NotSupportedException("ReadClosureEvidenceAsync");
    public virtual Task CancelRoundAsync(Guid rondinId, string motivo) => throw new NotSupportedException("CancelRoundAsync");
    public virtual Task ReassignRoundAsync(Guid rondinId, ReasignarRondinCommand command) => throw new NotSupportedException("ReassignRoundAsync");
    public virtual Task DeleteRoundAsync(Guid id) => throw new NotSupportedException("DeleteRoundAsync");
    public virtual Task<IReadOnlyList<IncidenciaDto>> GetIncidentsAsync() => throw new NotSupportedException("GetIncidentsAsync");
    public virtual Task<Guid> CreateIncidentAsync(CrearIncidenciaCommand command) => throw new NotSupportedException("CreateIncidentAsync");
    public virtual Task ChangeIncidentStateAsync(Guid id, CambiarIncidenciaCommand command) => throw new NotSupportedException("ChangeIncidentStateAsync");
    public virtual Task<IReadOnlyList<EvidenciaDto>> ListEvidenceAsync(Guid incidentId) => throw new NotSupportedException("ListEvidenceAsync");
    public virtual Task<byte[]> ReadEvidenceAsync(Guid evidenceId) => throw new NotSupportedException("ReadEvidenceAsync");
    public virtual Task SaveEvidenceAsync(Guid incidentId, string path) => throw new NotSupportedException("SaveEvidenceAsync");
    public virtual Task<IReadOnlyList<AuditoriaDto>> GetAuditAsync(int take = 200) => throw new NotSupportedException("GetAuditAsync");
    public virtual Task<int> ExportReportAsync(ReportKind kind, string path, DateTime? desde, DateTime? hasta, string? prefectoId) => throw new NotSupportedException("ExportReportAsync");
    public virtual Task CreateBackupAsync(string zipPath) => throw new NotSupportedException("CreateBackupAsync");
    public virtual Task RestoreBackupAsync(string zipPath) => throw new NotSupportedException("RestoreBackupAsync");
    public virtual Task SaveEvidenceBytesAsync(Guid incidentId, string fileName, byte[] content) => throw new NotSupportedException("SaveEvidenceBytesAsync");
    public virtual Task<ArchivoGenerado> BuildReportAsync(ReportKind kind, DateTime? desde, DateTime? hasta, string? prefectoId) => throw new NotSupportedException("BuildReportAsync");
    public virtual Task<ArchivoGenerado> BuildCodeSheetAsync(Guid rutaId) => throw new NotSupportedException("BuildCodeSheetAsync");
    public virtual Task WriteBackupToAsync(Stream destination) => throw new NotSupportedException("WriteBackupToAsync");
    public virtual Task RestoreBackupFromAsync(Stream source) => throw new NotSupportedException("RestoreBackupFromAsync");
    public virtual IServerStore CreateAnonymous() => throw new NotSupportedException("CreateAnonymous");
    public virtual Task<IServerStore?> CreateForUserAsync(string userId) => throw new NotSupportedException("CreateForUserAsync");
    public virtual Task RecordSecurityEventAsync(string action, string detail) => throw new NotSupportedException("RecordSecurityEventAsync");
    public virtual Task CreateBackupFileAsync(string zipPath) => throw new NotSupportedException("CreateBackupFileAsync");
}

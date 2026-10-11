using Ftracker.Domain;

namespace Ftracker.Application;

public sealed record CrearRondinCommand(Guid RutaId, string PrefectoId, DateTime ProgramadoPara, string? Observaciones);
public sealed record MarcarPuntoCommand(RegistroResultado Resultado, string? Observaciones);
public sealed record CrearIncidenciaCommand(Guid RondinId, Guid? PuntoControlId, string Tipo, IncidenciaPrioridad Prioridad, string Descripcion);
public sealed record CambiarIncidenciaCommand(IncidenciaEstado Estado, string? Comentario);
public sealed record ReasignarRondinCommand(string PrefectoId, string Motivo);
public sealed record DashboardDto(int Programados, int EnCurso, int CerradosHoy, int IncidenciasAbiertas);

public interface IRondinService
{
    Task<Rondin> CrearAsync(CrearRondinCommand command, string actorId, CancellationToken ct = default);
    Task<Rondin> IniciarAsync(Guid rondinId, string actorId, CancellationToken ct = default);
    Task<RegistroPunto> MarcarPuntoAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command, string actorId, CancellationToken ct = default);
    Task<Rondin> CerrarAsync(Guid rondinId, string? observaciones, string actorId, CancellationToken ct = default);
    Task<Rondin> CancelarAsync(Guid rondinId, string motivo, string actorId, CancellationToken ct = default);
    Task<Rondin> ReasignarAsync(Guid rondinId, ReasignarRondinCommand command, string actorId, CancellationToken ct = default);
    Task<IReadOnlyList<Rondin>> MisRondinesAsync(string prefectoId, CancellationToken ct = default);
}

public interface IIncidenciaService
{
    Task<Incidencia> CrearAsync(CrearIncidenciaCommand command, string actorId, CancellationToken ct = default);
    Task<Incidencia> CambiarEstadoAsync(Guid id, CambiarIncidenciaCommand command, string actorId, CancellationToken ct = default);
}

public interface IDashboardService { Task<DashboardDto> ObtenerAsync(CancellationToken ct = default); }
public interface IEvidenciaStorage { Task<Evidencia> GuardarAsync(Guid incidenciaId, string fileName, string contentType, Stream content, long length, string actorId, CancellationToken ct = default); }
public sealed class ReglaNegocioException(string message) : Exception(message);

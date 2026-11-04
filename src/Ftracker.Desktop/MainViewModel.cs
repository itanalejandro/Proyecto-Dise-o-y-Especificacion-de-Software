using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Desktop;
public sealed record UserRow(string Id, string Nombre, string Correo, string Rol, bool Activo);
public sealed record RouteRow(Guid Id, string Nombre, int Puntos, bool Activa);
public sealed record RoundRow(Guid Id, string Ruta, string Prefecto, DateTime Programado, RondinEstado Estado, DateTime? CerradoEn = null, bool TieneHojaFirmas = false, string PrefectoId = "")
{
    public string CerradoEl => CerradoEn?.ToString("dd/MM/yyyy HH:mm") ?? "—";
    public string HojaFirmas => TieneHojaFirmas ? "Adjunta" : Estado == RondinEstado.Cerrado ? "Sin evidencia anterior" : "—";
    /// <summary>Un rondín programado que pasó más de 15 minutos de su hora sin iniciarse.</summary>
    public string Alerta => Estado == RondinEstado.Programado && Programado < DateTime.Now.AddMinutes(-15) ? "Atrasado" : "";
}
public sealed record PointRow(Guid Id, Guid PuntoId, string Nombre, RegistroResultado Resultado, string? Observaciones, bool Verificado = false)
{
    public string Verificacion => Verificado ? "Verificado" : "—";
    public IReadOnlyList<PointIncident> IncidenciasConEvidencia { get; init; } = [];
    public bool TieneEvidencia => IncidenciasConEvidencia.Count > 0;
}
public sealed record PointIncident(Guid Id, string Detalle);
public sealed record IncidentRow(Guid Id, string Ruta, string Tipo, string Descripcion, IncidenciaPrioridad Prioridad, IncidenciaEstado Estado, int Archivos, Guid RondinId);
public sealed record AuditRow(Guid Id, string ActorId, DateTime Fecha, string Accion, string Entidad, string EntidadId, string? Detalle, string Usuario);

public sealed class MainViewModel(IFtrackerStore store) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<RouteRow> Routes { get; private set; } = [];
    public IReadOnlyList<RoundRow> Rounds { get; private set; } = [];
    public IReadOnlyList<IncidentRow> Incidents { get; private set; } = [];
    public IReadOnlyList<AuditRow> Audit { get; private set; } = [];
    public string Summary { get; private set; } = "";
    public bool IsAdmin => store.Session.Role == Roles.Administrador;
    public int Programados => Rounds.Count(x => x.Estado == RondinEstado.Programado);
    public int EnCurso => Rounds.Count(x => x.Estado == RondinEstado.EnCurso);
    public int Cerrados => Rounds.Count(x => x.Estado == RondinEstado.Cerrado);
    public int Abiertas => Incidents.Count(x => x.Estado == IncidenciaEstado.Abierta || x.Estado == IncidenciaEstado.EnRevision);
    public async Task RefreshAsync()
    {
        // Las cinco consultas son independientes: en modo servidor se piden a la vez.
        var users = store.GetUsersAsync(); var routes = store.GetRoutesAsync(); var rounds = store.GetRoundsAsync(); var incidents = store.GetIncidentsAsync(); var audit = store.GetAuditAsync(200);
        await Task.WhenAll(users, routes, rounds, incidents, audit);
        Users = users.Result.Select(x => new UserRow(x.Id, x.Nombre, x.Correo, x.Rol, x.Activo)).ToList();
        Routes = routes.Result.Select(x => new RouteRow(x.Id, x.Nombre, x.Puntos, x.Activa)).ToList();
        Rounds = rounds.Result.Select(x => new RoundRow(x.Id, x.Ruta, x.Prefecto, x.ProgramadoPara.ToLocalTime(), x.Estado, x.CerradoEn?.ToLocalTime(), x.TieneHojaFirmas, x.PrefectoId)).ToList();
        Incidents = incidents.Result.Select(x => new IncidentRow(x.Id, x.Ruta, x.Tipo, x.Descripcion, x.Prioridad, x.Estado, x.Archivos, x.RondinId)).ToList();
        // Los instantes de la bitácora son UTC: solo el modelo de presentación se pasa a hora local, nunca el registro.
        var actors = Users.ToDictionary(x => x.Id, x => $"{x.Nombre} · {x.Rol}");
        Audit = audit.Result.Select(x => new AuditRow(x.Id, x.ActorId, DateTime.SpecifyKind(x.Fecha, DateTimeKind.Utc).ToLocalTime(), x.Accion, x.Entidad, x.EntidadId, x.Detalle,
            actors.GetValueOrDefault(x.ActorId, "Usuario no disponible"))).ToList();
        Summary = $"{Rounds.Count(x => x.Estado == RondinEstado.Programado)} programados     ·     {Rounds.Count(x => x.Estado == RondinEstado.EnCurso)} en curso     ·     {Rounds.Count(x => x.Estado == RondinEstado.Cerrado)} cerrados     ·     {Incidents.Count(x => x.Estado == IncidenciaEstado.Abierta || x.Estado == IncidenciaEstado.EnRevision)} incidencias abiertas";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Desktop;
public sealed record UserRow(string Id, string Nombre, string Correo, string Rol, bool Activo);
public sealed record RouteRow(Guid Id, string Nombre, int Puntos, bool Activa);
public sealed record RoundRow(Guid Id, string Ruta, string Prefecto, DateTime Programado, RondinEstado Estado, DateTime? CerradoEn = null, bool TieneHojaFirmas = false)
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

public sealed class MainViewModel(LocalStore store) : INotifyPropertyChanged
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
        await using var db = store.Open();
        Users = IsAdmin ? await db.Users.AsNoTracking().OrderBy(x => x.NombreCompleto).Select(x => new UserRow(x.Id, x.NombreCompleto, x.Email, x.Rol, x.Activo)).ToListAsync() : [];
        Routes = IsAdmin ? await db.Rutas.AsNoTracking().OrderBy(x => x.Nombre).Select(x => new RouteRow(x.Id, x.Nombre, x.Puntos.Count, x.Activa)).ToListAsync() : [];
        var rounds = db.Rondines.AsNoTracking().Include(x => x.Ruta).AsQueryable();
        if (!IsAdmin) rounds = rounds.Where(x => x.PrefectoId == store.Session.UserId);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NombreCompleto);
        var loaded = await rounds.OrderByDescending(x => x.ProgramadoPara)
            .Select(x => new { Round = x, HasPhoto = x.EvidenciaCierre != null }).ToListAsync();
        Rounds = loaded.Select(x => new RoundRow(x.Round.Id, x.Round.Ruta.Nombre, users.GetValueOrDefault(x.Round.PrefectoId, "Usuario"), x.Round.ProgramadoPara.ToLocalTime(), x.Round.Estado, x.Round.CerradoEn?.ToLocalTime(), x.HasPhoto)).ToList();
        var incidents = db.Incidencias.AsNoTracking().Include(x => x.Rondin).ThenInclude(x => x.Ruta).Include(x => x.Evidencias).AsQueryable();
        if (!IsAdmin) incidents = incidents.Where(x => x.Rondin.PrefectoId == store.Session.UserId);
        Incidents = (await incidents.OrderByDescending(x => x.CreadoEn).ToListAsync()).Select(x => new IncidentRow(x.Id, x.Rondin.Ruta.Nombre, x.Tipo, x.Descripcion, x.Prioridad, x.Estado, x.Evidencias.Count, x.RondinId)).ToList();
        // SQLite returns unspecified DateTime values. Audit instants are stored in UTC;
        // convert only the display model, never the persisted audit record.
        var actors = Users.ToDictionary(x => x.Id, x => $"{x.Nombre} · {x.Rol}");
        Audit = IsAdmin ? (await db.Auditorias.AsNoTracking().OrderByDescending(x => x.Fecha).Take(200).ToListAsync())
            .Select(x => new AuditRow(x.Id, x.ActorId, DateTime.SpecifyKind(x.Fecha, DateTimeKind.Utc).ToLocalTime(), x.Accion, x.Entidad, x.EntidadId, x.Detalle,
                actors.GetValueOrDefault(x.ActorId, "Usuario no disponible"))).ToList() : [];
        Summary = $"{Rounds.Count(x => x.Estado == RondinEstado.Programado)} programados     ·     {Rounds.Count(x => x.Estado == RondinEstado.EnCurso)} en curso     ·     {Rounds.Count(x => x.Estado == RondinEstado.Cerrado)} cerrados     ·     {Incidents.Count(x => x.Estado == IncidenciaEstado.Abierta || x.Estado == IncidenciaEstado.EnRevision)} incidencias abiertas";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

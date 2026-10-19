using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Desktop;
public sealed record UserRow(string Id, string Nombre, string Correo, string Rol, bool Activo);
public sealed record RouteRow(Guid Id, string Nombre, int Puntos, bool Activa);
public sealed record RoundRow(Guid Id, string Ruta, string Prefecto, DateTime Programado, RondinEstado Estado, DateTime? CerradoEn = null)
{
    public string CerradoEl => CerradoEn?.ToString("dd/MM/yyyy HH:mm") ?? "—";
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

public sealed class MainViewModel(LocalStore store) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<UserRow> Users { get; private set; } = [];
    public IReadOnlyList<RouteRow> Routes { get; private set; } = [];
    public IReadOnlyList<RoundRow> Rounds { get; private set; } = [];
    public IReadOnlyList<IncidentRow> Incidents { get; private set; } = [];
    public IReadOnlyList<Auditoria> Audit { get; private set; } = [];
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
        Rounds = (await rounds.OrderByDescending(x => x.ProgramadoPara).ToListAsync()).Select(x => new RoundRow(x.Id, x.Ruta.Nombre, users.GetValueOrDefault(x.PrefectoId, "Usuario"), x.ProgramadoPara.ToLocalTime(), x.Estado, x.CerradoEn?.ToLocalTime())).ToList();
        var incidents = db.Incidencias.AsNoTracking().Include(x => x.Rondin).ThenInclude(x => x.Ruta).Include(x => x.Evidencias).AsQueryable();
        if (!IsAdmin) incidents = incidents.Where(x => x.Rondin.PrefectoId == store.Session.UserId);
        Incidents = (await incidents.OrderByDescending(x => x.CreadoEn).ToListAsync()).Select(x => new IncidentRow(x.Id, x.Rondin.Ruta.Nombre, x.Tipo, x.Descripcion, x.Prioridad, x.Estado, x.Evidencias.Count, x.RondinId)).ToList();
        Audit = IsAdmin ? await db.Auditorias.AsNoTracking().OrderByDescending(x => x.Fecha).Take(200).ToListAsync() : [];
        Summary = $"{Rounds.Count(x => x.Estado == RondinEstado.Programado)} programados     ·     {Rounds.Count(x => x.Estado == RondinEstado.EnCurso)} en curso     ·     {Rounds.Count(x => x.Estado == RondinEstado.Cerrado)} cerrados     ·     {Incidents.Count(x => x.Estado == IncidenciaEstado.Abierta || x.Estado == IncidenciaEstado.EnRevision)} incidencias abiertas";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

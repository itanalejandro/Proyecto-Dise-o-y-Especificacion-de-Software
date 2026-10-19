using Ftracker.Application;
using Ftracker.Domain;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed class RondinService(FtrackerDbContext db, ISession session) : IRondinService
{
    public async Task<Rondin> CrearAsync(CrearRondinCommand c, string actorId, CancellationToken ct = default)
    {
        if (session.UserId != actorId || session.Role != Roles.Administrador) throw new ReglaNegocioException("Se requiere administrador.");
        if (!await db.Users.AnyAsync(x => x.Id == c.PrefectoId && x.Activo && x.Rol == Roles.Prefecto, ct)) throw new ReglaNegocioException("Selecciona un prefecto activo.");
        var ruta = await db.Rutas.Include(x => x.Puntos).SingleOrDefaultAsync(x => x.Id == c.RutaId, ct) ?? throw new ReglaNegocioException("La ruta no existe.");
        if (!ruta.Activa || !ruta.Puntos.Any(x => x.Activo)) throw new ReglaNegocioException("La ruta debe estar activa y tener puntos activos.");
        var rondin = new Rondin { RutaId = c.RutaId, PrefectoId = c.PrefectoId, ProgramadoPara = c.ProgramadoPara, Observaciones = c.Observaciones, CreadoPorId = actorId };
        db.Rondines.Add(rondin); await Auditar(actorId, "Crear", "Rondin", rondin.Id, null, ct); await db.SaveChangesAsync(ct); return rondin;
    }
    public async Task<Rondin> IniciarAsync(Guid id, string actorId, CancellationToken ct = default)
    {
        var r = await db.Rondines.Include(x => x.Ruta).ThenInclude(x => x.Puntos).Include(x => x.Registros).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaNegocioException("El rondín no existe.");
        ValidarAsignacion(r, actorId); if (r.Estado != RondinEstado.Programado) throw new ReglaNegocioException("Solo puede iniciarse un rondín programado.");
        if (!r.Ruta.Activa) throw new ReglaNegocioException("La ruta está desactivada.");
        var puntos = r.Ruta.Puntos.Where(x => x.Activo).OrderBy(x => x.Orden).ToList(); if (puntos.Count == 0) throw new ReglaNegocioException("La ruta no tiene puntos activos.");
        r.Estado = RondinEstado.EnCurso; r.IniciadoEn = DateTime.UtcNow; r.ActualizadoEn = DateTime.UtcNow;
        var ids = puntos.Select(x => x.Id).ToArray();
        var locations = await db.UbicacionesCroquis.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var p in puntos)
        {
            var registro = new RegistroPunto { RondinId = r.Id, PuntoControlId = p.Id, PuntoNombre = p.Nombre, Orden = p.Orden, CreadoPorId = actorId };
            db.RegistrosPunto.Add(registro);
            if (locations.TryGetValue(p.Id, out var location))
                db.UbicacionesCroquis.Add(new UbicacionCroquis { Id = registro.Id, X = location.X, Y = location.Y });
        }
        await Auditar(actorId, "Iniciar", "Rondin", r.Id, null, ct); await db.SaveChangesAsync(ct); return r;
    }
    public async Task<RegistroPunto> MarcarPuntoAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand c, string actorId, CancellationToken ct = default)
    {
        var r = await db.Rondines.Include(x => x.Registros).SingleOrDefaultAsync(x => x.Id == rondinId, ct) ?? throw new ReglaNegocioException("El rondín no existe.");
        ValidarAsignacion(r, actorId); if (r.Estado != RondinEstado.EnCurso) throw new ReglaNegocioException("El rondín no está en curso.");
        var registro = r.Registros.SingleOrDefault(x => x.Id == registroId) ?? throw new ReglaNegocioException("El punto no pertenece al rondín.");
        if (registro.Resultado != RegistroResultado.Pendiente) throw new ReglaNegocioException("El punto ya fue registrado.");
        if (!Enum.IsDefined(c.Resultado) || c.Resultado == RegistroResultado.Pendiente || (c.Resultado == RegistroResultado.Omitido && string.IsNullOrWhiteSpace(c.Observaciones))) throw new ReglaNegocioException("Resultado inválido. Un punto omitido requiere observación.");
        if (c.Resultado == RegistroResultado.Completado)
        {
            var esperado = await db.PuntosControl.AsNoTracking().Where(x => x.Id == registro.PuntoControlId).Select(x => x.CodigoVerificacion).SingleOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(esperado))
            {
                if (!CodigosIguales(esperado, c.Codigo))
                {
                    // Queda constancia del intento fallido; no se modifica el registro del punto.
                    await Auditar(actorId, "CodigoPuntoIncorrecto", "RegistroPunto", registro.Id, null, ct); await db.SaveChangesAsync(ct);
                    throw new ReglaNegocioException("El código no coincide con el del punto. Escanéalo o escríbelo de nuevo en el punto de control.");
                }
                registro.Verificado = true;
            }
        }
        registro.Resultado = c.Resultado; registro.Observaciones = c.Observaciones; registro.MarcadoEn = DateTime.UtcNow; registro.ActualizadoEn = DateTime.UtcNow;
        r.ActualizadoEn = DateTime.UtcNow; // Detect a concurrent cancellation before saving a result.
        await Auditar(actorId, "MarcarPunto", "RegistroPunto", registro.Id, c.Resultado.ToString(), ct); await db.SaveChangesAsync(ct); return registro;
    }
    public async Task<Rondin> CerrarAsync(Guid id, string? observaciones, string actorId, CancellationToken ct = default)
    {
        var r = await db.Rondines.Include(x => x.Registros).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaNegocioException("El rondín no existe.");
        ValidarAsignacion(r, actorId); if (r.Estado != RondinEstado.EnCurso) throw new ReglaNegocioException("Solo puede cerrarse un rondín en curso.");
        if (r.Registros.Any(x => x.Resultado == RegistroResultado.Pendiente)) throw new ReglaNegocioException("Todos los puntos deben registrarse antes de cerrar.");
        r.Estado = RondinEstado.Cerrado; r.CerradoEn = DateTime.UtcNow; r.Observaciones = observaciones ?? r.Observaciones; r.ActualizadoEn = DateTime.UtcNow;
        await Auditar(actorId, "Cerrar", "Rondin", r.Id, null, ct); await db.SaveChangesAsync(ct); return r;
    }
    public async Task<Rondin> CancelarAsync(Guid id, string motivo, string actorId, CancellationToken ct = default)
    {
        ValidarAdministrador(actorId); ValidarMotivo(motivo);
        var round = await db.Rondines.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ReglaNegocioException("El rondín no existe.");
        if (round.Estado is not (RondinEstado.Programado or RondinEstado.EnCurso))
            throw new ReglaNegocioException("Solo puede cancelarse un rondín programado o en curso.");
        var previous = round.Estado;
        round.Estado = RondinEstado.Cancelado; round.ActualizadoEn = DateTime.UtcNow;
        await Auditar(actorId, "Cancelar", "Rondin", id, $"Estado anterior: {previous}. Motivo: {motivo.Trim()}", ct);
        await db.SaveChangesAsync(ct); return round;
    }
    public async Task<Rondin> ReasignarAsync(Guid id, ReasignarRondinCommand command, string actorId, CancellationToken ct = default)
    {
        ValidarAdministrador(actorId); ValidarMotivo(command.Motivo);
        var round = await db.Rondines.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ReglaNegocioException("El rondín no existe.");
        if (round.Estado != RondinEstado.Programado) throw new ReglaNegocioException("Solo puede reasignarse un rondín que aún no ha iniciado.");
        if (round.PrefectoId == command.PrefectoId) throw new ReglaNegocioException("Selecciona un prefecto distinto al actual.");
        if (!await db.Users.AnyAsync(x => x.Id == command.PrefectoId && x.Activo && x.Rol == Roles.Prefecto, ct))
            throw new ReglaNegocioException("Selecciona un prefecto activo.");
        var previous = round.PrefectoId;
        round.PrefectoId = command.PrefectoId; round.ActualizadoEn = DateTime.UtcNow;
        await Auditar(actorId, "Reasignar", "Rondin", id, $"Prefecto anterior: {previous}. Nuevo prefecto: {command.PrefectoId}. Motivo: {command.Motivo.Trim()}", ct);
        await db.SaveChangesAsync(ct); return round;
    }
    internal static string NormalizarCodigo(string? codigo) => new string((codigo ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    private static bool CodigosIguales(string esperado, string? recibido) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(NormalizarCodigo(esperado)), Encoding.UTF8.GetBytes(NormalizarCodigo(recibido)));
    private void ValidarAdministrador(string actorId)
    {
        if (session.UserId != actorId || session.Role != Roles.Administrador) throw new ReglaNegocioException("Se requiere administrador.");
    }
    private static void ValidarMotivo(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 1000) throw new ReglaNegocioException("Escribe un motivo de 1 a 1000 caracteres.");
    }
    public async Task<IReadOnlyList<Rondin>> MisRondinesAsync(string prefectoId, CancellationToken ct = default)
    {
        if (session.UserId != prefectoId || session.Role != Roles.Prefecto) throw new ReglaNegocioException("Sesión de prefecto inválida.");
        return await db.Rondines.Include(x => x.Ruta).Include(x => x.Registros).Where(x => x.PrefectoId == prefectoId).OrderByDescending(x => x.ProgramadoPara).ToListAsync(ct);
    }
    private void ValidarAsignacion(Rondin r, string actorId) { if (session.UserId != actorId || session.Role != Roles.Prefecto || r.PrefectoId != actorId) throw new ReglaNegocioException("No tienes permiso para operar este rondín."); }
    private async Task Auditar(string actor, string accion, string entidad, Guid id, string? detalle, CancellationToken ct) => await db.Auditorias.AddAsync(new Auditoria { ActorId = actor, Accion = accion, Entidad = entidad, EntidadId = id.ToString(), Detalle = detalle }, ct);
}

public sealed class IncidenciaService(FtrackerDbContext db, ISession session) : IIncidenciaService
{
    public async Task<Incidencia> CrearAsync(CrearIncidenciaCommand c, string actor, CancellationToken ct = default)
    {
        if (session.UserId != actor || session.Role != Roles.Prefecto) throw new ReglaNegocioException("Se requiere prefecto.");
        if (string.IsNullOrWhiteSpace(c.Tipo) || string.IsNullOrWhiteSpace(c.Descripcion)) throw new ReglaNegocioException("Tipo y descripción son obligatorios.");
        if (c.Tipo.Trim().Length > 80 || c.Descripcion.Trim().Length > 4000) throw new ReglaNegocioException("El tipo admite hasta 80 caracteres y la descripción hasta 4000.");
        var r = await db.Rondines.SingleOrDefaultAsync(x => x.Id == c.RondinId, ct) ?? throw new ReglaNegocioException("El rondín no existe.");
        if (r.PrefectoId != actor) throw new ReglaNegocioException("No tienes permiso para registrar una incidencia en este rondín.");
        if (r.Estado != RondinEstado.EnCurso) throw new ReglaNegocioException("El rondín debe estar en curso.");
        if (!Enum.IsDefined(c.Prioridad)) throw new ReglaNegocioException("Prioridad inválida.");
        if (c.PuntoControlId.HasValue && !await db.RegistrosPunto.AnyAsync(x => x.RondinId == r.Id && x.PuntoControlId == c.PuntoControlId, ct)) throw new ReglaNegocioException("El punto no pertenece al rondín.");
        var i = new Incidencia { RondinId = c.RondinId, PuntoControlId = c.PuntoControlId, Tipo = c.Tipo.Trim(), Prioridad = c.Prioridad, Descripcion = c.Descripcion.Trim(), CreadoPorId = actor };
        r.ActualizadoEn = DateTime.UtcNow; // Serialize against cancellation using the round's concurrency token.
        db.Incidencias.Add(i); db.Auditorias.Add(new Auditoria { ActorId = actor, Accion = "Crear", Entidad = "Incidencia", EntidadId = i.Id.ToString() }); await db.SaveChangesAsync(ct); return i;
    }
    public async Task<Incidencia> CambiarEstadoAsync(Guid id, CambiarIncidenciaCommand c, string actor, CancellationToken ct = default)
    {
        if (session.UserId != actor || session.Role != Roles.Administrador) throw new ReglaNegocioException("Se requiere administrador.");
        var i = await db.Incidencias.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ReglaNegocioException("La incidencia no existe.");
        if (!EsTransicionValida(i.Estado, c.Estado)) throw new ReglaNegocioException("La transición de estado no es válida.");
        var anterior = i.Estado;
        i.Estado = c.Estado; if (c.Estado == IncidenciaEstado.Resuelta) i.ResueltaEn = DateTime.UtcNow; i.ActualizadoEn = DateTime.UtcNow;
        db.Auditorias.Add(new Auditoria { ActorId = actor, Accion = "CambiarEstado", Entidad = "Incidencia", EntidadId = id.ToString(), Detalle = $"{anterior} → {c.Estado}: {c.Comentario}" }); await db.SaveChangesAsync(ct); return i;
    }
    private static bool EsTransicionValida(IncidenciaEstado desde, IncidenciaEstado hacia) => (desde, hacia) is (IncidenciaEstado.Abierta, IncidenciaEstado.EnRevision or IncidenciaEstado.Resuelta) or (IncidenciaEstado.EnRevision, IncidenciaEstado.Resuelta) or (IncidenciaEstado.Resuelta, IncidenciaEstado.Cerrada);
}

public sealed class DashboardService(FtrackerDbContext db) : IDashboardService
{
    public async Task<DashboardDto> ObtenerAsync(CancellationToken ct = default)
    {
        var hoy = DateTime.UtcNow.Date;
        return new DashboardDto(await db.Rondines.CountAsync(x => x.Estado == RondinEstado.Programado, ct), await db.Rondines.CountAsync(x => x.Estado == RondinEstado.EnCurso, ct), await db.Rondines.CountAsync(x => x.Estado == RondinEstado.Cerrado && x.CerradoEn >= hoy, ct), await db.Incidencias.CountAsync(x => x.Estado == IncidenciaEstado.Abierta || x.Estado == IncidenciaEstado.EnRevision, ct));
    }
}

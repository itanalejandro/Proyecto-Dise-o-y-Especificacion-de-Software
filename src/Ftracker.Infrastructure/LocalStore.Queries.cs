using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

// Implementa el contrato común (IServerStore): consultas que antes vivían en la interfaz de usuario y envoltorios de los servicios.
public sealed partial class LocalStore : IServerStore
{
    ISession IFtrackerStore.Session => Session;
    public void SignOut() => Session.SignOut();

    private string RequireSession() => Session.UserId ?? throw new ReglaNegocioException("Inicia sesión para continuar.");
    private bool IsAdmin => Session.Role == Roles.Administrador;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public Task<UsuarioSesionDto?> GetCurrentUserAsync() =>
        Task.FromResult(Session.User is { } u ? new UsuarioSesionDto(u.Id, u.NombreCompleto, u.Email, u.Rol) : null);

    // ---- Lecturas (las listas de administración devuelven vacío a quien no es administrador, como hacía la interfaz) ----

    public async Task<IReadOnlyList<UsuarioDto>> GetUsersAsync()
    {
        RequireSession(); if (!IsAdmin) return [];
        await using var db = Open();
        return await db.Users.AsNoTracking().OrderBy(x => x.NombreCompleto).Select(x => new UsuarioDto(x.Id, x.NombreCompleto, x.Email, x.Rol, x.Activo)).ToListAsync();
    }

    public async Task<IReadOnlyList<RutaDto>> GetRoutesAsync()
    {
        RequireSession(); if (!IsAdmin) return [];
        await using var db = Open();
        return await db.Rutas.AsNoTracking().OrderBy(x => x.Nombre).Select(x => new RutaDto(x.Id, x.Nombre, x.Puntos.Count, x.Activa)).ToListAsync();
    }

    public async Task<IReadOnlyList<RondinDto>> GetRoundsAsync()
    {
        var userId = RequireSession();
        await using var db = Open();
        var query = db.Rondines.AsNoTracking().Include(x => x.Ruta).AsQueryable();
        if (!IsAdmin) query = query.Where(x => x.PrefectoId == userId);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.NombreCompleto);
        var rows = await query.OrderByDescending(x => x.ProgramadoPara).Select(x => new { Round = x, HasPhoto = x.EvidenciaCierre != null }).ToListAsync();
        return rows.Select(x => new RondinDto(x.Round.Id, x.Round.Ruta.Nombre, names.GetValueOrDefault(x.Round.PrefectoId, "Usuario"), Utc(x.Round.ProgramadoPara), x.Round.Estado,
            x.Round.CerradoEn.HasValue ? Utc(x.Round.CerradoEn.Value) : null, x.Round.PrefectoId, x.HasPhoto)).ToList();
    }

    public async Task<IReadOnlyList<RegistroPuntoDto>> GetRoundPointsAsync(Guid rondinId)
    {
        var userId = RequireSession();
        await using var db = Open();
        var round = await db.Rondines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rondinId) ?? throw new ReglaNegocioException("El rondín no existe.");
        if (!IsAdmin && round.PrefectoId != userId) throw new ReglaNegocioException("No tienes permiso para consultar este rondín.");
        var linked = await db.Incidencias.AsNoTracking()
            .Where(x => x.RondinId == rondinId && x.PuntoControlId != null && x.Evidencias.Any())
            .OrderByDescending(x => x.CreadoEn)
            .Select(x => new { x.Id, x.PuntoControlId, x.Tipo, x.Descripcion }).ToListAsync();
        var rows = await db.RegistrosPunto.AsNoTracking().Where(x => x.RondinId == rondinId).OrderBy(x => x.Orden).ToListAsync();
        var pointIds = rows.Select(x => x.PuntoControlId).ToArray();
        var coded = (await db.PuntosControl.AsNoTracking().Where(x => pointIds.Contains(x.Id) && x.CodigoVerificacion != null).Select(x => x.Id).ToListAsync()).ToHashSet();
        return rows.Select(p => new RegistroPuntoDto(p.Id, p.PuntoControlId, p.Orden, p.PuntoNombre, p.Resultado, p.Observaciones, p.Verificado, coded.Contains(p.PuntoControlId),
            linked.Where(i => i.PuntoControlId == p.PuntoControlId).Select(i => new IncidenciaPuntoDto(i.Id, i.Tipo + ": " + i.Descripcion)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<IncidenciaDto>> GetIncidentsAsync()
    {
        var userId = RequireSession();
        await using var db = Open();
        var query = db.Incidencias.AsNoTracking().Include(x => x.Rondin).ThenInclude(x => x.Ruta).Include(x => x.Evidencias).AsQueryable();
        if (!IsAdmin) query = query.Where(x => x.Rondin.PrefectoId == userId);
        return (await query.OrderByDescending(x => x.CreadoEn).ToListAsync())
            .Select(x => new IncidenciaDto(x.Id, x.Rondin.Ruta.Nombre, x.Tipo, x.Descripcion, x.Prioridad, x.Estado, x.Evidencias.Count, x.RondinId)).ToList();
    }

    public async Task<IReadOnlyList<AuditoriaDto>> GetAuditAsync(int take = 200)
    {
        RequireSession(); if (!IsAdmin) return [];
        await using var db = Open();
        return (await db.Auditorias.AsNoTracking().OrderByDescending(x => x.Fecha).Take(Math.Clamp(take, 1, 1000)).ToListAsync())
            .Select(x => new AuditoriaDto(x.Id, x.ActorId, x.Accion, x.Entidad, x.EntidadId, Utc(x.Fecha), x.Detalle)).ToList();
    }

    public async Task<IReadOnlyList<EvidenciaDto>> ListEvidenceAsync(Guid incidentId) =>
        (await GetEvidenceAsync(incidentId)).Select(x => new EvidenciaDto(x.Id, x.NombreOriginal, x.TipoMime, Utc(x.SubidaEn))).ToList();

    // ---- Operaciones sobre rondines e incidencias (los servicios validan permisos con la sesión de esta instancia) ----

    public async Task CreateRoundAsync(CrearRondinCommand command)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).CrearAsync(command, userId); }

    public async Task StartRoundAsync(Guid rondinId)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).IniciarAsync(rondinId, userId); }

    public async Task MarkPointAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).MarcarPuntoAsync(rondinId, registroId, command, userId); }

    public async Task CloseRoundAsync(Guid rondinId, string? observaciones, FotoCierreCommand fotografia)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).CerrarAsync(rondinId, observaciones, userId, fotografia); }

    public async Task CancelRoundAsync(Guid rondinId, string motivo)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).CancelarAsync(rondinId, motivo, userId); }

    public async Task ReassignRoundAsync(Guid rondinId, ReasignarRondinCommand command)
    { var userId = RequireSession(); await using var db = Open(); await new RondinService(db, Session).ReasignarAsync(rondinId, command, userId); }

    public async Task<Guid> CreateIncidentAsync(CrearIncidenciaCommand command)
    { var userId = RequireSession(); await using var db = Open(); return (await new IncidenciaService(db, Session).CrearAsync(command, userId)).Id; }

    public async Task ChangeIncidentStateAsync(Guid id, CambiarIncidenciaCommand command)
    { var userId = RequireSession(); await using var db = Open(); await new IncidenciaService(db, Session).CambiarEstadoAsync(id, command, userId); }

    // ---- Transporte por HTTP: el servidor entrega y recibe bytes en lugar de rutas de archivo ----

    public async Task SaveEvidenceBytesAsync(Guid incidentId, string fileName, byte[] content)
    {
        if (content is null || content.Length == 0 || content.Length > 5 * 1024 * 1024) throw new ReglaNegocioException("El archivo está vacío o supera el límite de 5 MB.");
        var safe = Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safe) || safe.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) safe = "archivo";
        var folder = Path.Combine(Path.GetTempPath(), "Ftracker-evidencia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, safe);
            await File.WriteAllBytesAsync(path, content);
            await SaveEvidenceAsync(incidentId, path); // reutiliza todas las validaciones existentes
        }
        finally { TryDelete(folder); }
    }

    public async Task<ArchivoGenerado> BuildReportAsync(ReportKind kind, DateTime? desde, DateTime? hasta, string? prefectoId)
    {
        RequireAdmin();
        var path = TempFile(".csv");
        try
        {
            var rows = await ExportReportAsync(kind, path, desde, hasta, prefectoId);
            return new ArchivoGenerado(await File.ReadAllBytesAsync(path), rows, $"Ftracker-{kind.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd}.csv");
        }
        finally { TryDelete(path); }
    }

    public async Task<ArchivoGenerado> BuildCodeSheetAsync(Guid rutaId)
    {
        RequireAdmin();
        var path = TempFile(".html");
        try
        {
            var rows = await WriteCodeSheetAsync(rutaId, path);
            return new ArchivoGenerado(await File.ReadAllBytesAsync(path), rows, $"Codigos-{DateTime.Now:yyyyMMdd}.html");
        }
        finally { TryDelete(path); }
    }

    public async Task WriteBackupToAsync(Stream destination)
    {
        RequireAdmin();
        var path = TempFile(".zip");
        try
        {
            await CreateBackupAsync(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await input.CopyToAsync(destination);
        }
        finally { TryDelete(path); }
    }

    public async Task RestoreBackupFromAsync(Stream source)
    {
        RequireAdmin();
        var path = TempFile(".zip");
        try
        {
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true)) await source.CopyToAsync(output);
            await RestoreBackupAsync(path);
        }
        finally { TryDelete(path); }
    }

    private static string TempFile(string extension) => Path.Combine(Path.GetTempPath(), "Ftracker-" + Guid.NewGuid().ToString("N") + extension);
    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); else if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

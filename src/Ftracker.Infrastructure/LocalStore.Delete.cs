using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    /// <summary>Elimina un usuario. Se bloquea si es la cuenta activa o si tiene rondines asignados (se conserva el historial).</summary>
    public async Task DeleteUserAsync(string id)
    {
        RequireAdmin();
        if (id == Session.UserId) throw new ReglaNegocioException("No puedes eliminar tu propia cuenta.");
        await using var db = Open();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id) ?? throw new ReglaNegocioException("El usuario ya no existe. Actualiza los datos.");
        var rounds = await db.Rondines.CountAsync(x => x.PrefectoId == id);
        if (rounds > 0)
            throw new ReglaNegocioException($"{user.NombreCompleto} tiene {rounds} rondín(es) asignado(s). Elimina esos rondines primero o desactiva la cuenta para conservar el historial.");
        if (user.Rol == Roles.Administrador && !await db.Users.AnyAsync(x => x.Id != id && x.Rol == Roles.Administrador && x.Activo))
            throw new ReglaNegocioException("Debe quedar al menos un administrador activo.");
        db.Users.Remove(user);
        Audit(db, "EliminarUsuario", id, $"{user.NombreCompleto} <{user.Email}> ({user.Rol})");
        await db.SaveChangesAsync();
    }

    /// <summary>Elimina una ruta con sus puntos y ubicaciones del croquis. Se bloquea si algún rondín la usa.</summary>
    public async Task DeleteRouteAsync(Guid id)
    {
        RequireAdmin();
        await using var db = Open();
        var route = await db.Rutas.Include(x => x.Puntos).SingleOrDefaultAsync(x => x.Id == id) ?? throw new ReglaNegocioException("La ruta ya no existe. Actualiza los datos.");
        var rounds = await db.Rondines.CountAsync(x => x.RutaId == id);
        if (rounds > 0)
            throw new ReglaNegocioException($"La ruta {route.Nombre} se usa en {rounds} rondín(es). Elimina esos rondines primero o desactiva la ruta para conservar el historial.");
        var pointIds = route.Puntos.Select(x => x.Id).ToArray();
        db.UbicacionesCroquis.RemoveRange(await db.UbicacionesCroquis.Where(x => pointIds.Contains(x.Id)).ToListAsync());
        db.PuntosControl.RemoveRange(route.Puntos);
        db.Rutas.Remove(route);
        Audit(db, "EliminarRuta", id.ToString(), $"{route.Nombre} ({route.Puntos.Count} puntos)");
        await db.SaveChangesAsync();
    }

    /// <summary>Elimina un rondín junto con sus puntos registrados, incidencias y archivos de evidencia.</summary>
    public async Task DeleteRoundAsync(Guid id)
    {
        RequireAdmin();
        await using var db = Open();
        var round = await db.Rondines.Include(x => x.Ruta).Include(x => x.Registros).Include(x => x.Incidencias).ThenInclude(x => x.Evidencias)
            .SingleOrDefaultAsync(x => x.Id == id) ?? throw new ReglaNegocioException("El rondín ya no existe. Actualiza los datos.");
        var files = round.Incidencias.SelectMany(x => x.Evidencias).Select(x => x.RutaArchivo).ToList();
        var recordIds = round.Registros.Select(x => x.Id).ToArray();
        db.UbicacionesCroquis.RemoveRange(await db.UbicacionesCroquis.Where(x => recordIds.Contains(x.Id)).ToListAsync());
        db.Evidencias.RemoveRange(round.Incidencias.SelectMany(x => x.Evidencias));
        db.Incidencias.RemoveRange(round.Incidencias);
        db.RegistrosPunto.RemoveRange(round.Registros);
        db.Rondines.Remove(round);
        Audit(db, "EliminarRondin", id.ToString(), $"Ruta: {round.Ruta.Nombre}. Estado: {round.Estado}. Incidencias: {round.Incidencias.Count}. Evidencias: {files.Count}");
        await db.SaveChangesAsync();
        // Solo después de confirmar la base de datos, y solo dentro de la carpeta de evidencias de la aplicación.
        var directory = Path.GetFullPath(Path.Combine(DataDirectory, "evidencias"));
        foreach (var file in files)
        {
            try
            {
                var path = Path.GetFullPath(file);
                if (string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception) { /* el registro ya se eliminó; un archivo huérfano no debe interrumpir la operación */ }
        }
    }
}

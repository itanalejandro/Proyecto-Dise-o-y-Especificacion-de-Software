using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    public async Task<IReadOnlyList<Evidencia>> GetEvidenceAsync(Guid incidentId)
    {
        await using var db = Open();
        var incident = await db.Incidencias.AsNoTracking().Include(x => x.Rondin).SingleOrDefaultAsync(x => x.Id == incidentId)
            ?? throw new ReglaNegocioException("La incidencia no existe.");
        RequireEvidenceAccess(incident);
        return await db.Evidencias.AsNoTracking().Where(x => x.IncidenciaId == incidentId).OrderBy(x => x.SubidaEn).ThenBy(x => x.Id).ToListAsync();
    }

    public async Task<byte[]> ReadEvidenceAsync(Guid evidenceId)
    {
        await using var db = Open();
        var evidence = await db.Evidencias.AsNoTracking().Include(x => x.Incidencia).ThenInclude(x => x.Rondin)
            .SingleOrDefaultAsync(x => x.Id == evidenceId) ?? throw new ReglaNegocioException("La evidencia no existe.");
        RequireEvidenceAccess(evidence.Incidencia);
        try
        {
            // Only read the app's local evidence directory; never load a URI or arbitrary path from the database.
            var path = ResolveEvidencePath(evidence.RutaArchivo);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            if (input.Length == 0 || input.Length > 5 * 1024 * 1024)
                throw new ReglaNegocioException("El archivo está vacío o supera el límite de 5 MB.");
            using var output = new MemoryStream((int)input.Length);
            await input.CopyToAsync(output);
            return output.ToArray();
        }
        catch (FileNotFoundException) { throw new ReglaNegocioException("El archivo ya no está disponible. Puede haberse movido o eliminado."); }
        catch (DirectoryNotFoundException) { throw new ReglaNegocioException("El archivo ya no está disponible. Puede haberse movido o eliminado."); }
        catch (UnauthorizedAccessException) { throw new ReglaNegocioException("No tienes permiso para leer el archivo en este equipo."); }
        catch (IOException) { throw new ReglaNegocioException("No se pudo leer el archivo. Puede estar en uso o no disponible en este equipo."); }
        catch (ArgumentException) { throw new ReglaNegocioException("La ubicación del archivo no es válida para esta evidencia."); }
        catch (NotSupportedException) { throw new ReglaNegocioException("La ubicación del archivo no es válida para esta evidencia."); }
    }

    /// <summary>Resuelve el archivo por su nombre dentro de la carpeta de evidencias actual. Así un respaldo restaurado en otro equipo sigue funcionando y nunca se lee fuera de esa carpeta.</summary>
    internal string ResolveEvidencePath(string stored)
    {
        var name = Path.GetFileName((stored ?? "").Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ReglaNegocioException("La ubicación del archivo no es válida para esta evidencia.");
        return Path.GetFullPath(Path.Combine(DataDirectory, "evidencias", name));
    }

    private void RequireEvidenceAccess(Incidencia incident)
    {
        if (Session.Role != Roles.Administrador && (Session.Role != Roles.Prefecto || Session.UserId != incident.Rondin.PrefectoId))
            throw new ReglaNegocioException("No tienes permiso para consultar las evidencias de esta incidencia.");
    }
}

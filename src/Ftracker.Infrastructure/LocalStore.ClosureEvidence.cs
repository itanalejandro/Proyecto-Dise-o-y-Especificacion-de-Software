using System.Security.Cryptography;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    private async Task RequireClosureAccessAsync(FtrackerDbContext db, Guid roundId)
    {
        var round = await db.Rondines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == roundId)
            ?? throw new ReglaNegocioException("El rondín no existe.");
        if (Session.Role != Roles.Administrador && (Session.Role != Roles.Prefecto || Session.UserId != round.PrefectoId))
            throw new ReglaNegocioException("No tienes permiso para consultar la hoja de firmas de este rondín.");
    }

    public async Task<FotoCierreInfo?> GetClosureEvidenceAsync(Guid roundId)
    {
        await using var db = Open(); await RequireClosureAccessAsync(db, roundId);
        var info = await db.EvidenciasCierre.AsNoTracking().Where(x => x.RondinId == roundId)
            .Select(x => new { x.Id, x.NombreOriginal, x.TipoMime, x.SubidaEn, x.CreadoPorId, x.Sha256 }).SingleOrDefaultAsync();
        if (info is null) return null; // Historic closed rounds are not given fabricated evidence.
        var author = await db.Users.Where(x => x.Id == info.CreadoPorId).Select(x => x.NombreCompleto).SingleOrDefaultAsync();
        return new FotoCierreInfo(info.Id, info.NombreOriginal, info.TipoMime, info.SubidaEn, author ?? "Usuario", info.Sha256);
    }

    public async Task<byte[]> ReadClosureEvidenceAsync(Guid roundId)
    {
        await using var db = Open(); await RequireClosureAccessAsync(db, roundId);
        var photo = await db.EvidenciasCierre.AsNoTracking().SingleOrDefaultAsync(x => x.RondinId == roundId)
            ?? throw new ReglaNegocioException("Este rondín no tiene una hoja de firmas adjunta. Puede ser un cierre anterior a esta actualización.");
        if (photo.Contenido.Length is 0 or > ClosurePhoto.MaxBytes ||
            !string.Equals(Convert.ToHexStringLower(SHA256.HashData(photo.Contenido)), photo.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ReglaNegocioException("No se pudo comprobar la integridad de la fotografía guardada. Solicita revisión al responsable.");
        return photo.Contenido;
    }
}

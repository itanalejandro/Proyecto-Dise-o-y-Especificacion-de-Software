using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

/// <summary>Un LocalStore nuevo por petición: cada solicitud lleva su propia sesión y se revalida contra la base (rol y estado actuales).</summary>
public sealed class LocalStoreFactory(string? dataDirectory = null, IEvidenceBlobStore? blobs = null) : IStoreFactory
{
    public Task InitializeAsync() => new LocalStore(dataDirectory).InitializeAsync();

    public Task CreateBackupFileAsync(string zipPath) => new LocalStore(dataDirectory).BackupToFileAsync(zipPath);

    public IServerStore CreateAnonymous() => new LocalStore(dataDirectory) { Blobs = blobs };

    public async Task<IServerStore?> CreateForUserAsync(string userId)
    {
        var store = new LocalStore(dataDirectory) { Blobs = blobs };
        await using var db = store.Open();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.Activo);
        if (user is null) return null;
        store.Session.SignIn(user);
        return store;
    }

    public async Task RecordSecurityEventAsync(string action, string detail)
    {
        await using var db = new LocalStore(dataDirectory).Open();
        db.Auditorias.Add(new Auditoria
        {
            ActorId = "sistema", Accion = action, Entidad = "Seguridad", EntidadId = "-",
            Detalle = detail.Length > 500 ? detail[..500] : detail
        });
        await db.SaveChangesAsync();
    }
}

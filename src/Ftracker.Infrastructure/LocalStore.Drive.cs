using Ftracker.Application;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    /// <summary>Si se asigna, las evidencias nuevas se guardan aquí (Drive) en lugar de la carpeta local. Las ya guardadas en disco se siguen leyendo.</summary>
    public IEvidenceBlobStore? Blobs { get; init; }

    private static bool IsBlobKey(string stored) => stored.StartsWith(BlobKeys.DrivePrefix, StringComparison.Ordinal);

    /// <summary>Uso interno del servidor (respaldo programado, sin sesión de usuario): crea el .zip en la ruta indicada.</summary>
    public Task BackupToFileAsync(string zipPath) => Task.Run(() => { WriteBackup(zipPath); });
}

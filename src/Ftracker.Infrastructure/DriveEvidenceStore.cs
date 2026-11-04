using System.Text.RegularExpressions;
using Ftracker.Application;

namespace Ftracker.Infrastructure;

/// <summary>Guarda las evidencias en una carpeta de Google Drive (idealmente una unidad compartida de la organización).</summary>
public sealed class DriveEvidenceStore(GoogleDriveClient drive, string folderId) : IEvidenceBlobStore
{
    private static readonly Regex ValidId = new("^[A-Za-z0-9_-]{10,120}$", RegexOptions.Compiled);

    public async Task<string> SaveAsync(string fileName, string contentType, byte[] content, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content, writable: false);
        return BlobKeys.DrivePrefix + await drive.UploadAsync(folderId, fileName, contentType, stream, content.Length, ct);
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken ct = default) => drive.DownloadAsync(IdOf(key), ct);
    public Task DeleteAsync(string key, CancellationToken ct = default) => drive.DeleteAsync(IdOf(key), ct);

    // La clave viene de la base de datos: se valida antes de armar cualquier dirección.
    private static string IdOf(string key)
    {
        var id = key.StartsWith(BlobKeys.DrivePrefix, StringComparison.Ordinal) ? key[BlobKeys.DrivePrefix.Length..] : "";
        return ValidId.IsMatch(id) ? id : throw new ReglaNegocioException("La referencia de la evidencia no es válida.");
    }
}

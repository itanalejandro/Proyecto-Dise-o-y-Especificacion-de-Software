namespace Ftracker.Application;

public static class BlobKeys
{
    /// <summary>Las evidencias guardadas en Google Drive se referencian en la base como "drive:ID".</summary>
    public const string DrivePrefix = "drive:";
}

/// <summary>Dónde se guardan los archivos de evidencia además del disco local (por ejemplo, Google Drive).</summary>
public interface IEvidenceBlobStore
{
    /// <summary>Guarda el archivo y devuelve la clave con la que se leerá o borrará después.</summary>
    Task<string> SaveAsync(string fileName, string contentType, byte[] content, CancellationToken ct = default);
    Task<byte[]> ReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>Falla de comunicación con Google Drive (red, permisos, cuota). El mensaje técnico es solo para el registro del servidor.</summary>
public sealed class DriveException(string message, Exception? inner = null) : Exception(message, inner);

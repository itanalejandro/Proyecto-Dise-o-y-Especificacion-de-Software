using System.IO.Compression;
using System.Text.Json;
using Ftracker.Application;
using Ftracker.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ftracker.Infrastructure;

public sealed partial class LocalStore
{
    private const string ManifestName = "manifiesto.json";
    private const long MaxEvidenceBytes = 5 * 1024 * 1024;
    private const long MaxDatabaseBytes = 1L << 30;
    private const int MaxEvidenceFiles = 50000;
    private static readonly string[] EvidenceExtensions = [".jpg", ".jpeg", ".png", ".pdf"];

    /// <summary>Crea un .zip con una copia consistente de la base de datos y todas las evidencias. No está cifrado.</summary>
    public async Task CreateBackupAsync(string zipPath)
    {
        RequireAdmin();
        var evidence = await Task.Run(() => WriteBackup(zipPath));
        await using var db = Open();
        Audit(db, "CrearRespaldo", Path.GetFileName(zipPath), $"{evidence} evidencia(s)");
        await db.SaveChangesAsync();
    }

    private int WriteBackup(string zipPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "Ftracker-respaldo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var snapshot = Path.Combine(temp, DbFileName);
            // API de respaldo de SQLite: copia consistente aunque la base esté en uso.
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ToString()))
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
            {
                source.Open(); destination.Open(); source.BackupDatabase(destination);
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var count = 0;
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(snapshot, DbFileName, CompressionLevel.Optimal);
            var directory = Path.Combine(DataDirectory, "evidencias");
            if (Directory.Exists(directory))
                foreach (var file in Directory.EnumerateFiles(directory)) { zip.CreateEntryFromFile(file, "evidencias/" + Path.GetFileName(file), CompressionLevel.Optimal); count++; }
            var manifest = zip.CreateEntry(ManifestName);
            using (var writer = new StreamWriter(manifest.Open()))
                writer.Write(JsonSerializer.Serialize(new { aplicacion = "Ftracker", formato = 1, creadoUtc = DateTime.UtcNow, evidencias = count }));
            return count;
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>
    /// Reemplaza la base y las evidencias por las del respaldo. Antes guarda un respaldo de seguridad de los datos actuales
    /// en la carpeta "respaldos". Cierra la sesión: el llamador debe cerrar la aplicación para volver a iniciar.
    /// </summary>
    public async Task RestoreBackupAsync(string zipPath)
    {
        RequireAdmin();
        var actor = Session.UserId!;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        // Misma unidad que los datos, porque Directory.Move no funciona entre unidades.
        var work = Path.Combine(DataDirectory, "restauracion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var staged = await Task.Run(() => StageBackup(zipPath, work));
            var safety = Path.Combine(DataDirectory, "respaldos"); Directory.CreateDirectory(safety);
            await Task.Run(() => WriteBackup(Path.Combine(safety, $"antes-de-restaurar-{stamp}.zip")));

            SqliteConnection.ClearAllPools();
            var live = Path.Combine(DataDirectory, "evidencias");
            var previous = Path.Combine(DataDirectory, "evidencias.anterior-" + stamp);
            var hadEvidence = Directory.Exists(live);
            if (hadEvidence) Directory.Move(live, previous);
            try
            {
                Directory.Move(staged.EvidenceDirectory, live);
                foreach (var suffix in new[] { "-wal", "-shm" }) if (File.Exists(DbPath + suffix)) File.Delete(DbPath + suffix);
                File.Move(staged.DatabasePath, DbPath, overwrite: true);
            }
            catch
            {
                // Devuelve las evidencias originales si algo falló a medio camino.
                if (Directory.Exists(live)) Directory.Delete(live, true);
                if (hadEvidence) Directory.Move(previous, live);
                throw;
            }
            if (hadEvidence) try { Directory.Delete(previous, true); } catch (IOException) { }
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        Session.SignOut();
        await InitializeAsync(); // sube el esquema si el respaldo viene de una versión anterior
        await using var db = Open();
        db.Auditorias.Add(new Auditoria { ActorId = actor, Accion = "RestaurarRespaldo", Entidad = "Sistema", EntidadId = Path.GetFileName(zipPath), Detalle = "Datos reemplazados desde un respaldo." });
        await db.SaveChangesAsync();
    }

    private static (string DatabasePath, string EvidenceDirectory) StageBackup(string zipPath, string work)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { throw new ReglaNegocioException("No se pudo abrir el archivo de respaldo. Verifica que sea un .zip creado por Ftracker."); }
        using (zip)
        {
            var manifest = zip.GetEntry(ManifestName) ?? throw new ReglaNegocioException("El archivo no es un respaldo de Ftracker.");
            try
            {
                using var document = JsonDocument.Parse(manifest.Open());
                if (!document.RootElement.TryGetProperty("aplicacion", out var app) || app.GetString() != "Ftracker" ||
                    !document.RootElement.TryGetProperty("formato", out var format) || format.GetInt32() != 1)
                    throw new ReglaNegocioException("El archivo no es un respaldo de Ftracker compatible.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { throw new ReglaNegocioException("El manifiesto del respaldo no es válido."); }

            var database = zip.GetEntry(DbFileName) ?? throw new ReglaNegocioException("El respaldo no contiene la base de datos.");
            if (database.Length == 0 || database.Length > MaxDatabaseBytes) throw new ReglaNegocioException("La base de datos del respaldo no es válida.");
            var databasePath = Path.Combine(work, DbFileName);
            database.ExtractToFile(databasePath);
            ValidateRestoredDatabase(databasePath);

            var evidenceDirectory = Path.Combine(work, "evidencias"); Directory.CreateDirectory(evidenceDirectory);
            var files = 0;
            foreach (var entry in zip.Entries.Where(x => x.FullName.StartsWith("evidencias/", StringComparison.Ordinal) && x.Name.Length > 0))
            {
                var name = entry.Name;
                // Solo archivos directos de la carpeta, con extensión permitida y tamaño razonable: sin rutas relativas ni "..".
                if (entry.FullName != "evidencias/" + name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith('.') ||
                    !EvidenceExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()) || entry.Length > MaxEvidenceBytes || ++files > MaxEvidenceFiles)
                    throw new ReglaNegocioException("El respaldo contiene archivos de evidencia no válidos.");
                entry.ExtractToFile(Path.Combine(evidenceDirectory, name), overwrite: false);
            }
            return (databasePath, evidenceDirectory);
        }
    }

    private static void ValidateRestoredDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            long Scalar(string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar()); }
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)) throw new ReglaNegocioException("La base de datos del respaldo está dañada.");
            }
            if (Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Users','Rutas','Rondines','Incidencias','Evidencias','Auditorias')") != 6)
                throw new ReglaNegocioException("La base de datos del respaldo no tiene la estructura de Ftracker.");
            // Evita dejar la aplicación sin ningún administrador que pueda entrar.
            if (Scalar("SELECT COUNT(*) FROM Users WHERE Rol = 'Administrador' AND Activo = 1") < 1)
                throw new ReglaNegocioException("El respaldo no tiene ningún administrador activo.");
        }
        catch (SqliteException) { throw new ReglaNegocioException("La base de datos del respaldo no se pudo leer."); }
    }
}

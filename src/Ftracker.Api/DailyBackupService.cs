using Ftracker.Application;
using Ftracker.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ftracker.Api;

/// <summary>Cada día, a la hora configurada, sube un respaldo de la base a Drive y conserva solo los más recientes.</summary>
public sealed class DailyBackupService(IStoreFactory factory, GoogleDriveClient drive, string folderId, TimeOnly runAt, int keep,
    ILogger<DailyBackupService> logger, Func<DateTime>? clock = null) : BackgroundService
{
    private const string NamePrefix = "Ftracker-respaldo-";
    private readonly Func<DateTime> now = clock ?? (() => DateTime.Now);

    /// <summary>Próxima ejecución a la hora indicada: hoy si todavía no pasa, si no mañana.</summary>
    public static DateTime NextRun(DateTime now, TimeOnly at)
    {
        var today = now.Date + at.ToTimeSpan();
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>Hace un respaldo ahora. Devuelve el nombre del archivo subido.</summary>
    public async Task<string> RunOnceAsync(CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "Ftracker-respaldo-programado-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await factory.CreateBackupFileAsync(path);
            var name = $"{NamePrefix}{now():yyyyMMdd-HHmm}.zip";
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                await drive.UploadAsync(folderId, name, "application/zip", stream, stream.Length, ct);
            // Solo se borran respaldos propios (por su nombre) y siempre se conservan los más nuevos.
            var old = (await drive.ListAsync(folderId, ct)).Where(x => x.Name.StartsWith(NamePrefix, StringComparison.Ordinal)).Skip(Math.Max(1, keep));
            foreach (var file in old) await drive.DeleteAsync(file.Id, ct);
            return name;
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(NextRun(now(), runAt) - now(), stoppingToken); }
            catch (OperationCanceledException) { return; }
            try { logger.LogInformation("Respaldo programado subido a Drive: {Nombre}", await RunOnceAsync(stoppingToken)); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Falló el respaldo programado a Drive."); }
        }
    }
}

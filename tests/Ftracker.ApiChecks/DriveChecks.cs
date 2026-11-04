using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ftracker.Api;
using Ftracker.Application;
using Ftracker.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ftracker.ApiChecks;

/// <summary>Pruebas del cliente de Drive, el almacén de evidencias y el respaldo diario contra un "Google" falso en memoria.</summary>
public static class DriveChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var rsa = RSA.Create(2048);
        var options = new DriveOptions("cuenta@proyecto.iam.gserviceaccount.com", rsa.ExportPkcs8PrivateKeyPem(), "https://oauth.test/token");
        var google = new FakeGoogle(rsa);
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        using var drive = new GoogleDriveClient(options, google, () => now);
        const string folder = "carpetaEvidencias123";

        var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)(i * 13 % 251)).ToArray();
        string id;
        using (var stream = new MemoryStream(bytes)) id = await drive.UploadAsync(folder, "foto.jpg", "image/jpeg", stream, bytes.Length);
        var file = google.Files[id];
        check(google.TokenRequests == 1 && google.JwtValid, "Drive: el JWT de la cuenta de servicio se firma y valida (RS256, alcance, vigencia)");
        check(file.Name == "foto.jpg" && file.Folder == folder && file.ContentType == "image/jpeg" && file.Bytes.SequenceEqual(bytes), "Drive: subida reanudable conserva nombre, carpeta, tipo y bytes");
        check(google.SessionUploadHadNoAuth, "Drive: el envío del contenido no reenvía el token a la dirección de sesión");
        check((await drive.DownloadAsync(id)).SequenceEqual(bytes) && google.TokenRequests == 1, "Drive: descarga idéntica y el token se reutiliza");

        google.Expire401Once = true;
        check((await drive.DownloadAsync(id)).Length == bytes.Length && google.TokenRequests == 2, "Drive: un token vencido (401) se renueva y la operación se reintenta");

        await drive.DeleteAsync(id);
        await drive.DeleteAsync(id); // ya no existe: no debe fallar
        check(!google.Files.ContainsKey(id), "Drive: borrar es idempotente");

        google.FailWith = HttpStatusCode.InternalServerError;
        var failure = await Capture(() => drive.DownloadAsync("archivoInexistente1"));
        google.FailWith = null;
        check(failure is DriveException { Message: var m1 } && m1.Contains("500"), "Drive: un error del servidor de Google llega como DriveException con el código");
        google.Offline = true;
        check(await Capture(() => drive.DownloadAsync("archivoInexistente1")) is DriveException { Message: var m2 } && m2.Contains("conectar"), "Drive: sin red se informa como DriveException");
        google.Offline = false;

        // Almacén de evidencias
        var store = new DriveEvidenceStore(drive, folder);
        var key = await store.SaveAsync("evidencia-uno.pdf", "application/pdf", bytes);
        check(key.StartsWith(BlobKeys.DrivePrefix) && (await store.ReadAsync(key)).SequenceEqual(bytes), "Evidencias: se guarda con clave drive:ID y se lee igual");
        check(await Capture(() => store.ReadAsync("drive:../../etc/passwd")) is ReglaNegocioException &&
              await Capture(() => store.ReadAsync("C:\\Windows\\win.ini")) is ReglaNegocioException &&
              await Capture(() => store.DeleteAsync("drive:corto")) is ReglaNegocioException, "Evidencias: una clave manipulada en la base no se usa para armar direcciones");
        await store.DeleteAsync(key);
        check(!google.Files.ContainsKey(key[BlobKeys.DrivePrefix.Length..]), "Evidencias: borrar elimina el archivo de Drive");

        // Respaldo diario
        check(DailyBackupService.NextRun(new(2026, 11, 2, 1, 0, 0), new(2, 0)) == new DateTime(2026, 11, 2, 2, 0, 0) &&
              DailyBackupService.NextRun(new(2026, 11, 2, 3, 0, 0), new(2, 0)) == new DateTime(2026, 11, 3, 2, 0, 0) &&
              DailyBackupService.NextRun(new(2026, 11, 2, 2, 0, 0), new(2, 0)) == new DateTime(2026, 11, 3, 2, 0, 0), "Respaldo diario: cálculo de la próxima ejecución");
        const string backupFolder = "carpetaRespaldos456";
        for (var day = 1; day <= 5; day++) google.Add($"Ftracker-respaldo-2026110{day}-0200.zip", backupFolder, "application/zip", [1, 2, 3], new DateTime(2026, 11, day, 2, 0, 0, DateTimeKind.Utc));
        google.Add("notas-de-la-ingeniera.txt", backupFolder, "text/plain", [9], new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var world = new World();
        var service = new DailyBackupService(new FakeFactory(world), drive, backupFolder, new(2, 0), keep: 3, NullLogger<DailyBackupService>.Instance, () => new DateTime(2026, 11, 6, 2, 0, 0));
        google.Clock = new DateTime(2026, 11, 6, 2, 0, 0, DateTimeKind.Utc);
        var name = await service.RunOnceAsync(CancellationToken.None);
        var inFolder = google.Files.Values.Where(x => x.Folder == backupFolder).ToList();
        var backups = inFolder.Where(x => x.Name.StartsWith("Ftracker-respaldo-")).OrderBy(x => x.Name).ToList();
        check(name == "Ftracker-respaldo-20261106-0200.zip" && backups.Count == 3 && backups.Last().Name == name && backups.Last().Bytes.SequenceEqual(world.BackupBytes), "Respaldo diario: sube el .zip completo");
        check(backups.First().Name == "Ftracker-respaldo-20261104-0200.zip" && inFolder.Any(x => x.Name == "notas-de-la-ingeniera.txt"), "Respaldo diario: conserva los 3 más recientes y nunca borra archivos ajenos");
    }

    private static async Task<Exception?> Capture(Func<Task> action) { try { await action(); return null; } catch (Exception ex) { return ex; } }

    private sealed record FakeFile(string Id, string Name, string Folder, string ContentType, byte[] Bytes, DateTime Created);

    private sealed class FakeGoogle(RSA key) : HttpMessageHandler
    {
        public Dictionary<string, FakeFile> Files { get; } = [];
        public int TokenRequests; public bool JwtValid, SessionUploadHadNoAuth, Expire401Once, Offline;
        public HttpStatusCode? FailWith; public DateTime Clock = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        private string token = ""; private readonly Dictionary<string, (string Name, string Folder, string Type)> sessions = [];
        private int counter;

        public void Add(string name, string folder, string type, byte[] bytes, DateTime created)
        { var id = "id" + (++counter).ToString("D3") + Guid.NewGuid().ToString("N")[..12]; Files[id] = new(id, name, folder, type, bytes, created); }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("sin red");
            var url = request.RequestUri!; var path = url.AbsolutePath; var auth = request.Headers.Authorization?.Parameter;

            if (url.Host == "oauth.test")
            {
                var form = (await request.Content!.ReadAsStringAsync(ct)).Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
                var parts = form["assertion"].Split('.');
                var claims = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[1]))).RootElement;
                var header = JsonDocument.Parse(Convert.FromBase64String(Pad(parts[0]))).RootElement;
                JwtValid = form["grant_type"] == "urn:ietf:params:oauth:grant-type:jwt-bearer" && header.GetProperty("alg").GetString() == "RS256" &&
                    key.VerifyData(Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]), Convert.FromBase64String(Pad(parts[2])), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) &&
                    claims.GetProperty("iss").GetString() == "cuenta@proyecto.iam.gserviceaccount.com" && claims.GetProperty("aud").GetString() == "https://oauth.test/token" &&
                    claims.GetProperty("scope").GetString() == "https://www.googleapis.com/auth/drive" &&
                    claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64() == 3600;
                token = "token-" + (++TokenRequests);
                return Json(new { access_token = token, expires_in = 3600 });
            }
            if (url.Host == "upload.test")
            {
                SessionUploadHadNoAuth = auth is null;
                var (name, folder, type) = sessions[path];
                var content = await request.Content!.ReadAsByteArrayAsync(ct);
                Add(name, folder, type, content, Clock);
                return Json(new { id = Files.Keys.Last() });
            }
            // A partir de aquí son llamadas a la API de Drive: requieren el token vigente.
            if (Expire401Once) { Expire401Once = false; return new HttpResponseMessage(HttpStatusCode.Unauthorized); }
            if (auth != token) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (FailWith is { } failure) return new HttpResponseMessage(failure) { Content = new StringContent("{\"error\":\"falla simulada\"}") };

            if (request.Method == HttpMethod.Post && path == "/upload/drive/v3/files")
            {
                var meta = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                var sessionPath = "/session/" + (++counter);
                sessions[sessionPath] = (meta.GetProperty("name").GetString()!, meta.GetProperty("parents")[0].GetString()!, request.Headers.GetValues("X-Upload-Content-Type").Single());
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location = new Uri("https://upload.test" + sessionPath);
                return response;
            }
            if (path.StartsWith("/drive/v3/files/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/drive/v3/files/".Length..]);
                if (!Files.TryGetValue(id, out var file)) return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Delete) { Files.Remove(id); return new HttpResponseMessage(HttpStatusCode.NoContent); }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file.Bytes) };
            }
            if (request.Method == HttpMethod.Get && path == "/drive/v3/files")
            {
                var q = Uri.UnescapeDataString(url.Query.TrimStart('?').Split('&').First(x => x.StartsWith("q=")).Substring(2));
                var folder = q.Split('\'')[1];
                var list = Files.Values.Where(x => x.Folder == folder).OrderByDescending(x => x.Created)
                    .Select(x => new { id = x.Id, name = x.Name, createdTime = x.Created.ToString("o") });
                return Json(new { files = list });
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        private static string Pad(string s) { s = s.Replace('-', '+').Replace('_', '/'); return s + new string('=', (4 - s.Length % 4) % 4); }
    }
}

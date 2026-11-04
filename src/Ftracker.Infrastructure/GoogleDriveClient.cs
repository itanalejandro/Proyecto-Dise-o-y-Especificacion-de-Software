using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ftracker.Application;

namespace Ftracker.Infrastructure;

public sealed record DriveOptions(string ServiceAccountEmail, string PrivateKeyPem, string TokenUri = "https://oauth2.googleapis.com/token");
public sealed record DriveFile(string Id, string Name, DateTime CreatedUtc);

/// <summary>
/// Cliente mínimo de la API REST de Google Drive v3 con cuenta de servicio (sin paquetes NuGet de Google).
/// La cuenta de servicio solo ve lo que se comparta con ella: lo previsto es una unidad compartida de la organización.
/// </summary>
public sealed class GoogleDriveClient : IDisposable
{
    private const string Scope = "https://www.googleapis.com/auth/drive";
    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";

    private readonly DriveOptions options;
    private readonly HttpClient http;
    private readonly Func<DateTime> clock;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTime accessTokenExpires;

    public GoogleDriveClient(DriveOptions options, HttpMessageHandler? handler = null, Func<DateTime>? clock = null)
    {
        this.options = options; this.clock = clock ?? (() => DateTime.UtcNow);
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = Timeout.InfiniteTimeSpan; // cada operación lleva su propio CancellationToken
    }

    public void Dispose() { http.Dispose(); tokenLock.Dispose(); }

    /// <summary>Lee el archivo JSON de la cuenta de servicio descargado de Google Cloud.</summary>
    public static DriveOptions LoadServiceAccountKey(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new DriveOptions(root.GetProperty("client_email").GetString()!, root.GetProperty("private_key").GetString()!,
                root.TryGetProperty("token_uri", out var uri) ? uri.GetString()! : "https://oauth2.googleapis.com/token");
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new DriveException("No se pudo leer el archivo de la cuenta de servicio de Google: " + path, ex);
        }
    }

    public async Task<string> UploadAsync(string folderId, string name, string contentType, Stream content, long length, CancellationToken ct = default)
    {
        // Subida reanudable en una sola petición: sirve igual para una foto de 200 KB que para un respaldo de cientos de MB.
        using var start = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadUrl}?uploadType=resumable&supportsAllDrives=true&fields=id")
            { Content = JsonContent.Create(new { name, parents = new[] { folderId } }) };
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", contentType);
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
            return request;
        }, ct);
        var session = start.Headers.Location ?? throw new DriveException("Drive no devolvió la dirección de subida.");
        using var put = new HttpRequestMessage(HttpMethod.Put, session) { Content = new StreamContent(content) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        put.Content.Headers.ContentLength = length;
        using var response = await SendRawAsync(put, ct);
        var created = await response.Content.ReadFromJsonAsync<IdResponse>(cancellationToken: ct);
        return created?.Id ?? throw new DriveException("Drive no devolvió el identificador del archivo.");
    }

    public async Task<byte[]> DownloadAsync(string fileId, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true"), ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(string fileId, CancellationToken ct = default)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{FilesUrl}/{Uri.EscapeDataString(fileId)}?supportsAllDrives=true"), ct, allowNotFound: true);
    }

    /// <summary>Archivos de una carpeta, del más reciente al más antiguo.</summary>
    public async Task<IReadOnlyList<DriveFile>> ListAsync(string folderId, CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed = false");
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{FilesUrl}?q={query}&orderBy=createdTime%20desc&pageSize=200&fields=files(id,name,createdTime)&supportsAllDrives=true&includeItemsFromAllDrives=true"), ct);
        var list = await response.Content.ReadFromJsonAsync<FileList>(cancellationToken: ct);
        return (list?.Files ?? []).Select(x => new DriveFile(x.Id, x.Name, DateTime.Parse(x.CreatedTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))).ToList();
    }

    // ---- Transporte y autenticación ----

    private sealed record IdResponse(string Id);
    private sealed record FileItem(string Id, string Name, string CreatedTime);
    private sealed record FileList(List<FileItem>? Files);
    private sealed record TokenResponse(string Access_Token, int Expires_In);

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool allowNotFound = false)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException ex) { throw new DriveException("No se pudo conectar con Google Drive.", ex); }
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { response.Dispose(); InvalidateToken(); continue; } // el token pudo caducar
            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)) return response;
            throw await FailureAsync(response);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // La subida del contenido va a la dirección de sesión devuelta por Drive; no lleva encabezado de autorización propio.
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex) { throw new DriveException("No se pudo conectar con Google Drive.", ex); }
        if (response.IsSuccessStatusCode) return response;
        throw await FailureAsync(response);
    }

    private static async Task<DriveException> FailureAsync(HttpResponseMessage response)
    {
        var body = ""; try { body = await response.Content.ReadAsStringAsync(); } catch (Exception) { }
        var status = (int)response.StatusCode; response.Dispose();
        return new DriveException($"Google Drive respondió {status}: {(body.Length > 300 ? body[..300] : body)}");
    }

    private void InvalidateToken() { accessToken = null; }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (accessToken is not null && clock() < accessTokenExpires) return accessToken;
            var now = clock(); var issued = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var unsigned = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" })) + "." +
                Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = options.ServiceAccountEmail, scope = Scope, aud = options.TokenUri, iat = issued, exp = issued + 3600 }));
            using var rsa = RSA.Create(); rsa.ImportFromPem(options.PrivateKeyPem);
            var assertion = unsigned + "." + Base64Url(rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUri)
            { Content = new FormUrlEncodedContent([new("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"), new("assertion", assertion)]) };
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, ct); }
            catch (HttpRequestException ex) { throw new DriveException("No se pudo conectar con Google para autenticar la cuenta de servicio.", ex); }
            using (response)
            {
                if (!response.IsSuccessStatusCode) throw new DriveException($"Google rechazó la cuenta de servicio ({(int)response.StatusCode}). Revisa la llave y que la API de Drive esté activada.");
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct) ?? throw new DriveException("Respuesta de autenticación vacía.");
                accessToken = token.Access_Token; accessTokenExpires = now.AddSeconds(Math.Max(60, token.Expires_In - 120));
                return accessToken;
            }
        }
        finally { tokenLock.Release(); }
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

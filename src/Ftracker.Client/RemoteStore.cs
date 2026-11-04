using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using Ftracker.Application;

namespace Ftracker.Client;

/// <summary>Sesión de la aplicación cuando los datos viven en el servidor.</summary>
public sealed class RemoteSession : ISession
{
    public string? UserId { get; private set; }
    public string? Role { get; private set; }
    public string? UserName { get; private set; }
    internal void SignIn(UsuarioSesionDto user) { UserId = user.Id; Role = user.Rol; UserName = user.Nombre; }
    internal void Clear() { UserId = null; Role = null; UserName = null; }
}

/// <summary>
/// Implementa el contrato de la aplicación contra la API del servidor de prefectura. Los errores del servidor llegan como
/// <see cref="ReglaNegocioException"/> con el mismo mensaje que daría la base local, así que la interfaz no cambia.
/// </summary>
public sealed class RemoteStore : IFtrackerStore, IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(30);

    private readonly HttpClient http;
    private readonly RemoteSession session = new();
    private string? token;

    /// <summary>Se dispara cuando el servidor rechaza la sesión (caducó, la cuenta se desactivó o se restauró un respaldo).</summary>
    public event Action? SessionExpired;
    public RemoteSession RemoteSession => session;
    ISession IFtrackerStore.Session => session;

    /// <param name="pinnedCertificateSha256">Huella SHA-256 del certificado del servidor. Con ella se acepta un certificado propio
    /// (autofirmado) y se rechaza cualquier otro; sin ella se exige un certificado válido de una entidad de confianza.</param>
    public RemoteStore(Uri baseAddress, string? pinnedCertificateSha256 = null, HttpMessageHandler? handler = null)
    {
        if (handler is null)
        {
            var native = new HttpClientHandler();
            var pin = Normalize(pinnedCertificateSha256);
            if (pin.Length > 0)
                native.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && string.Equals(Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)), pin, StringComparison.OrdinalIgnoreCase);
            handler = native;
        }
        http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => http.Dispose();
    private static string Normalize(string? pin) => new((pin ?? "").Where(Uri.IsHexDigit).ToArray());

    /// <summary>Comprueba que el servidor responda y hable la misma versión de la API.</summary>
    public async Task CheckServerAsync()
    {
        var info = await GetAsync<SaludDto>("salud", authenticated: false);
        if (info.App != "Ftracker") throw new ReglaNegocioException("La dirección no corresponde a un servidor de Ftracker.");
        if (info.Version != ApiInfo.Version)
            throw new ReglaNegocioException("Esta versión de la aplicación no es compatible con la del servidor. Actualiza Ftracker.");
    }

    // ---- Acceso ----
    public void SignOut()
    {
        var current = token; token = null; session.Clear();
        if (current is null) return;
        // Se avisa al servidor para invalidar el token, sin bloquear la interfaz; si falla (sin red), la sesión local ya está cerrada de todos modos.
        _ = Task.Run(async () =>
        {
            try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); using var request = Request(HttpMethod.Post, "auth/logout", current); using var response = await http.SendAsync(request, cts.Token); }
            catch (Exception) { }
        });
    }
    public async Task<UsuarioSesionDto?> GetCurrentUserAsync() => session.UserId is null ? null : await GetAsync<UsuarioSesionDto>("auth/yo");
    public async Task<bool> NeedsSetupAsync() => (await GetAsync<ConfiguracionInicialDto>("auth/configuracion-inicial", authenticated: false)).Requerida;
    public Task SetupAsync(string name, string email, string password) => SendAsync(HttpMethod.Post, "auth/configuracion-inicial", new SetupRequest(name, email, password), authenticated: false);
    public async Task LoginAsync(string email, string password)
    {
        SignOut();
        var response = await PostAsync<LoginResponse>("auth/login", new LoginRequest(email, password), authenticated: false);
        token = response.Token; session.SignIn(response.Usuario);
    }
    public Task ChangeOwnPasswordAsync(string current, string next) => SendAsync(HttpMethod.Post, "auth/cambiar-clave", new CambiarClaveRequest(current, next));

    // ---- Usuarios ----
    public async Task<IReadOnlyList<UsuarioDto>> GetUsersAsync() => await GetAsync<List<UsuarioDto>>("usuarios");
    public Task AddUserAsync(string name, string email, string password, string role) => SendAsync(HttpMethod.Post, "usuarios", new NuevoUsuarioRequest(name, email, password, role));
    public Task UpdateUserAsync(string id, string name, string email, string role) => SendAsync(HttpMethod.Put, $"usuarios/{Uri.EscapeDataString(id)}", new EditarUsuarioRequest(name, email, role));
    public Task ToggleUserAsync(string id) => SendAsync(HttpMethod.Post, $"usuarios/{Uri.EscapeDataString(id)}/alternar");
    public Task DeleteUserAsync(string id) => SendAsync(HttpMethod.Delete, $"usuarios/{Uri.EscapeDataString(id)}");
    public Task ResetPasswordAsync(string id, string next) => SendAsync(HttpMethod.Post, $"usuarios/{Uri.EscapeDataString(id)}/restablecer-clave", new CambiarClaveRequest(null, next));

    // ---- Rutas ----
    public async Task<IReadOnlyList<RutaDto>> GetRoutesAsync() => await GetAsync<List<RutaDto>>("rutas");
    public Task AddRouteAsync(string name, string description, string points) => SendAsync(HttpMethod.Post, "rutas", new NuevaRutaRequest(name, description, points));
    public Task ToggleRouteAsync(Guid id) => SendAsync(HttpMethod.Post, $"rutas/{id}/alternar");
    public Task DeleteRouteAsync(Guid id) => SendAsync(HttpMethod.Delete, $"rutas/{id}");
    public async Task<IReadOnlyList<PuntoCatalogo>> GetRoutePointsAsync(Guid rutaId) => await GetAsync<List<PuntoCatalogo>>($"rutas/{rutaId}/puntos");
    public Task AddPointAsync(Guid rutaId, string name) => SendAsync(HttpMethod.Post, $"rutas/{rutaId}/puntos", new NombreRequest(name));
    public Task RenamePointAsync(Guid pointId, string name) => SendAsync(HttpMethod.Put, $"puntos/{pointId}", new NombreRequest(name));
    public Task MovePointAsync(Guid pointId, int direction) => SendAsync(HttpMethod.Post, $"puntos/{pointId}/mover", new MoverPuntoRequest(direction));
    public Task SetRouteCodesAsync(Guid rutaId, bool enabled, bool regenerate = false) => SendAsync(HttpMethod.Post, $"rutas/{rutaId}/codigos", new CodigosRutaRequest(enabled, regenerate));
    public Task<bool> RequiresCodeAsync(Guid registroId) => GetAsync<bool>($"registros/{registroId}/requiere-codigo");
    public async Task<int> WriteCodeSheetAsync(Guid rutaId, string path)
    {
        var file = await DownloadAsync($"rutas/{rutaId}/hoja-codigos");
        await File.WriteAllBytesAsync(path, file.Contenido); return file.Filas;
    }
    public async Task<IReadOnlyList<CroquisPunto>> GetRouteCroquisAsync(Guid rutaId) => await GetAsync<List<CroquisPunto>>($"rutas/{rutaId}/croquis");
    public Task SaveRouteCroquisAsync(Guid rutaId, IReadOnlyList<CroquisPunto> points) => SendAsync(HttpMethod.Put, $"rutas/{rutaId}/croquis", points);
    public Task<CroquisRecorrido> GetRoundCroquisAsync(Guid rondinId) => GetAsync<CroquisRecorrido>($"rondines/{rondinId}/croquis");

    // ---- Rondines ----
    public async Task<IReadOnlyList<RondinDto>> GetRoundsAsync() => await GetAsync<List<RondinDto>>("rondines");
    public async Task<IReadOnlyList<RegistroPuntoDto>> GetRoundPointsAsync(Guid rondinId) => await GetAsync<List<RegistroPuntoDto>>($"rondines/{rondinId}/puntos");
    public Task CreateRoundAsync(CrearRondinCommand command) => SendAsync(HttpMethod.Post, "rondines", command);
    public Task StartRoundAsync(Guid rondinId) => SendAsync(HttpMethod.Post, $"rondines/{rondinId}/iniciar");
    public Task MarkPointAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command) => SendAsync(HttpMethod.Post, $"rondines/{rondinId}/puntos/{registroId}/marcar", command);
    public Task CloseRoundAsync(Guid rondinId, string? observaciones, FotoCierreCommand fotografia) => SendAsync(HttpMethod.Post, $"rondines/{rondinId}/cerrar", new CerrarRondinRequest(observaciones, fotografia), timeout: TimeSpan.FromMinutes(2));
    public async Task<FotoCierreInfo?> GetClosureEvidenceAsync(Guid roundId)
    {
        using var response = await SendRawAsync(HttpMethod.Get, $"rondines/{roundId}/hoja-firmas");
        return response.StatusCode == HttpStatusCode.NoContent ? null : await ReadAsync<FotoCierreInfo>(response); // 204: rondín sin hoja de firmas (cierre anterior a esta función)
    }
    public async Task<byte[]> ReadClosureEvidenceAsync(Guid roundId)
    {
        using var response = await SendRawAsync(HttpMethod.Get, $"rondines/{roundId}/hoja-firmas/contenido");
        return await response.Content.ReadAsByteArrayAsync();
    }
    public Task CancelRoundAsync(Guid rondinId, string motivo) => SendAsync(HttpMethod.Post, $"rondines/{rondinId}/cancelar", new CancelarRondinRequest(motivo));
    public Task ReassignRoundAsync(Guid rondinId, ReasignarRondinCommand command) => SendAsync(HttpMethod.Post, $"rondines/{rondinId}/reasignar", command);
    public Task DeleteRoundAsync(Guid id) => SendAsync(HttpMethod.Delete, $"rondines/{id}");

    // ---- Incidencias y evidencias ----
    public async Task<IReadOnlyList<IncidenciaDto>> GetIncidentsAsync() => await GetAsync<List<IncidenciaDto>>("incidencias");
    public async Task<Guid> CreateIncidentAsync(CrearIncidenciaCommand command) => (await PostAsync<IdResponse>("incidencias", command)).Id;
    public Task ChangeIncidentStateAsync(Guid id, CambiarIncidenciaCommand command) => SendAsync(HttpMethod.Post, $"incidencias/{id}/estado", command);
    public async Task<IReadOnlyList<EvidenciaDto>> ListEvidenceAsync(Guid incidentId) => await GetAsync<List<EvidenciaDto>>($"incidencias/{incidentId}/evidencias");
    public async Task<byte[]> ReadEvidenceAsync(Guid evidenceId)
    {
        using var response = await SendRawAsync(HttpMethod.Get, $"evidencias/{evidenceId}");
        return await response.Content.ReadAsByteArrayAsync();
    }
    public async Task SaveEvidenceAsync(Guid incidentId, string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || file.Length > 5 * 1024 * 1024) throw new ReglaNegocioException("El archivo está vacío o supera el límite de 5 MB.");
        using var content = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await SendRawAsync(HttpMethod.Post, $"incidencias/{incidentId}/evidencias?nombre={Uri.EscapeDataString(file.Name)}", content);
    }

    // ---- Bitácora, reportes y respaldo ----
    public async Task<IReadOnlyList<AuditoriaDto>> GetAuditAsync(int take = 200) => await GetAsync<List<AuditoriaDto>>($"bitacora?limite={take}");
    public async Task<int> ExportReportAsync(ReportKind kind, string path, DateTime? desde, DateTime? hasta, string? prefectoId)
    {
        var query = new List<string>();
        if (desde.HasValue) query.Add("desde=" + desde.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (hasta.HasValue) query.Add("hasta=" + hasta.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(prefectoId)) query.Add("prefectoId=" + Uri.EscapeDataString(prefectoId));
        var file = await DownloadAsync($"reportes/{kind.ToString().ToLowerInvariant()}" + (query.Count > 0 ? "?" + string.Join("&", query) : ""));
        await File.WriteAllBytesAsync(path, file.Contenido); return file.Filas;
    }
    public async Task CreateBackupAsync(string zipPath)
    {
        using var response = await SendRawAsync(HttpMethod.Get, "respaldo", timeout: TransferTimeout, streamResponse: true); // el respaldo puede ser grande: se copia a disco sin cargarlo en memoria
        await using var output = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await response.Content.CopyToAsync(output);
    }
    public async Task RestoreBackupAsync(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new ReglaNegocioException("No se encontró el archivo de respaldo.");
        await using var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var content = new StreamContent(input);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await SendRawAsync(HttpMethod.Post, "respaldo/restaurar", content, TransferTimeout);
        // El servidor cerró todas las sesiones al restaurar.
        token = null; session.Clear();
    }

    // ---- Transporte ----

    private sealed record IdResponse(Guid Id);

    private HttpRequestMessage Request(HttpMethod method, string path, string? bearer, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, HttpContent? content = null, TimeSpan? timeout = null, bool authenticated = true, bool streamResponse = false)
    {
        using var cts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var request = Request(method, path, authenticated ? token : null, content);
        try
        {
            var response = await http.SendAsync(request, streamResponse ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cts.Token);
            if (response.IsSuccessStatusCode) return response;
            using (response) throw await ToExceptionAsync(response, authenticated);
        }
        catch (HttpRequestException)
        {
            throw new ReglaNegocioException("No se pudo conectar con el servidor de Ftracker. Verifica que estés en la red de prefectura y que el servidor esté encendido.");
        }
        catch (OperationCanceledException)
        {
            throw new ReglaNegocioException("El servidor tardó demasiado en responder. Inténtalo de nuevo.");
        }
    }

    private async Task<Exception> ToExceptionAsync(HttpResponseMessage response, bool authenticated)
    {
        string? message = null;
        try { message = (await response.Content.ReadFromJsonAsync<ApiError>(ApiJson.Options))?.Error; } catch (Exception) { }
        if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated && token is not null)
        {
            token = null; session.Clear(); SessionExpired?.Invoke();
        }
        return new ReglaNegocioException(string.IsNullOrWhiteSpace(message)
            ? $"El servidor respondió con un error inesperado ({(int)response.StatusCode}). Avisa al administrador."
            : message);
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null, bool authenticated = true, TimeSpan? timeout = null)
    {
        using var content = body is null ? null : JsonContent.Create(body, body.GetType(), options: ApiJson.Options);
        using var response = await SendRawAsync(method, path, content, timeout, authenticated);
    }

    private async Task<T> GetAsync<T>(string path, bool authenticated = true)
    {
        using var response = await SendRawAsync(HttpMethod.Get, path, authenticated: authenticated);
        return await ReadAsync<T>(response);
    }

    private async Task<T> PostAsync<T>(string path, object body, bool authenticated = true)
    {
        using var content = JsonContent.Create(body, body.GetType(), options: ApiJson.Options);
        using var response = await SendRawAsync(HttpMethod.Post, path, content, authenticated: authenticated);
        return await ReadAsync<T>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(ApiJson.Options) ?? throw new ReglaNegocioException("El servidor devolvió una respuesta vacía.");

    private async Task<ArchivoGenerado> DownloadAsync(string path)
    {
        using var response = await SendRawAsync(HttpMethod.Get, path);
        var rows = response.Headers.TryGetValues("X-Filas", out var values) && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0;
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "archivo";
        return new ArchivoGenerado(await response.Content.ReadAsByteArrayAsync(), rows, name);
    }
}

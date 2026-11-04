using System.Globalization;
using System.Text.Json;
using Ftracker.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ftracker.Api;

/// <summary>
/// Capa HTTP delgada sobre el contrato común (IServerStore): cada petición autenticada trabaja con su propio almacén y sesión,
/// y los permisos los siguen aplicando los mismos servicios que usa la aplicación local.
/// </summary>
public static class ApiEndpoints
{
    private const long MaxEvidenceBody = 6L * 1024 * 1024;
    private const long MaxClosureBody = 8L * 1024 * 1024;
    private const long MaxBackupBody = 2L * 1024 * 1024 * 1024;

    public static void Map(WebApplication app)
    {
        app.MapGet("/salud", () => Json(new SaludDto("Ftracker", ApiInfo.Version)));

        // ---- Acceso ----
        app.MapGet("/auth/configuracion-inicial", Anon(async (f, c) => Json(new ConfiguracionInicialDto(await f.CreateAnonymous().NeedsSetupAsync()))));
        app.MapPost("/auth/configuracion-inicial", Anon(async (f, c) =>
        {
            var request = await Body<SetupRequest>(c);
            var store = f.CreateAnonymous();
            if (!await store.NeedsSetupAsync()) throw new ReglaNegocioException("La configuración inicial ya se realizó.");
            await store.SetupAsync(request.Nombre, request.Email, request.Password);
            return Results.NoContent();
        }));
        app.MapPost("/auth/login", Anon(Login));
        app.MapPost("/auth/logout", Authed(async (s, c) => { c.RequestServices.GetRequiredService<TokenStore>().Revoke((string)c.Items["token"]!); return Results.NoContent(); }));
        app.MapGet("/auth/yo", Authed(async (s, c) => Json(await s.GetCurrentUserAsync() ?? throw new ReglaNegocioException("Inicia sesión para continuar."))));
        app.MapPost("/auth/cambiar-clave", Authed(async (s, c) =>
        {
            var r = await Body<CambiarClaveRequest>(c);
            await s.ChangeOwnPasswordAsync(r.Actual ?? "", r.Nueva); return Results.NoContent();
        }));

        // ---- Usuarios ----
        app.MapGet("/usuarios", Authed(async (s, c) => Json(await s.GetUsersAsync())));
        app.MapPost("/usuarios", Authed(async (s, c) => { var r = await Body<NuevoUsuarioRequest>(c); await s.AddUserAsync(r.Nombre, r.Email, r.Password, r.Rol); return Results.NoContent(); }));
        app.MapPut("/usuarios/{id}", Authed(async (s, c) => { var r = await Body<EditarUsuarioRequest>(c); await s.UpdateUserAsync(RouteText(c, "id"), r.Nombre, r.Email, r.Rol); return Results.NoContent(); }));
        app.MapPost("/usuarios/{id}/alternar", Authed(async (s, c) => { await s.ToggleUserAsync(RouteText(c, "id")); return Results.NoContent(); }));
        app.MapDelete("/usuarios/{id}", Authed(async (s, c) => { await s.DeleteUserAsync(RouteText(c, "id")); return Results.NoContent(); }));
        app.MapPost("/usuarios/{id}/restablecer-clave", Authed(async (s, c) =>
        {
            var id = RouteText(c, "id"); var r = await Body<CambiarClaveRequest>(c);
            await s.ResetPasswordAsync(id, r.Nueva);
            c.RequestServices.GetRequiredService<TokenStore>().RevokeUser(id); // la contraseña cambió: sus sesiones abiertas dejan de valer
            return Results.NoContent();
        }));

        // ---- Rutas, puntos y croquis ----
        app.MapGet("/rutas", Authed(async (s, c) => Json(await s.GetRoutesAsync())));
        app.MapPost("/rutas", Authed(async (s, c) => { var r = await Body<NuevaRutaRequest>(c); await s.AddRouteAsync(r.Nombre, r.Descripcion, r.Puntos); return Results.NoContent(); }));
        app.MapPost("/rutas/{id}/alternar", Authed(async (s, c) => { await s.ToggleRouteAsync(RouteGuid(c, "id")); return Results.NoContent(); }));
        app.MapDelete("/rutas/{id}", Authed(async (s, c) => { await s.DeleteRouteAsync(RouteGuid(c, "id")); return Results.NoContent(); }));
        app.MapGet("/rutas/{id}/puntos", Authed(async (s, c) => Json(await s.GetRoutePointsAsync(RouteGuid(c, "id")))));
        app.MapPost("/rutas/{id}/puntos", Authed(async (s, c) => { var r = await Body<NombreRequest>(c); await s.AddPointAsync(RouteGuid(c, "id"), r.Nombre); return Results.NoContent(); }));
        app.MapPut("/puntos/{id}", Authed(async (s, c) => { var r = await Body<NombreRequest>(c); await s.RenamePointAsync(RouteGuid(c, "id"), r.Nombre); return Results.NoContent(); }));
        app.MapPost("/puntos/{id}/mover", Authed(async (s, c) => { var r = await Body<MoverPuntoRequest>(c); await s.MovePointAsync(RouteGuid(c, "id"), r.Direccion); return Results.NoContent(); }));
        app.MapPost("/rutas/{id}/codigos", Authed(async (s, c) => { var r = await Body<CodigosRutaRequest>(c); await s.SetRouteCodesAsync(RouteGuid(c, "id"), r.Activar, r.Regenerar); return Results.NoContent(); }));
        app.MapGet("/rutas/{id}/hoja-codigos", Authed(async (s, c) => Download(c, await s.BuildCodeSheetAsync(RouteGuid(c, "id")), "text/html; charset=utf-8")));
        app.MapGet("/registros/{id}/requiere-codigo", Authed(async (s, c) => Json(await s.RequiresCodeAsync(RouteGuid(c, "id")))));
        app.MapGet("/rutas/{id}/croquis", Authed(async (s, c) => Json(await s.GetRouteCroquisAsync(RouteGuid(c, "id")))));
        app.MapPut("/rutas/{id}/croquis", Authed(async (s, c) => { var points = await Body<List<CroquisPunto>>(c); await s.SaveRouteCroquisAsync(RouteGuid(c, "id"), points); return Results.NoContent(); }));

        // ---- Rondines ----
        app.MapGet("/rondines", Authed(async (s, c) => Json(await s.GetRoundsAsync())));
        app.MapPost("/rondines", Authed(async (s, c) => { await s.CreateRoundAsync(await Body<CrearRondinCommand>(c)); return Results.NoContent(); }));
        app.MapDelete("/rondines/{id}", Authed(async (s, c) => { await s.DeleteRoundAsync(RouteGuid(c, "id")); return Results.NoContent(); }));
        app.MapGet("/rondines/{id}/puntos", Authed(async (s, c) => Json(await s.GetRoundPointsAsync(RouteGuid(c, "id")))));
        app.MapGet("/rondines/{id}/croquis", Authed(async (s, c) => Json(await s.GetRoundCroquisAsync(RouteGuid(c, "id")))));
        app.MapPost("/rondines/{id}/iniciar", Authed(async (s, c) => { await s.StartRoundAsync(RouteGuid(c, "id")); return Results.NoContent(); }));
        app.MapPost("/rondines/{id}/cerrar", Authed(async (s, c) =>
        {
            AllowBody(c, MaxClosureBody); // lleva la fotografía de la hoja de firmas (hasta 5 MB, ~6.7 MB en base64)
            var r = await Body<CerrarRondinRequest>(c);
            await s.CloseRoundAsync(RouteGuid(c, "id"), r.Observaciones, r.Fotografia ?? throw new ReglaNegocioException("Adjunta la fotografía de la hoja de firmas para cerrar el rondín."));
            return Results.NoContent();
        }));
        app.MapGet("/rondines/{id}/hoja-firmas", Authed(async (s, c) => await s.GetClosureEvidenceAsync(RouteGuid(c, "id")) is { } info ? Json(info) : Results.NoContent()));
        app.MapGet("/rondines/{id}/hoja-firmas/contenido", Authed(async (s, c) => Results.File(await s.ReadClosureEvidenceAsync(RouteGuid(c, "id")), "application/octet-stream")));
        app.MapPost("/rondines/{id}/cancelar", Authed(async (s, c) => { var r = await Body<CancelarRondinRequest>(c); await s.CancelRoundAsync(RouteGuid(c, "id"), r.Motivo); return Results.NoContent(); }));
        app.MapPost("/rondines/{id}/reasignar", Authed(async (s, c) => { await s.ReassignRoundAsync(RouteGuid(c, "id"), await Body<ReasignarRondinCommand>(c)); return Results.NoContent(); }));
        app.MapPost("/rondines/{id}/puntos/{registroId}/marcar", Authed(async (s, c) =>
        {
            await s.MarkPointAsync(RouteGuid(c, "id"), RouteGuid(c, "registroId"), await Body<MarcarPuntoCommand>(c)); return Results.NoContent();
        }));

        // ---- Incidencias y evidencias ----
        app.MapGet("/incidencias", Authed(async (s, c) => Json(await s.GetIncidentsAsync())));
        app.MapPost("/incidencias", Authed(async (s, c) => Json(new { id = await s.CreateIncidentAsync(await Body<CrearIncidenciaCommand>(c)) })));
        app.MapPost("/incidencias/{id}/estado", Authed(async (s, c) => { await s.ChangeIncidentStateAsync(RouteGuid(c, "id"), await Body<CambiarIncidenciaCommand>(c)); return Results.NoContent(); }));
        app.MapGet("/incidencias/{id}/evidencias", Authed(async (s, c) => Json(await s.ListEvidenceAsync(RouteGuid(c, "id")))));
        app.MapPost("/incidencias/{id}/evidencias", Authed(async (s, c) =>
        {
            AllowBody(c, MaxEvidenceBody);
            using var buffer = new MemoryStream();
            await c.Request.Body.CopyToAsync(buffer);
            await s.SaveEvidenceBytesAsync(RouteGuid(c, "id"), c.Request.Query["nombre"].ToString(), buffer.ToArray());
            return Results.NoContent();
        }));
        app.MapGet("/evidencias/{id}", Authed(async (s, c) => Results.File(await s.ReadEvidenceAsync(RouteGuid(c, "id")), "application/octet-stream")));

        // ---- Bitácora, reportes y respaldo ----
        app.MapGet("/bitacora", Authed(async (s, c) => Json(await s.GetAuditAsync(int.TryParse(c.Request.Query["limite"], out var take) ? take : 200))));
        app.MapGet("/reportes/{tipo}", Authed(async (s, c) =>
        {
            if (!Enum.TryParse<ReportKind>(RouteText(c, "tipo"), true, out var kind) || !Enum.IsDefined(kind)) throw new ReglaNegocioException("Tipo de reporte inválido.");
            var file = await s.BuildReportAsync(kind, QueryDate(c, "desde"), QueryDate(c, "hasta"), NullIfEmpty(c.Request.Query["prefectoId"].ToString()));
            return Download(c, file, "text/csv; charset=utf-8");
        }));
        app.MapGet("/respaldo", Authed(async (s, c) =>
        {
            var path = Path.Combine(Path.GetTempPath(), "Ftracker-api-" + Guid.NewGuid().ToString("N") + ".zip");
            try { await using var temp = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true); await s.WriteBackupToAsync(temp); }
            catch { try { File.Delete(path); } catch (IOException) { } throw; }
            // DeleteOnClose: el archivo temporal se borra solo cuando termina el envío.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            return Results.File(stream, "application/zip", $"Ftracker-respaldo-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        }));
        app.MapPost("/respaldo/restaurar", Authed(async (s, c) =>
        {
            AllowBody(c, MaxBackupBody);
            await s.RestoreBackupFromAsync(c.Request.Body);
            c.RequestServices.GetRequiredService<TokenStore>().Clear(); // la base cambió: todos deben iniciar sesión de nuevo
            return Results.NoContent();
        }));
    }

    private static async Task<IResult> Login(IStoreFactory factory, HttpContext c)
    {
        var request = await Body<LoginRequest>(c);
        var email = (request.Email ?? "").Trim();
        var password = request.Password ?? "";
        var tooLong = email.Length > 254 || password.Length > 256; // evita hashes costosos con entradas gigantes
        var throttle = c.RequestServices.GetRequiredService<LoginThrottle>();
        var ip = c.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var key = email.ToLowerInvariant();
        if (throttle.IsLocked(key, ip, out var wait))
            return Error($"Demasiados intentos. Espera {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minuto(s) e inténtalo de nuevo.", StatusCodes.Status429TooManyRequests);
        var store = factory.CreateAnonymous();
        try
        {
            if (tooLong) throw new ReglaNegocioException("Correo o contraseña incorrectos, o cuenta desactivada.");
            await store.LoginAsync(email, password);
        }
        catch (ReglaNegocioException)
        {
            var locked = throttle.RegisterFailure(key, ip);
            await factory.RecordSecurityEventAsync(locked ? "CuentaBloqueada" : "LoginFallido", $"{(key.Length > 100 ? key[..100] : key)} desde {ip}");
            throw;
        }
        throttle.Reset(key);
        var token = c.RequestServices.GetRequiredService<TokenStore>().Issue(store.Session.UserId!);
        return Json(new LoginResponse(token, (await store.GetCurrentUserAsync())!));
    }

    // ---- Ayudas ----

    private static IResult Json(object value, int status = StatusCodes.Status200OK) => Results.Json(value, ApiJson.Options, statusCode: status);
    private static IResult Error(string message, int status) => Json(new ApiError(message), status);
    private static IResult Unauthorized() => Error("Tu sesión expiró o no es válida. Inicia sesión de nuevo.", StatusCodes.Status401Unauthorized);

    private static IResult Download(HttpContext c, ArchivoGenerado file, string contentType)
    {
        c.Response.Headers["X-Filas"] = file.Filas.ToString(CultureInfo.InvariantCulture);
        return Results.File(file.Contenido, contentType, file.NombreSugerido);
    }

    private static async Task<T> Body<T>(HttpContext c) where T : class =>
        await c.Request.ReadFromJsonAsync<T>(ApiJson.Options) ?? throw new ReglaNegocioException("Solicitud inválida.");

    private static string RouteText(HttpContext c, string name)
    {
        var value = Convert.ToString(c.Request.RouteValues[name], CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) || value.Length > 64 ? throw new ReglaNegocioException("Identificador inválido.") : value;
    }
    private static Guid RouteGuid(HttpContext c, string name) => Guid.TryParse(RouteText(c, name), out var id) ? id : throw new ReglaNegocioException("Identificador inválido.");
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static DateTime? QueryDate(HttpContext c, string name)
    {
        var value = c.Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ReglaNegocioException("Fecha inválida.");
    }
    private static void AllowBody(HttpContext c, long max)
    {
        var feature = c.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = max;
    }

    private static Func<HttpContext, Task<IResult>> Anon(Func<IStoreFactory, HttpContext, Task<IResult>> action) =>
        c => Guard(c, () => action(c.RequestServices.GetRequiredService<IStoreFactory>(), c));

    private static Func<HttpContext, Task<IResult>> Authed(Func<IServerStore, HttpContext, Task<IResult>> action) =>
        c => Guard(c, async () =>
        {
            var tokens = c.RequestServices.GetRequiredService<TokenStore>();
            var header = c.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return Unauthorized();
            var token = header[7..].Trim();
            var userId = tokens.Touch(token);
            if (userId is null) return Unauthorized();
            // Se revalida contra la base en cada petición: un usuario desactivado o eliminado queda fuera al instante.
            var store = await c.RequestServices.GetRequiredService<IStoreFactory>().CreateForUserAsync(userId);
            if (store is null) { tokens.Revoke(token); return Unauthorized(); }
            c.Items["token"] = token;
            return await action(store, c);
        });

    private static async Task<IResult> Guard(HttpContext c, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ReglaNegocioException ex) { return Error(ex.Message, StatusCodes.Status400BadRequest); }
        catch (JsonException) { return Error("Solicitud inválida.", StatusCodes.Status400BadRequest); }
        catch (BadHttpRequestException) { return Error("Solicitud inválida o demasiado grande.", StatusCodes.Status400BadRequest); }
        catch (OperationCanceledException) { return Results.StatusCode(499); }
        catch (DriveException ex)
        {
            c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ftracker.Api").LogError(ex, "Error con Google Drive al atender {Metodo} {Ruta}", c.Request.Method, c.Request.Path.Value);
            return Error("No se pudo comunicar con Google Drive. Inténtalo de nuevo; si persiste, avisa al administrador.", StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
        { return Error("Los datos cambiaron en otro equipo. Actualiza e inténtalo de nuevo.", StatusCodes.Status409Conflict); }
        catch (Exception ex)
        {
            c.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ftracker.Api").LogError(ex, "Error al atender {Metodo} {Ruta}", c.Request.Method, c.Request.Path.Value);
            return Error("Ocurrió un error en el servidor. Inténtalo de nuevo o avisa al administrador.", StatusCodes.Status500InternalServerError);
        }
    }
}

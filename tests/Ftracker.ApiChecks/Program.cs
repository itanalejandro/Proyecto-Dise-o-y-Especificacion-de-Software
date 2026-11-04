using System.Net;
using Ftracker.Api;
using Ftracker.ApiChecks;
using Ftracker.Application;
using Ftracker.Client;
using Ftracker.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

// Pruebas de punta a punta de la capa HTTP: servidor Kestrel real + RemoteStore real + almacén falso en memoria.
var failures = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "OK    " : "FALLA ") + name); if (!ok) failures++; }
async Task<string?> Reject(Func<Task> action) { try { await action(); return null; } catch (ReglaNegocioException ex) { return ex.Message; } }

var world = new World();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1_000_000); // igual que el servidor real: lo grande solo pasa con el permiso de cada endpoint
var clock = new Clock();
builder.Services.AddSingleton<IStoreFactory>(new FakeFactory(world));
builder.Services.AddSingleton(new TokenStore(TimeSpan.FromMinutes(30), TimeSpan.FromHours(8), () => clock.Now));
builder.Services.AddSingleton(new LoginThrottle(clock: () => clock.Now));
builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
var app = builder.Build();
ApiEndpoints.Map(app);
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
using var client = new RemoteStore(new Uri(address));
var expired = 0; client.SessionExpired += () => expired++;

// --- Servidor y sesión ---
await client.CheckServerAsync(); Check(true, "Comprueba servidor y versión de la API");
Check(await client.NeedsSetupAsync() == false, "Consulta de configuración inicial");
Check(await Reject(() => client.GetUsersAsync()) is { } m1 && m1.Contains("sesión"), "Sin iniciar sesión, la API rechaza (401) con mensaje claro");
Check(await Reject(() => client.LoginAsync("ana@example.com", "mal")) is { } m2 && m2.Contains("incorrectos"), "Login incorrecto devuelve el mensaje del servidor");
await client.LoginAsync("ana@example.com", "Clave-Correcta-123");
Check(client.RemoteSession.UserId == "u-admin" && client.RemoteSession.Role == Roles.Administrador && client.RemoteSession.UserName == "Ana Admin", "Login correcto fija la sesión remota");
Check((await client.GetUsersAsync()).Count == 2, "Lista de usuarios por HTTP");

// --- Contratos: fechas, enums, errores de negocio ---
var rounds = await client.GetRoundsAsync();
Check(rounds.Count == 1 && rounds[0].ProgramadoPara.Kind == DateTimeKind.Utc && rounds[0].Estado == RondinEstado.Programado && rounds[0].ProgramadoPara == world.RoundDate, "Fechas en UTC y enumeraciones se conservan");
Check(await Reject(() => client.CreateRoundAsync(new(Guid.Empty, "u-pref", DateTime.UtcNow, null))) == "La ruta no existe.", "Un error de negocio llega con su mensaje original");
await client.MarkPointAsync(world.RoundId, world.PointId, new(RegistroResultado.Omitido, "Puerta cerrada", "ab-12"));
Check(world.LastMark is { Resultado: RegistroResultado.Omitido, Observaciones: "Puerta cerrada", Codigo: "ab-12" }, "El comando de marcar punto viaja completo");
Check((await client.CreateIncidentAsync(new(world.RoundId, null, "Fuga", IncidenciaPrioridad.Alta, "Agua"))) == world.IncidentId, "Crear incidencia devuelve el id");

// --- Cierre con hoja de firmas (la foto supera el límite de 1 MB por defecto: solo pasa por el permiso del endpoint) ---
var sheet = Enumerable.Range(0, 4_800_000).Select(i => (byte)(i * 31 % 253)).ToArray();
Check(await client.GetClosureEvidenceAsync(world.RoundId) is null, "Sin hoja de firmas, la consulta devuelve nada (204) y no un error");
await client.CloseRoundAsync(world.RoundId, "Sin novedades", new FotoCierreCommand("firmas.jpg", sheet));
Check(world.LastClose is { Observaciones: "Sin novedades" } closed && closed.Foto.NombreArchivo == "firmas.jpg" && closed.Foto.Contenido.SequenceEqual(sheet), "Cerrar con una foto de ~5 MB llega completo (base64 dentro del JSON)");
var sheetInfo = await client.GetClosureEvidenceAsync(world.RoundId);
Check(sheetInfo is { NombreOriginal: "firmas.jpg", Autor: "Pablo" } && (await client.ReadClosureEvidenceAsync(world.RoundId)).SequenceEqual(sheet), "Consultar la hoja de firmas: datos y contenido idénticos");

// --- Archivos ---
var temp = Path.Combine(Path.GetTempPath(), "ftracker-apichecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
var photo = Path.Combine(temp, "foto.jpg"); var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray(); await File.WriteAllBytesAsync(photo, bytes);
await client.SaveEvidenceAsync(world.IncidentId, photo);
Check(world.UploadedName == "foto.jpg" && world.Uploaded!.SequenceEqual(bytes), "Subir evidencia conserva nombre y bytes");
Check((await client.ReadEvidenceAsync(Guid.Empty)).SequenceEqual(world.Uploaded!), "Descargar evidencia conserva los bytes");
var bigPhoto = Path.Combine(temp, "grande.png"); var big = Enumerable.Range(0, 4_900_000).Select(i => (byte)(i % 249)).ToArray(); await File.WriteAllBytesAsync(bigPhoto, big);
await client.SaveEvidenceAsync(world.IncidentId, bigPhoto);
Check(world.UploadedName == "grande.png" && world.Uploaded!.SequenceEqual(big), "Una evidencia de ~4.9 MB sube completa aunque el límite general sea 1 MB");
await File.WriteAllBytesAsync(photo, new byte[6 * 1024 * 1024]);
Check(await Reject(() => client.SaveEvidenceAsync(world.IncidentId, photo)) is not null, "Una evidencia de más de 5 MB se rechaza antes de enviarla");
var csv = Path.Combine(temp, "reporte.csv");
Check(await client.ExportReportAsync(ReportKind.Incidencias, csv, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "u-pref") == 7 && File.ReadAllText(csv) == "id,total\n1,2\n", "Reporte CSV: filas, contenido y filtros viajan bien");
Check(world.LastReport == (ReportKind.Incidencias, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), "u-pref"), "Los filtros del reporte llegan al servidor");
var zip = Path.Combine(temp, "respaldo.zip");
await client.CreateBackupAsync(zip);
Check((await File.ReadAllBytesAsync(zip)).SequenceEqual(world.BackupBytes), "Descargar respaldo conserva el archivo");
await client.RestoreBackupAsync(zip);
Check(world.Restored!.SequenceEqual(world.BackupBytes) && client.RemoteSession.UserId is null, "Restaurar sube el archivo y cierra la sesión local");
Check(await Reject(() => client.GetUsersAsync()) is not null && expired >= 0, "Tras restaurar, el token anterior deja de valer");

// --- Seguridad de sesión ---
await client.LoginAsync("ana@example.com", "Clave-Correcta-123"); expired = 0;
world.Deactivated.Add("u-admin");
Check(await Reject(() => client.GetUsersAsync()) is { } m3 && m3.Contains("sesión") && expired == 1, "Un usuario desactivado queda fuera de inmediato (y se avisa a la app)");
world.Deactivated.Clear();
await client.LoginAsync("ana@example.com", "Clave-Correcta-123");
clock.Now = clock.Now.AddMinutes(31);
Check(await Reject(() => client.GetUsersAsync()) is { } m4 && m4.Contains("sesión"), "La sesión caduca por inactividad");

// --- Bloqueo de intentos ---
for (var i = 0; i < 5; i++) await Reject(() => client.LoginAsync("victima@example.com", "mal-" + i));
var locked = await Reject(() => client.LoginAsync("victima@example.com", "Clave-Correcta-123"));
Check(locked is { } && locked.Contains("Demasiados intentos"), "Tras 5 fallos la cuenta se bloquea, aun con la clave correcta");
Check(world.SecurityEvents.Count(x => x.StartsWith("LoginFallido")) >= 4 && world.SecurityEvents.Any(x => x.StartsWith("CuentaBloqueada")), "Los fallos y el bloqueo quedan en la bitácora de seguridad");
clock.Now = clock.Now.AddMinutes(6);
await client.LoginAsync("ana@example.com", "Clave-Correcta-123");
Check(client.RemoteSession.UserId == "u-admin", "El bloqueo se levanta pasado el tiempo");

// --- Servidor caído ---
await app.StopAsync();
Check(await Reject(() => client.GetRoundsAsync()) is { } m5 && m5.Contains("No se pudo conectar"), "Servidor apagado: mensaje claro en lugar de un error técnico");
Directory.Delete(temp, true);
await DriveChecks.RunAsync(Check);
Console.WriteLine(failures == 0 ? "\nPruebas de la API: correctas." : $"\nPruebas de la API: {failures} falla(s).");
return failures == 0 ? 0 : 1;

sealed class Clock { public DateTime Now { get; set; } = DateTime.UtcNow; }

sealed class World
{
    public Guid RoundId = Guid.NewGuid(), PointId = Guid.NewGuid(), IncidentId = Guid.NewGuid();
    public DateTime RoundDate = new(2026, 11, 2, 15, 30, 0, DateTimeKind.Utc);
    public (Guid Id, string? Observaciones, FotoCierreCommand Foto)? LastClose;
    public MarcarPuntoCommand? LastMark; public byte[]? Uploaded; public string? UploadedName;
    public (ReportKind, DateTime?, DateTime?, string?) LastReport;
    public byte[] BackupBytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7 % 256)).ToArray(); public byte[]? Restored;
    public HashSet<string> Deactivated = []; public List<string> SecurityEvents = [];
}

sealed class FakeSession(string? id, string? role) : ISession { public string? UserId { get; set; } = id; public string? Role { get; set; } = role; public string? UserName => "Fake"; }

sealed class FakeFactory(World world) : IStoreFactory
{
    public IServerStore CreateAnonymous() => new Fake(world, new FakeSession(null, null));
    public Task<IServerStore?> CreateForUserAsync(string userId) =>
        Task.FromResult<IServerStore?>(world.Deactivated.Contains(userId) ? null : new Fake(world, new FakeSession(userId, userId == "u-admin" ? Roles.Administrador : Roles.Prefecto)));
    public Task CreateBackupFileAsync(string zipPath) { File.WriteAllBytes(zipPath, world.BackupBytes); return Task.CompletedTask; }
    public Task RecordSecurityEventAsync(string action, string detail) { lock (world.SecurityEvents) world.SecurityEvents.Add(action + " " + detail); return Task.CompletedTask; }
}

sealed class Fake(World world, FakeSession session) : FakeBase
{
    public override ISession Session => session;
    public override Task<bool> NeedsSetupAsync() => Task.FromResult(false);
    public override Task LoginAsync(string email, string password)
    {
        if (email == "ana@example.com" && password == "Clave-Correcta-123") { session.UserId = "u-admin"; session.Role = Roles.Administrador; return Task.CompletedTask; }
        throw new ReglaNegocioException("Correo o contraseña incorrectos, o cuenta desactivada.");
    }
    public override Task<UsuarioSesionDto?> GetCurrentUserAsync() => Task.FromResult<UsuarioSesionDto?>(new("u-admin", "Ana Admin", "ana@example.com", Roles.Administrador));
    public override Task<IReadOnlyList<UsuarioDto>> GetUsersAsync() => Task.FromResult<IReadOnlyList<UsuarioDto>>([new("u-admin", "Ana Admin", "ana@example.com", Roles.Administrador, true), new("u-pref", "Pablo", "pablo@example.com", Roles.Prefecto, true)]);
    public override Task<IReadOnlyList<RondinDto>> GetRoundsAsync() => Task.FromResult<IReadOnlyList<RondinDto>>([new(world.RoundId, "Edificio A", "Pablo", world.RoundDate, RondinEstado.Programado, null, "u-pref", false)]);
    public override Task CreateRoundAsync(CrearRondinCommand c) => c.RutaId == Guid.Empty ? throw new ReglaNegocioException("La ruta no existe.") : Task.CompletedTask;
    public override Task CloseRoundAsync(Guid rondinId, string? observaciones, FotoCierreCommand fotografia) { world.LastClose = (rondinId, observaciones, fotografia); return Task.CompletedTask; }
    public override Task<FotoCierreInfo?> GetClosureEvidenceAsync(Guid roundId) =>
        Task.FromResult<FotoCierreInfo?>(roundId == world.RoundId && world.LastClose is { } c ? new(Guid.NewGuid(), c.Foto.NombreArchivo, "image/jpeg", DateTime.UtcNow, "Pablo", "abc123") : null);
    public override Task<byte[]> ReadClosureEvidenceAsync(Guid roundId) => Task.FromResult(world.LastClose!.Value.Foto.Contenido);
    public override Task MarkPointAsync(Guid rondinId, Guid registroId, MarcarPuntoCommand command) { world.LastMark = command; return Task.CompletedTask; }
    public override Task<Guid> CreateIncidentAsync(CrearIncidenciaCommand command) => Task.FromResult(world.IncidentId);
    public override Task SaveEvidenceBytesAsync(Guid incidentId, string fileName, byte[] content) { world.UploadedName = fileName; world.Uploaded = content; return Task.CompletedTask; }
    public override Task<byte[]> ReadEvidenceAsync(Guid evidenceId) => Task.FromResult(world.Uploaded!);
    public override Task<ArchivoGenerado> BuildReportAsync(ReportKind kind, DateTime? desde, DateTime? hasta, string? prefectoId)
    { world.LastReport = (kind, desde, hasta, prefectoId); return Task.FromResult(new ArchivoGenerado("id,total\n1,2\n"u8.ToArray(), 7, "reporte.csv")); }
    public override async Task WriteBackupToAsync(Stream destination) => await destination.WriteAsync(world.BackupBytes);
    public override async Task RestoreBackupFromAsync(Stream source) { using var buffer = new MemoryStream(); await source.CopyToAsync(buffer); world.Restored = buffer.ToArray(); }
}

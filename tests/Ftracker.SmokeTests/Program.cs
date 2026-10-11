using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

var directory = Path.Combine(Path.GetTempPath(), "Ftracker-tests-" + Guid.NewGuid());
var store = new LocalStore(directory);
await store.InitializeAsync();
Check(await store.NeedsSetupAsync(), "Configuración inicial");
await store.SetupAsync("Administrador", "admin@example.com", "PruebaLocal12345!");
await Reject(() => store.LoginAsync("admin@example.com", "incorrecta"), "Contraseña incorrecta");
await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
await store.AddUserAsync("Prefecto", "prefecto@example.com", "PrefectoLocal123!", Roles.Prefecto);
await store.AddRouteAsync("Edificio A", "Prueba", "Entrada\nPasillo");
Guid roundId; Guid pointId;
await using (var db = store.Open())
{
    var route = await db.Rutas.FirstAsync(); var prefect = await db.Users.SingleAsync(x => x.Rol == Roles.Prefecto);
    var round = await new RondinService(db, store.Session).CrearAsync(new(route.Id, prefect.Id, DateTime.UtcNow, null), store.Session.UserId!); roundId = round.Id;
}
store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CrearAsync(new(Guid.NewGuid(), "", DateTime.UtcNow, null), store.Session.UserId!); }, "Prefecto no puede asignar");
await using (var db = store.Open()) { var r = await new RondinService(db, store.Session).IniciarAsync(roundId, store.Session.UserId!); pointId = r.Registros.First().Id; }
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CerrarAsync(roundId, null, store.Session.UserId!); }, "Cierre con pendientes bloqueado");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(roundId, pointId, new(RegistroResultado.Omitido, ""), store.Session.UserId!); }, "Omisión sin motivo bloqueada");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(roundId, pointId, new((RegistroResultado)99, ""), store.Session.UserId!); }, "Enum inválido bloqueado");
Guid incidentId;
await using (var db = store.Open()) { var i = await new IncidenciaService(db, store.Session).CrearAsync(new(roundId, null, "Mantenimiento", IncidenciaPrioridad.Media, "Luz averiada"), store.Session.UserId!); incidentId = i.Id; }
var evidencePath = Path.Combine(directory, "prueba.pdf");
await File.WriteAllTextAsync(evidencePath, "%PDF-1.4\n%%EOF");
await store.SaveEvidenceAsync(incidentId, evidencePath);
await using (var db = store.Open()) { Check(await db.Evidencias.AnyAsync(x => x.IncidenciaId == incidentId), "Evidencia local persistida"); }
var invalidPath = Path.Combine(directory, "invalido.png");
await File.WriteAllTextAsync(invalidPath, "esto no es una imagen");
await Reject(() => store.SaveEvidenceAsync(incidentId, invalidPath), "Evidencia con cabecera inválida bloqueada");
await using (var db = store.Open())
{
    var list = await db.RegistrosPunto.Where(x => x.RondinId == roundId).ToListAsync(); var service = new RondinService(db, store.Session);
    foreach (var p in list) await service.MarcarPuntoAsync(roundId, p.Id, new(RegistroResultado.Completado, "Correcto"), store.Session.UserId!);
    await service.CerrarAsync(roundId, "Fin", store.Session.UserId!);
}
await using (var first = store.Open())
await using (var second = store.Open())
{
    var a = await first.Rutas.FirstAsync(); var b = await second.Rutas.FirstAsync(); a.Descripcion = "Edición A"; await first.SaveChangesAsync(); b.Descripcion = "Edición B";
    try { await second.SaveChangesAsync(); throw new Exception("No detectó concurrencia"); } catch (DbUpdateConcurrencyException) { Console.WriteLine("OK: concurrencia SQLite"); }
}
store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
await using (var db = store.Open()) { var svc = new IncidenciaService(db, store.Session); await svc.CambiarEstadoAsync(incidentId, new(IncidenciaEstado.EnRevision, "Revisando"), store.Session.UserId!); await svc.CambiarEstadoAsync(incidentId, new(IncidenciaEstado.Resuelta, "Reparado"), store.Session.UserId!); }
await using (var db = store.Open()) { Check(await db.Rondines.AnyAsync(x => x.Id == roundId && x.Estado == RondinEstado.Cerrado), "Rondín cerrado persistido"); Check(await db.Auditorias.CountAsync() >= 8, "Auditoría persistida"); var prefect = await db.Users.SingleAsync(x => x.Rol == Roles.Prefecto); await store.ToggleUserAsync(prefect.Id); }
await Reject(() => store.LoginAsync("prefecto@example.com", "PrefectoLocal123!"), "Usuario desactivado bloqueado");
Console.WriteLine("PRUEBAS COMPLETADAS. Base aislada: " + directory);
static void Check(bool value, string title) { if (!value) throw new Exception(title); Console.WriteLine("OK: " + title); }
static async Task Reject(Func<Task> action, string title) { try { await action(); } catch (ReglaNegocioException) { Console.WriteLine("OK: " + title); return; } throw new Exception("No bloqueó: " + title); }

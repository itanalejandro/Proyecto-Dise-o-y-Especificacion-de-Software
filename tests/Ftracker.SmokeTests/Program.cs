using Ftracker.Application;
using Ftracker.Domain;
using Ftracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

var directory = Path.Combine(Path.GetTempPath(), "Ftracker-tests-" + Guid.NewGuid());
var store = new LocalStore(directory);
await store.InitializeAsync();
Check(await store.NeedsSetupAsync(), "Configuración inicial");
await store.SetupAsync("Administrador", "admin@example.com", "PruebaLocal12345!");
// Simulate the pre-croquis beta inside this isolated test database only.
await using (var db = store.Open()) { await db.Database.ExecuteSqlRawAsync("DROP TABLE UbicacionesCroquis"); }
await store.InitializeAsync(); await store.InitializeAsync();
Check(!await store.NeedsSetupAsync(), "Actualización aditiva conserva usuarios y es repetible");
await Reject(() => store.LoginAsync("admin@example.com", "incorrecta"), "Contraseña incorrecta");
await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
await store.AddUserAsync("Prefecto", "prefecto@example.com", "PrefectoLocal123!", Roles.Prefecto);
await store.AddRouteAsync("Edificio A", "Prueba", "Entrada\nPasillo");
Guid roundId; Guid pointId; Guid routeId;
await using (var db = store.Open())
{
    var route = await db.Rutas.FirstAsync(); routeId = route.Id; var prefect = await db.Users.SingleAsync(x => x.Rol == Roles.Prefecto);
    var round = await new RondinService(db, store.Session).CrearAsync(new(route.Id, prefect.Id, DateTime.UtcNow, null), store.Session.UserId!); roundId = round.Id;
}
var originalMap = await store.GetRouteCroquisAsync(routeId);
Check(originalMap.All(x => x.X is null), "Puntos existentes sin ubicación no se inventan");
await Reject(() => store.SaveRouteCroquisAsync(routeId, originalMap.Select(x => x with { X = double.NaN, Y = 0.5 }).ToList()), "Coordenadas no finitas bloqueadas");
await Reject(() => store.SaveRouteCroquisAsync(routeId, originalMap.Select(x => x with { X = 1.1, Y = 0.5 }).ToList()), "Coordenadas fuera del mapa bloqueadas");
await Reject(() => store.SaveRouteCroquisAsync(routeId, originalMap.Select(x => x with { X = 0.4, Y = null }).ToList()), "Coordenadas incompletas bloqueadas");
await Reject(() => store.SaveRouteCroquisAsync(routeId, [originalMap[0], originalMap[0]]), "Puntos duplicados bloqueados");
await Reject(() => store.SaveRouteCroquisAsync(routeId, originalMap.Select(x => x with { Id = Guid.NewGuid() }).ToList()), "Puntos ajenos bloqueados");
await store.SaveRouteCroquisAsync(routeId, originalMap.Select((x, i) => i == 0 ? x with { X = 0.3, Y = 0.4 } : x).ToList());
try { await store.SaveRouteCroquisAsync(routeId, originalMap); throw new Exception("Sobrescribió mapa con versión antigua"); }
catch (DbUpdateConcurrencyException) { Console.WriteLine("OK: concurrencia de ubicaciones"); }
Check((await store.GetRoundCroquisAsync(roundId)).Puntos.First().X == 0.3, "Rondín programado muestra ubicación de ruta");
store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
await Reject(() => store.SaveRouteCroquisAsync(routeId, originalMap), "Prefecto no puede ubicar puntos");
await Reject(async () => { await store.GetRouteCroquisAsync(routeId); }, "Prefecto no puede consultar catálogo de croquis");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CrearAsync(new(Guid.NewGuid(), "", DateTime.UtcNow, null), store.Session.UserId!); }, "Prefecto no puede asignar");
await using (var db = store.Open()) { var r = await new RondinService(db, store.Session).IniciarAsync(roundId, store.Session.UserId!); pointId = r.Registros.First().Id; }
var startedMap = await store.GetRoundCroquisAsync(roundId);
Check(startedMap.PuedeRegistrar && startedMap.Puntos[0].X == 0.3 && startedMap.Puntos[1].X is null, "Inicio conserva ubicaciones y puntos sin ubicación");
store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
var editedMap = await store.GetRouteCroquisAsync(routeId);
await store.SaveRouteCroquisAsync(routeId, editedMap.Select(x => x with { X = 0.8, Y = 0.9 }).ToList());
Check((await store.GetRoundCroquisAsync(roundId)).Puntos[0].X == 0.3, "Editar ruta no mueve rondines iniciados");
var positionsToClear = await store.GetRouteCroquisAsync(routeId);
await store.SaveRouteCroquisAsync(routeId, positionsToClear.Select(x => x with { X = null, Y = null }).ToList());
Check((await store.GetRouteCroquisAsync(routeId)).All(x => x.X is null), "Quitar ubicación persiste");
Check((await store.GetRoundCroquisAsync(roundId)).Puntos[0].X == 0.3, "Quitar ubicación no altera copia histórica");
await store.AddUserAsync("Otro prefecto", "otro@example.com", "OtroPrefecto123!", Roles.Prefecto);
store.Session.SignOut(); await store.LoginAsync("otro@example.com", "OtroPrefecto123!");
await Reject(async () => { await store.GetRoundCroquisAsync(roundId); }, "No consulta croquis de otro prefecto");
store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CerrarAsync(roundId, null, store.Session.UserId!); }, "Cierre con pendientes bloqueado");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(roundId, pointId, new(RegistroResultado.Omitido, ""), store.Session.UserId!); }, "Omisión sin motivo bloqueada");
await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(roundId, pointId, new((RegistroResultado)99, ""), store.Session.UserId!); }, "Enum inválido bloqueado");
Guid incidentId;
await using (var db = store.Open()) { var i = await new IncidenciaService(db, store.Session).CrearAsync(new(roundId, null, "Mantenimiento", IncidenciaPrioridad.Media, "Luz averiada"), store.Session.UserId!); incidentId = i.Id; }
var evidencePath = Path.Combine(directory, "prueba.pdf");
await File.WriteAllTextAsync(evidencePath, "%PDF-1.4\n%%EOF");
await store.SaveEvidenceAsync(incidentId, evidencePath);
var attachments = await store.GetEvidenceAsync(incidentId);
var originalEvidenceBytes = await File.ReadAllBytesAsync(evidencePath);
var evidenceBytes = await store.ReadEvidenceAsync(attachments[0].Id);
Check(attachments.Count == 1 && evidenceBytes.SequenceEqual(originalEvidenceBytes), "Lectura de evidencia conserva bytes originales");
var originalLocation = attachments[0].RutaArchivo;
await using (var db = store.Open()) { (await db.Evidencias.SingleAsync(x => x.Id == attachments[0].Id)).RutaArchivo = evidencePath; await db.SaveChangesAsync(); }
await Reject(async () => { await store.ReadEvidenceAsync(attachments[0].Id); }, "No lee evidencia fuera de carpeta autorizada");
await using (var db = store.Open()) { (await db.Evidencias.SingleAsync(x => x.Id == attachments[0].Id)).RutaArchivo = originalLocation; await db.SaveChangesAsync(); }
File.Move(originalLocation, originalLocation + ".test-moved");
await Reject(async () => { await store.ReadEvidenceAsync(attachments[0].Id); }, "Archivo ausente informa error controlado");
File.Move(originalLocation + ".test-moved", originalLocation);
store.Session.SignOut();
await Reject(async () => { await store.GetEvidenceAsync(incidentId); }, "Sin sesión no consulta evidencias");
await Reject(async () => { await store.ReadEvidenceAsync(attachments[0].Id); }, "Sin sesión no lee evidencias");
await store.LoginAsync("otro@example.com", "OtroPrefecto123!");
await Reject(async () => { await store.GetEvidenceAsync(incidentId); }, "Otro prefecto no lista evidencias ajenas");
await Reject(async () => { await store.ReadEvidenceAsync(attachments[0].Id); }, "Otro prefecto no lee evidencias ajenas");
store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
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
var closedMap = await store.GetRoundCroquisAsync(roundId);
Check(!closedMap.PuedeRegistrar && closedMap.Puntos.All(x => x.Resultado == RegistroResultado.Completado), "Croquis cerrado es consulta y refleja resultados");
await using (var first = store.Open())
await using (var second = store.Open())
{
    var a = await first.Rutas.FirstAsync(); var b = await second.Rutas.FirstAsync(); a.Descripcion = "Edición A"; await first.SaveChangesAsync(); b.Descripcion = "Edición B";
    try { await second.SaveChangesAsync(); throw new Exception("No detectó concurrencia"); } catch (DbUpdateConcurrencyException) { Console.WriteLine("OK: concurrencia SQLite"); }
}
store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
await using (var db = store.Open()) { var svc = new IncidenciaService(db, store.Session); await svc.CambiarEstadoAsync(incidentId, new(IncidenciaEstado.EnRevision, "Revisando"), store.Session.UserId!); await svc.CambiarEstadoAsync(incidentId, new(IncidenciaEstado.Resuelta, "Reparado"), store.Session.UserId!); }
Check((await store.GetEvidenceAsync(incidentId)).Count == 1 && (await store.ReadEvidenceAsync(attachments[0].Id)).Length > 0, "Administrador consulta evidencia de rondín cerrado");
await CheckRoundManagement(store, routeId, roundId, evidencePath);
await CheckDeletion(store, evidencePath);
await using (var db = store.Open()) { Check(await db.Rondines.AnyAsync(x => x.Id == roundId && x.Estado == RondinEstado.Cerrado), "Rondín cerrado persistido"); Check(await db.Auditorias.CountAsync() >= 8, "Auditoría persistida"); var prefect = await db.Users.SingleAsync(x => x.Email == "prefecto@example.com"); await store.ToggleUserAsync(prefect.Id); }
await Reject(() => store.LoginAsync("prefecto@example.com", "PrefectoLocal123!"), "Usuario desactivado bloqueado");
Console.WriteLine("PRUEBAS COMPLETADAS. Base aislada: " + directory);
static void Check(bool value, string title) { if (!value) throw new Exception(title); Console.WriteLine("OK: " + title); }
static async Task Reject(Func<Task> action, string title) { try { await action(); } catch (ReglaNegocioException) { Console.WriteLine("OK: " + title); return; } throw new Exception("No bloqueó: " + title); }

static async Task CheckRoundManagement(LocalStore store, Guid routeId, Guid closedId, string evidencePath)
{
    string firstId; string secondId; Guid id;
    await using (var db = store.Open())
    {
        firstId = (await db.Users.SingleAsync(x => x.Email == "prefecto@example.com")).Id;
        secondId = (await db.Users.SingleAsync(x => x.Email == "otro@example.com")).Id;
        var service = new RondinService(db, store.Session);
        id = (await service.CrearAsync(new(routeId, firstId, DateTime.UtcNow, "Mantener observación inicial"), store.Session.UserId!)).Id;
    }
    async Task Cancel(Guid target, string reason)
    {
        await using var db = store.Open(); await new RondinService(db, store.Session).CancelarAsync(target, reason, store.Session.UserId!);
    }
    async Task Reassign(string prefect, string reason)
    {
        await using var db = store.Open(); await new RondinService(db, store.Session).ReasignarAsync(id, new(prefect, reason), store.Session.UserId!);
    }
    await Reject(() => Cancel(id, " "), "Cancelar exige motivo");
    await Reject(() => Cancel(closedId, "No debe cambiar"), "No cancela rondín cerrado");
    await Reject(() => Reassign(secondId, ""), "Reasignar exige motivo");
    await Reject(() => Reassign(firstId, "Sin cambio"), "Reasignar exige prefecto distinto");
    await Reject(() => Reassign(store.Session.UserId!, "Rol inválido"), "No reasigna a administrador");
    await store.ToggleUserAsync(secondId);
    await Reject(() => Reassign(secondId, "Ausente"), "No reasigna a prefecto desactivado");
    await store.ToggleUserAsync(secondId);
    await Reassign(secondId, "Cambio de turno");
    await using (var db = store.Open())
    {
        var r = await db.Rondines.SingleAsync(x => x.Id == id);
        Check(r.PrefectoId == secondId && r.Observaciones == "Mantener observación inicial", "Reasignación conserva ruta y observaciones");
        Check(await db.Auditorias.AnyAsync(x => x.EntidadId == id.ToString() && x.Accion == "Reasignar" && x.Detalle!.Contains("Cambio de turno")), "Reasignación audita motivo");
    }
    store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
    await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).IniciarAsync(id, store.Session.UserId!); }, "Prefecto anterior ya no inicia rondín");
    await Reject(() => Cancel(id, "No autorizado"), "Prefecto no puede cancelar");
    await Reject(() => Reassign(firstId, "No autorizado"), "Prefecto no puede reasignar");
    store.Session.SignOut(); await store.LoginAsync("otro@example.com", "OtroPrefecto123!");
    Guid recordId; Guid pointId;
    await using (var db = store.Open())
    {
        var r = await new RondinService(db, store.Session).IniciarAsync(id, store.Session.UserId!);
        recordId = r.Registros.First().Id; pointId = r.Registros.First().PuntoControlId;
    }
    await Reject(async () => { await using var db = store.Open(); await new IncidenciaService(db, store.Session).CrearAsync(new(id, Guid.NewGuid(), "Falla", IncidenciaPrioridad.Media, "Prueba"), store.Session.UserId!); }, "Incidencia rechaza punto ajeno");
    await Reject(async () => { await using var db = store.Open(); await new IncidenciaService(db, store.Session).CrearAsync(new(id, pointId, "Falla", IncidenciaPrioridad.Media, new string('x', 4001)), store.Session.UserId!); }, "Incidencia limita descripción");
    Guid incidentId;
    await using (var db = store.Open())
    {
        incidentId = (await new IncidenciaService(db, store.Session).CrearAsync(new(id, pointId, "Mantenimiento", IncidenciaPrioridad.Alta, "Puerta dañada"), store.Session.UserId!)).Id;
    }
    await store.SaveEvidenceAsync(incidentId, evidencePath);
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    await Reject(() => Reassign(firstId, "Ya empezó"), "No reasigna rondín iniciado");
    await Cancel(id, "Cierre del área por mantenimiento");
    await Reject(() => Cancel(id, "Otra vez"), "No cancela dos veces");
    await using (var db = store.Open())
    {
        var r = await db.Rondines.SingleAsync(x => x.Id == id);
        Check(r.Estado == RondinEstado.Cancelado && await db.RegistrosPunto.CountAsync(x => x.RondinId == id) == 2 &&
            await db.Incidencias.AnyAsync(x => x.Id == incidentId) && await db.Evidencias.AnyAsync(x => x.IncidenciaId == incidentId), "Cancelar conserva registros, incidencias y evidencias");
        Check(await db.Auditorias.AnyAsync(x => x.EntidadId == id.ToString() && x.Accion == "Cancelar" && x.Detalle!.Contains("mantenimiento")), "Cancelación audita motivo");
        await new IncidenciaService(db, store.Session).CambiarEstadoAsync(incidentId, new(IncidenciaEstado.Resuelta, "Área reparada"), store.Session.UserId!);
    }
    Check(!(await store.GetRoundCroquisAsync(id)).PuedeRegistrar, "Croquis cancelado es solo consulta");
    store.Session.SignOut(); await store.LoginAsync("otro@example.com", "OtroPrefecto123!");
    await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).MarcarPuntoAsync(id, recordId, new(RegistroResultado.Completado, ""), store.Session.UserId!); }, "No marca puntos cancelados");
    await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).CerrarAsync(id, null, store.Session.UserId!); }, "No cierra rondín cancelado");
    await Reject(async () => { await using var db = store.Open(); await new IncidenciaService(db, store.Session).CrearAsync(new(id, pointId, "Falla", IncidenciaPrioridad.Media, "Ya cancelado"), store.Session.UserId!); }, "No reporta sobre rondín cancelado");
    await Reject(() => store.SaveEvidenceAsync(incidentId, evidencePath), "No adjunta evidencia a rondín cancelado");
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    Guid scheduledId;
    await using (var db = store.Open()) { scheduledId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, firstId, DateTime.UtcNow, null), store.Session.UserId!)).Id; }
    await Cancel(scheduledId, "Actividad suspendida");
    store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
    await Reject(async () => { await using var db = store.Open(); await new RondinService(db, store.Session).IniciarAsync(scheduledId, store.Session.UserId!); }, "No inicia rondín programado cancelado");
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    Guid concurrentId; Guid concurrentRecord;
    await using (var db = store.Open()) { concurrentId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, firstId, DateTime.UtcNow, null), store.Session.UserId!)).Id; }
    store.Session.SignOut(); await store.LoginAsync("prefecto@example.com", "PrefectoLocal123!");
    await using (var db = store.Open()) { concurrentRecord = (await new RondinService(db, store.Session).IniciarAsync(concurrentId, store.Session.UserId!)).Registros.First().Id; }
    await using (var stale = store.Open())
    {
        _ = await stale.Rondines.SingleAsync(x => x.Id == concurrentId);
        await using (var db = store.Open()) { await new RondinService(db, store.Session).MarcarPuntoAsync(concurrentId, concurrentRecord, new(RegistroResultado.Completado, "Registro simultáneo"), store.Session.UserId!); }
        store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
        try { await new RondinService(stale, store.Session).CancelarAsync(concurrentId, "Vista anterior", store.Session.UserId!); throw new Exception("No detectó cambio concurrente del rondín"); }
        catch (DbUpdateConcurrencyException) { Console.WriteLine("OK: cancelación detecta registro concurrente"); }
    }
    await using (var db = store.Open())
    {
        Check(await db.Rondines.AnyAsync(x => x.Id == concurrentId && x.Estado == RondinEstado.EnCurso) &&
            !await db.Auditorias.AnyAsync(x => x.EntidadId == concurrentId.ToString() && x.Accion == "Cancelar"), "Conflicto no guarda cancelación ni auditoría parcial");
    }
}

static async Task CheckDeletion(LocalStore store, string evidencePath)
{
    // Se espera sesión de administrador.
    await store.AddUserAsync("Prefecto borrable", "borrable@example.com", "Borrable12345!", Roles.Prefecto);
    await store.AddRouteAsync("Ruta borrable", "Prueba de eliminación", "Uno\nDos");
    string userId; Guid routeId; Guid roundId; Guid incidentId;
    await using (var db = store.Open())
    {
        userId = (await db.Users.SingleAsync(x => x.Email == "borrable@example.com")).Id;
        routeId = (await db.Rutas.SingleAsync(x => x.Nombre == "Ruta borrable")).Id;
        roundId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, userId, DateTime.UtcNow, null), store.Session.UserId!)).Id;
    }
    await Reject(() => store.DeleteUserAsync(store.Session.UserId!), "No elimina la propia cuenta");
    await Reject(() => store.DeleteUserAsync(userId), "No elimina usuario con rondines asignados");
    await Reject(() => store.DeleteRouteAsync(routeId), "No elimina ruta usada por rondines");
    store.Session.SignOut(); await store.LoginAsync("borrable@example.com", "Borrable12345!");
    await Reject(() => store.DeleteRoundAsync(roundId), "Prefecto no puede eliminar rondines");
    await Reject(() => store.DeleteUserAsync(userId), "Prefecto no puede eliminar usuarios");
    await Reject(() => store.DeleteRouteAsync(routeId), "Prefecto no puede eliminar rutas");
    await using (var db = store.Open()) { await new RondinService(db, store.Session).IniciarAsync(roundId, store.Session.UserId!); }
    await using (var db = store.Open()) { incidentId = (await new IncidenciaService(db, store.Session).CrearAsync(new(roundId, null, "Mantenimiento", IncidenciaPrioridad.Baja, "Para eliminar"), store.Session.UserId!)).Id; }
    await store.SaveEvidenceAsync(incidentId, evidencePath);
    var evidenceFile = (await store.GetEvidenceAsync(incidentId))[0].RutaArchivo;
    Check(File.Exists(evidenceFile), "Evidencia de prueba guardada antes de eliminar");
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    await store.DeleteRoundAsync(roundId);
    await using (var db = store.Open())
    {
        Check(!await db.Rondines.AnyAsync(x => x.Id == roundId) && !await db.RegistrosPunto.AnyAsync(x => x.RondinId == roundId) &&
            !await db.Incidencias.AnyAsync(x => x.Id == incidentId) && !await db.Evidencias.AnyAsync(x => x.IncidenciaId == incidentId), "Eliminar rondín borra puntos, incidencias y evidencias");
        Check(await db.Auditorias.AnyAsync(x => x.EntidadId == roundId.ToString() && x.Accion == "EliminarRondin"), "Eliminar rondín queda en bitácora");
        Check(await db.Rutas.AnyAsync(x => x.Id == routeId) && await db.Users.AnyAsync(x => x.Id == userId), "Eliminar rondín conserva ruta y prefecto");
    }
    Check(!File.Exists(evidenceFile), "Eliminar rondín borra el archivo de evidencia");
    await Reject(() => store.DeleteRoundAsync(roundId), "No elimina dos veces el mismo rondín");
    await store.DeleteRouteAsync(routeId);
    await using (var db = store.Open()) { Check(!await db.Rutas.AnyAsync(x => x.Id == routeId) && !await db.PuntosControl.AnyAsync(x => x.RutaId == routeId), "Eliminar ruta borra sus puntos"); }
    await store.DeleteUserAsync(userId);
    await using (var db = store.Open()) { Check(!await db.Users.AnyAsync(x => x.Id == userId) && await db.Auditorias.AnyAsync(x => x.EntidadId == userId && x.Accion == "EliminarUsuario"), "Eliminar usuario persiste y audita"); }
    await Reject(() => store.LoginAsync("borrable@example.com", "Borrable12345!"), "Usuario eliminado no puede iniciar sesión");
    await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
}

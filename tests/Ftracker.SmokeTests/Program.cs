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
await CheckNewFeatures(store, directory);
await using (var db = store.Open()) { Check(await db.Rondines.AnyAsync(x => x.Id == roundId && x.Estado == RondinEstado.Cerrado), "Rondín cerrado persistido"); Check(await db.Auditorias.CountAsync() >= 8, "Auditoría persistida"); var prefect = await db.Users.SingleAsync(x => x.Email == "prefecto@example.com"); await store.ToggleUserAsync(prefect.Id); }
await Reject(() => store.LoginAsync("prefecto@example.com", "PrefectoLocal123!"), "Usuario desactivado bloqueado");
await CheckLoginAliases();
Console.WriteLine("PRUEBAS COMPLETADAS. Base aislada: " + directory);
static void Check(bool value, string title) { if (!value) throw new Exception(title); Console.WriteLine("OK: " + title); }
static async Task Reject(Func<Task> action, string title) { try { await action(); } catch (ReglaNegocioException) { Console.WriteLine("OK: " + title); return; } throw new Exception("No bloqueó: " + title); }

static async Task CheckLoginAliases()
{
    var local = new LocalStore(Path.Combine(Path.GetTempPath(), "Ftracker-login-" + Guid.NewGuid()));
    await local.InitializeAsync();
    await using (var db = local.Open()) Check(!await db.Users.AnyAsync(), "Inicialización no crea cuentas de soporte ni administradores ocultos");
    await local.SetupAsync("Administrador de alias", "admin.alias@example.com", "AdministradorPrueba123!");
    await local.LoginAsync("ADMIN.ALIAS@EXAMPLE.COM", "AdministradorPrueba123!");
    await local.AddUserAsync("Ángel Prueba", "angel@example.com", "PrimeraPrueba123!", Roles.Prefecto);
    await local.AddUserAsync("Guardia Compartido", "guardia1@example.com", "GuardiaUnoPrueba123!", Roles.Prefecto);
    await local.AddUserAsync("Guardia Compartido", "guardia2@example.com", "GuardiaDosPrueba123!", Roles.Prefecto);
    await local.AddUserAsync("angel@example.com", "alias.correo@example.com", "OtroCorreoPrueba123!", Roles.Prefecto);
    // This is an ordinary user-owned account, not a privileged or protected system identity.
    await local.AddUserAsync("soporte", "asistencia@example.com", "AsistenciaPropia123!", Roles.Prefecto);
    await local.InitializeAsync();
    await local.LoginAsync("  áNGEL PRUEBA  ", "PrimeraPrueba123!");
    Check(local.Session.User?.Email == "angel@example.com", "Nombre único admite mayúsculas y acentos sin depender de LOWER de SQLite");
    await local.LoginAsync("angel@example.com", "PrimeraPrueba123!");
    Check(local.Session.User?.NombreCompleto == "Ángel Prueba", "Correo único tiene prioridad frente a otro nombre coincidente");
    await Reject(() => local.LoginAsync("Guardia Compartido", "GuardiaUnoPrueba123!"), "Nombre duplicado exige correo sin fallar con excepción de consulta");
    Check(local.Session.UserId is null, "Nombre ambiguo no deja una sesión anterior activa");
    await local.LoginAsync("GUARDIA1@EXAMPLE.COM", "GuardiaUnoPrueba123!");
    Check(local.Session.User?.Email == "guardia1@example.com", "Primera cuenta con nombre repetido accede con su correo");
    await local.LoginAsync("guardia2@example.com", "GuardiaDosPrueba123!");
    Check(local.Session.User?.Email == "guardia2@example.com", "Segunda cuenta con nombre repetido accede con su correo");
    await Reject(() => local.LoginAsync("nombre inexistente", "PrimeraPrueba123!"), "Alias desconocido informa error controlado");
    await Reject(() => local.LoginAsync("Ángel Prueba", "incorrecta"), "Alias no evita comprobar la contraseña");
    await local.LoginAsync("soporte", "AsistenciaPropia123!");
    Check(local.Session.Role == Roles.Prefecto, "Nombre soporte no recibe privilegios especiales al inicializar");
    await local.ChangeOwnPasswordAsync("AsistenciaPropia123!", "AsistenciaNueva123!");
    await local.LoginAsync("admin.alias@example.com", "AdministradorPrueba123!");
    string id;
    await using (var db = local.Open()) id = (await db.Users.SingleAsync(x => x.Email == "asistencia@example.com")).Id;
    await local.ToggleUserAsync(id);
    await local.InitializeAsync();
    await Reject(() => local.LoginAsync("soporte", "AsistenciaNueva123!"), "Inicialización no reactiva ni restablece cuentas desactivadas");
}

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

static async Task CheckNewFeatures(LocalStore store, string directory)
{
    // Se espera sesión de administrador; "otro@example.com" es un prefecto activo creado antes.
    await store.AddRouteAsync("Ruta con códigos", "Prueba de puntos y códigos", "A\nB\nC");
    Guid routeId;
    await using (var db = store.Open()) routeId = (await db.Rutas.SingleAsync(x => x.Nombre == "Ruta con códigos")).Id;
    var points = await store.GetRoutePointsAsync(routeId);
    await store.RenamePointAsync(points[1].Id, "B2");
    await store.MovePointAsync(points[2].Id, -1);
    await store.MovePointAsync(points[0].Id, -1); // ya es el primero: no cambia nada
    Check((await store.GetRoutePointsAsync(routeId)).Select(x => x.Nombre + x.Orden).SequenceEqual(new[] { "A1", "C2", "B23" }), "Renombrar y reordenar puntos");
    await Reject(() => store.RenamePointAsync(points[0].Id, "   "), "Nombre de punto vacío bloqueado");
    await Reject(() => store.RenamePointAsync(points[0].Id, new string('x', 121)), "Nombre de punto demasiado largo bloqueado");

    // Verificación por código
    await store.SetRouteCodesAsync(routeId, true);
    await store.AddPointAsync(routeId, "D");
    points = await store.GetRoutePointsAsync(routeId);
    Check(points.Count == 4 && points.All(x => x.ConCodigo) && points[3].Nombre == "D", "Activar códigos cubre todos los puntos, incluso los nuevos");
    var codeSheet = Path.Combine(directory, "codigos.html");
    Check(await store.WriteCodeSheetAsync(routeId, codeSheet) == 4 && File.ReadAllText(codeSheet).Contains("data:image/png;base64,"), "Hoja de códigos con QR");
    Guid roundId;
    await using (var db = store.Open())
    {
        var prefect = await db.Users.SingleAsync(x => x.Email == "otro@example.com");
        roundId = (await new RondinService(db, store.Session).CrearAsync(new(routeId, prefect.Id, DateTime.UtcNow, null), store.Session.UserId!)).Id;
    }
    store.Session.SignOut(); await store.LoginAsync("otro@example.com", "OtroPrefecto123!");
    List<RegistroPunto> records;
    await using (var db = store.Open()) records = (await new RondinService(db, store.Session).IniciarAsync(roundId, store.Session.UserId!)).Registros.OrderBy(x => x.Orden).ToList();
    Check(await store.RequiresCodeAsync(records[0].Id), "El prefecto sabe que el punto exige código, sin ver el código");
    async Task Mark(int index, RegistroResultado result, string? note, string? code)
    {
        await using var db = store.Open();
        await new RondinService(db, store.Session).MarcarPuntoAsync(roundId, records[index].Id, new(result, note, code), store.Session.UserId!);
    }
    await Reject(() => Mark(0, RegistroResultado.Completado, null, null), "Completar sin código bloqueado");
    await Reject(() => Mark(0, RegistroResultado.Completado, null, "ZZZZ-ZZZZ"), "Código incorrecto bloqueado");
    await using (var db = store.Open()) Check(await db.Auditorias.AnyAsync(x => x.Accion == "CodigoPuntoIncorrecto" && x.EntidadId == records[0].Id.ToString()) &&
        (await db.RegistrosPunto.SingleAsync(x => x.Id == records[0].Id)).Resultado == RegistroResultado.Pendiente, "Código incorrecto se audita y no registra el punto");
    string expected;
    await using (var db = store.Open()) expected = (await db.PuntosControl.Where(x => x.Id == records[0].PuntoControlId).Select(x => x.CodigoVerificacion).SingleAsync())!;
    await Mark(0, RegistroResultado.Completado, null, expected.Replace("-", "").ToLowerInvariant());
    await Mark(1, RegistroResultado.Omitido, "Puerta cerrada", null);
    await using (var db = store.Open())
        Check((await db.RegistrosPunto.SingleAsync(x => x.Id == records[0].Id)).Verificado && !(await db.RegistrosPunto.SingleAsync(x => x.Id == records[1].Id)).Verificado, "Código correcto verifica el punto; omitir no lo exige");
    await using (var db = store.Open()) await new IncidenciaService(db, store.Session).CrearAsync(new(roundId, null, "Prueba", IncidenciaPrioridad.Alta, "=1+1"), store.Session.UserId!);
    await Reject(() => store.SetRouteCodesAsync(routeId, false), "Prefecto no administra códigos");
    await Reject(() => store.MovePointAsync(records[0].PuntoControlId, 1), "Prefecto no reordena puntos");

    // Reportes
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    var csv = Path.Combine(directory, "incidencias.csv");
    Check(await store.ExportReportAsync(ReportKind.Incidencias, csv, DateTime.Today.AddDays(-1), DateTime.Today, null) >= 1, "Exporta incidencias");
    var bytes = File.ReadAllBytes(csv);
    Check(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && File.ReadAllText(csv).Contains("\"'=1+1\""), "CSV con BOM y fórmulas neutralizadas");
    var detail = Path.Combine(directory, "puntos.csv");
    Check(await store.ExportReportAsync(ReportKind.Puntos, detail, null, null, null) >= 4 && File.ReadAllText(detail).Contains("\"Sí\""), "Exporta detalle de puntos con verificación");
    Check(await store.ExportReportAsync(ReportKind.Rondines, Path.Combine(directory, "rondines.csv"), null, null, null) >= 1, "Exporta rondines");
    Check(await store.ExportReportAsync(ReportKind.Rondines, Path.Combine(directory, "vacio.csv"), DateTime.Today.AddYears(-5), DateTime.Today.AddYears(-4), null) == 0, "Filtro de fechas sin resultados");
    await Reject(() => store.ExportReportAsync(ReportKind.Rondines, csv, DateTime.Today, DateTime.Today.AddDays(-3), null), "Rango de fechas invertido bloqueado");

    // Usuarios y contraseñas
    string otherId;
    await using (var db = store.Open()) otherId = (await db.Users.SingleAsync(x => x.Email == "otro@example.com")).Id;
    await Reject(() => store.UpdateUserAsync(otherId, "Otro", "prefecto@example.com", Roles.Prefecto), "Correo duplicado bloqueado al editar");
    await Reject(() => store.UpdateUserAsync(otherId, "", "otro@example.com", Roles.Prefecto), "Nombre vacío bloqueado al editar");
    await Reject(() => store.UpdateUserAsync(store.Session.UserId!, "Administrador", "admin@example.com", Roles.Prefecto), "No cambia su propio rol");
    await Reject(() => store.UpdateUserAsync(otherId, "Otro", "otro@example.com", Roles.Administrador), "No cambia el rol de un prefecto con rondín en curso");
    await store.UpdateUserAsync(otherId, "Otro prefecto editado", "OTRO@example.com", Roles.Prefecto);
    await using (var db = store.Open()) Check((await db.Users.SingleAsync(x => x.Id == otherId)).Email == "otro@example.com" && (await db.Users.SingleAsync(x => x.Id == otherId)).NombreCompleto == "Otro prefecto editado", "Editar usuario normaliza el correo");
    await Reject(() => store.ResetPasswordAsync(store.Session.UserId!, "NuevaClave12345!"), "Restablecer no aplica a la propia cuenta");
    await Reject(() => store.ResetPasswordAsync(otherId, "corta"), "Contraseña corta bloqueada");
    await store.ResetPasswordAsync(otherId, "ClaveRestablecida1!");
    await Reject(() => store.LoginAsync("otro@example.com", "OtroPrefecto123!"), "La contraseña anterior deja de funcionar");
    await store.LoginAsync("otro@example.com", "ClaveRestablecida1!");
    await Reject(() => store.ChangeOwnPasswordAsync("incorrecta", "OtraClave12345!"), "Cambio exige la contraseña actual");
    await Reject(() => store.ChangeOwnPasswordAsync("ClaveRestablecida1!", "corta"), "Cambio exige longitud mínima");
    await Reject(() => store.ResetPasswordAsync(otherId, "OtraClave12345!"), "Prefecto no restablece contraseñas");
    await store.ChangeOwnPasswordAsync("ClaveRestablecida1!", "ClavePropia12345!");
    store.Session.SignOut(); await store.LoginAsync("otro@example.com", "ClavePropia12345!");
    store.Session.SignOut(); await store.LoginAsync("admin@example.com", "PruebaLocal12345!");

    // Respaldo y restauración
    var zip = Path.Combine(directory, "respaldo.zip");
    await store.CreateBackupAsync(zip);
    using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        Check(archive.GetEntry("manifiesto.json") is not null && archive.GetEntry("ftracker-desktop.db") is not null && archive.Entries.Any(x => x.FullName.StartsWith("evidencias/")), "Respaldo incluye manifiesto, base y evidencias");
    await store.AddUserAsync("Usuario posterior", "posterior@example.com", "Posterior12345!", Roles.Prefecto);
    var fake = Path.Combine(directory, "falso.zip");
    using (var archive = System.IO.Compression.ZipFile.Open(fake, System.IO.Compression.ZipArchiveMode.Create)) archive.CreateEntry("hola.txt");
    await Reject(() => store.RestoreBackupAsync(fake), "Un .zip ajeno no se restaura");
    await Reject(() => store.RestoreBackupAsync(Path.Combine(directory, "no-existe.zip")), "Respaldo inexistente informa error controlado");
    await using (var db = store.Open()) Check(await db.Users.AnyAsync(x => x.Email == "posterior@example.com"), "Restaurar un archivo inválido no altera los datos");
    await store.RestoreBackupAsync(zip);
    Check(store.Session.User is null, "Restaurar cierra la sesión");
    await store.LoginAsync("admin@example.com", "PruebaLocal12345!");
    await using (var db = store.Open())
        Check(!await db.Users.AnyAsync(x => x.Email == "posterior@example.com") && await db.Users.AnyAsync(x => x.Email == "otro@example.com") &&
            await db.Auditorias.AnyAsync(x => x.Accion == "RestaurarRespaldo"), "Restaurar devuelve el estado del respaldo y lo audita");
    Check(Directory.EnumerateFiles(Path.Combine(directory, "respaldos"), "antes-de-restaurar-*.zip").Any(), "Restaurar guarda un respaldo de seguridad previo");
    await using (var db = store.Open()) Check((await store.ReadEvidenceAsync((await db.Evidencias.FirstAsync()).Id)).Length > 0, "Las evidencias siguen legibles tras restaurar");
}

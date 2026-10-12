# Ftracker · Aplicación de escritorio Windows

Sistema local de rondines e incidencias para FIME. Interfaz WPF con ventanas nativas: no utiliza navegador, localhost, servidor web, cookies ni conexión a internet durante su uso.

## Iniciar

Haz doble clic en **Iniciar-Ftracker.cmd** en esta carpeta. Busca primero `artifacts/Ftracker-actualizado/Ftracker.exe`, después `artifacts/Ftracker/Ftracker.exe` y, si no hay publicación, usa la compilación Debug. Al clonar por primera vez, ejecuta **Preparar-Ftracker.cmd** para restaurar paquetes y compilar: los ejecutables no se incluyen en Git.

En el primer inicio crea tu cuenta administradora: nombre, correo y contraseña de al menos 12 caracteres. No hay contraseñas predeterminadas. En inicios posteriores aparece la ventana de acceso.

## Probar el flujo completo

1. Entra como administrador. En **Usuarios**, crea un usuario con rol **Prefecto**.
2. En **Rutas y puntos**, crea una ruta y escribe sus puntos, uno por línea y en orden.
3. En **Rondines**, pulsa **Asignar rondín**, selecciona ruta, prefecto y fecha/hora local.
4. Cierra sesión y entra con el prefecto. Selecciona el rondín y pulsa **Iniciar seleccionado**.
5. Selecciona cada punto para completarlo u omitirlo con justificación.
6. Con el rondín en curso seleccionado, abre **Incidencias** para reportar. Si seleccionaste un punto, se vincula a ese punto; de lo contrario, al rondín general.
7. Selecciona la incidencia y pulsa **Adjuntar evidencia**: JPG, PNG o PDF; máximo 5 MB y cinco archivos.
8. Regresa al rondín y ciérralo cuando todos sus puntos estén registrados.
9. Entra como administrador, revisa las incidencias y cambia su estado: Abierta → EnRevision → Resuelta → Cerrada. También se permite Abierta → Resuelta.

### Reasignar o cancelar recorridos

En **Rondines**, el administrador puede **Reasignar** un recorrido programado a otro prefecto activo, indicando un motivo. No se permite reasignar recorridos iniciados, cerrados o cancelados. La asignación anterior y el motivo quedan en la bitácora.

**Cancelar rondín** permite detener uno programado o en curso. Exige un motivo, informa que la acción no se puede deshacer y conserva los puntos, incidencias y evidencias ya guardados. El prefecto deja de poder iniciar, registrar puntos, reportar incidencias o adjuntar evidencias a ese recorrido. El administrador puede seguir atendiendo sus incidencias. El motivo y el estado anterior se registran en la bitácora; no se sobrescriben las observaciones iniciales del rondín.

### Eliminar usuarios, rutas y rondines

Solo el administrador ve estos botones. Todos piden confirmación y la acción **no se puede deshacer**; cada eliminación queda en la bitácora.

- **Eliminar usuario** (pestaña Usuarios): no se puede eliminar la propia cuenta ni al último administrador activo. Si el usuario tiene rondines asignados se bloquea; elimina esos rondines primero o usa *Activar / desactivar* para conservar el historial.
- **Eliminar ruta** (pestaña Rutas y puntos): borra la ruta, sus puntos y sus ubicaciones en el croquis. Se bloquea si algún rondín la usa.
- **Eliminar rondín** (pestaña Rondines): borra el rondín con sus puntos registrados, incidencias y archivos de evidencia (también del disco). Para detener un rondín conservando todo, usa *Cancelar rondín*.

### Hora al asignar un rondín

Pulsa **Elegir en reloj** para abrir un reloj interactivo con la fecha y la hora seleccionadas a la derecha. Primero pulsa la hora o arrastra su manecilla; al soltar se pasa a los minutos, marcados de cinco en cinco. **Horas** y **Minutos** permiten cambiar de selección; también puedes tomar la punta de una manecilla para moverla.

Elige explícitamente **a. m.** o **p. m.**. Para minutos exactos usa **−1 min**, **+1 min** o las flechas del teclado sobre el reloj; H selecciona horas y M minutos. **Usar esta hora** confirma; **Cancelar** conserva la hora anterior. También puedes escribir **HH:mm** en formato de 24 horas directamente en el formulario. Debajo se muestra la fecha y su equivalente con a. m./p. m.; una hora inválida impide guardar. El reloj cambia la hora, no el día: la fecha se selecciona en el calendario.

### Revisar evidencias dentro de Ftracker

En **Incidencias**, selecciona un reporte con archivos adjuntos y pulsa **Ver evidencia**. La ventana permite elegir entre sus archivos y visualizar imágenes JPG/PNG con zoom, **Ajustar** y **100 %**. Las imágenes grandes usan una vista previa de hasta 2400 píxeles por lado mayor para limitar el consumo de memoria; 100 % se refiere al tamaño de esa vista previa. El original no se modifica ni se exporta automáticamente.

Los PDF muestran un aviso y permiten **Exportar archivo**; no tienen vista previa interna. **Exportar evidencia** continúa disponible desde la pantalla principal para guardar una copia de cualquier adjunto. No se abre navegador ni visor externo.

Solo el administrador y el prefecto del rondín pueden consultar sus evidencias, también después del cierre o cancelación. Si un archivo fue movido/eliminado o una imagen está dañada, el visor informa el problema sin cerrar la aplicación. Se muestran avisos para archivos vacíos, mayores de 5 MB o imágenes de dimensiones no compatibles; las vistas previas no mantienen bloqueado el original.

## Datos y almacenamiento

SQLite usa el componente nativo de Windows `winsqlite3.dll`. No necesita MySQL ni SQL Server.

La base se crea en `%LOCALAPPDATA%\Ftracker\ftracker-desktop.db`; las evidencias, en la subcarpeta `evidencias`. Los datos permanecen al cerrar y volver a abrir. Para respaldar, cierra todas las instancias y copia la carpeta completa. El archivo local no está cifrado: depende de los permisos de la cuenta Windows.

La versión de escritorio usa una base nueva; no importa automáticamente usuarios ni datos de la versión web. No se han eliminado bases antiguas.

## Desarrollo

Requiere Windows 10/11 y SDK .NET 10 (Visual Studio con herramientas de escritorio .NET o terminal). La restauración inicial descarga paquetes; después la aplicación funciona sin red.

```powershell
dotnet restore Ftracker.sln --configfile NuGet.config
dotnet build Ftracker.sln
dotnet run --project src/Ftracker.Desktop
```

## Compartir con el equipo por Git

Sí: cualquier compañero con acceso al repositorio puede clonar el proyecto, modificarlo, compilarlo y ejecutarlo en Windows. Para subir sus cambios también necesita permiso de escritura en el repositorio o trabajar mediante un fork y una solicitud de incorporación.

Cada integrante necesita Windows 10/11, Git y el **SDK de .NET 10** (el runtime por sí solo no permite compilar). Visual Studio es opcional; si lo usan, deben tener las herramientas de escritorio .NET y compatibilidad con .NET 10. `global.json` mantiene la solución en la familia del SDK 10.

Después de clonar y abrir la carpeta, puede hacer doble clic en **Preparar-Ftracker.cmd**: restaura paquetes, compila y abre la versión Debug recién construida. La primera restauración necesita internet. También puede usar los tres comandos de Desarrollo. Después de modificar código, usar `dotnet run` o Preparar para ejecutar los cambios; Iniciar puede abrir una publicación anterior si aún no se ha vuelto a publicar.

Subir a Git: `src` (incluidos los recursos del logo), `tests`, `scripts`, `Ftracker.sln`, `NuGet.config`, `global.json`, scripts CMD, `.gitignore`, `.gitattributes`, `.editorconfig` y documentación. `.gitignore` excluye binarios de compilación, paquetes descargados, bases y logs. No subir bases locales, evidencias reales, contraseñas ni configuración privada. Si alguno ya fue agregado a Git, ignorarlo no lo retira automáticamente del historial.

**Git comparte código, no datos de operación.** Cada integrante crea su administrador y tiene su propia base local. Los usuarios, rutas y rondines de una computadora no aparecen automáticamente en las otras. Para entregar la aplicación a alguien que solo la usará, compartir un ZIP de toda la carpeta publicada, por fuera del código fuente o como archivo de una entrega.

Para coordinar cambios, trabajar en una rama por tarea y revisar los cambios antes de incorporarlos a la rama principal. Esta entrega se prepara en `ftracker-beta-v2`.

## Comodidad de uso

### Croquis interactivo

1. Como administrador, selecciona una ruta en **Rutas y puntos** y pulsa **Ubicar en croquis**.
2. Selecciona un punto en la lista y haz clic en el mapa para colocarlo o moverlo. Repite con los demás y pulsa **Guardar ubicaciones**. **Quitar ubicación** deja un punto únicamente en la lista.
3. Como prefecto, selecciona un rondín en **Mis rondines** y pulsa **Ver croquis**. Los programados permiten consultar la ruta; para registrar resultados, primero inicia el rondín desde la pantalla principal.
4. En un rondín en curso, selecciona un marcador o un punto de la lista y pulsa **Completar punto** u **Omitir punto**. Omitir requiere una observación. También puedes pulsar **Reportar incidencia** para vincular una falla al punto seleccionado, aunque ya se haya completado. Las fotografías o PDF se adjuntan después desde **Incidencias → Adjuntar evidencia**. Azul significa pendiente, verde completado y ámbar omitido. Los rondines cerrados/cancelados y la vista de administrador son de consulta.

El zoom, las barras de desplazamiento y **Ajustar** permiten explorar el área. Los puntos sin ubicación siguen disponibles en la lista. Las ubicaciones se copian al iniciar un rondín: editar la ruta no altera los recorridos ya iniciados. Los rondines antiguos no tienen posiciones históricas y se muestran sin marcadores. No hay GPS y las líneas solo indican orden, no caminos reales.

El croquis se incluye en la aplicación y funciona sin internet. Al iniciar se añade de forma automática e idempotente la tabla de ubicaciones, sin borrar datos existentes. Conviene respaldar antes de actualizar. Véase `src/Ftracker.Desktop/Assets/CROQUIS.md` para detalles y limitaciones.

### Navegación y formularios

La navegación lateral agrupa rondines, incidencias y catálogos. El encabezado compacto muestra el logo, el usuario y las acciones de sesión, sin tarjetas de resumen. Las tablas tienen búsqueda por texto (sin distinguir mayúsculas ni acentos), selección de fila completa y mensajes cuando están vacías. Las acciones se habilitan según la selección y el estado del registro. La fecha de asignación se elige con calendario y la hora mediante el reloj interactivo o escritura en formato de 24 horas.

Los formularios muestran los errores sin cerrarse ni borrar lo escrito. Las desactivaciones y el cierre de sesión requieren confirmación. La configuración inicial solicita confirmar la contraseña. Tab permite moverse entre controles y Escape cancelar un formulario.

Para una distribución que incluya .NET y no requiera instalarlo en otro equipo Windows x64:

```powershell
dotnet publish src/Ftracker.Desktop -c Release -r win-x64 --self-contained true -o artifacts/Ftracker
```

Copia **toda** la carpeta `artifacts/Ftracker`, no solo el archivo EXE.

Pruebas de integración local:

```powershell
dotnet run --project tests/Ftracker.SmokeTests
```

Comprobaciones de interfaz WPF (usa datos temporales y no modifica las cuentas reales):

```powershell
dotnet run --project tests/Ftracker.DesktopChecks
```

## Arquitectura

- `Ftracker.Desktop`: WPF; ViewModel de datos y ventanas nativas de acceso/operación. Los eventos de UI coordinan los servicios.
- `Ftracker.Application`: contratos de casos de uso, roles y sesión.
- `Ftracker.Domain`: entidades y estados.
- `Ftracker.Infrastructure`: EF Core SQLite, autenticación PBKDF2, catálogos, auditoría, evidencias y servicios de rondines/incidencias.

Se retiró el código fuente del proyecto ASP.NET Core y se sustituyó por Desktop en la solución. Los PDF/HTML anteriores quedan sustituidos por la especificación de escritorio actualizada. Las carpetas de compilación web antiguas pueden permanecer como artefactos ignorados.

## Alcance y pendientes

Incluye alta y activación/desactivación de usuarios/rutas, puntos iniciales, croquis interactivo, asignación, reasignación, cancelación, ejecución, cierre, incidencias desde tabla y croquis, evidencias y bitácora. Aún faltan edición de perfiles y nombres/orden de puntos existentes, cambio/recuperación de contraseña, filtros avanzados y un instalador. La creación automática del esquema y la actualización aditiva del croquis sirven para esta beta; otros cambios futuros de estructura requerirán migraciones.

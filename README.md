# Ftracker · Aplicación de escritorio Windows

Sistema local de rondines e incidencias para FIME. Interfaz WPF con ventanas nativas: no utiliza navegador, localhost, servidor web, cookies ni conexión a internet durante su uso.

## Iniciar

Haz doble clic en **Iniciar-Ftracker.cmd** en esta carpeta. Busca primero `artifacts/Ftracker-actualizado/Ftracker.exe`, después `artifacts/Ftracker/Ftracker.exe` y, si no hay publicación, usa la compilación Debug. Al clonar por primera vez, ejecuta **Preparar-Ftracker.cmd** para restaurar paquetes y compilar: los ejecutables no se incluyen en Git.

En el primer inicio crea tu cuenta administradora: nombre, correo y contraseña de al menos 12 caracteres. No hay contraseñas predeterminadas. En inicios posteriores aparece la ventana de acceso.

En el acceso puedes escribir el **correo** o el **nombre completo**. El correo tiene prioridad y es la opción recomendada. Un nombre debe corresponder a una sola cuenta; si está repetido, el sistema pide usar el correo y no intenta adivinar el usuario por su contraseña. Los nombres no son identificadores únicos ni se añadió un campo de usuario nuevo.

No se crean cuentas especiales de soporte ni claves universales. La recuperación disponible sigue siendo el restablecimiento por otro administrador autorizado. Esta actualización no elimina ni modifica automáticamente cuentas que alguien haya creado en una base anterior: revisa esas cuentas con el responsable si utilizaste otra distribución.

## Preparar instalador y paquete portable

La integración de distribución se basa en el paquete v3 del equipo, sin importar sus binarios, cuenta fija de soporte, certificados ni lanzadores de desbloqueo. Requiere Windows y el SDK .NET 10. El generador es una aplicación de consola .NET (`tools/Ftracker.Packaging`), no un script PowerShell. Para el EXE también se necesita **Inno Setup 6.3 o posterior**, instalado por el responsable desde su proveedor oficial; el generador no lo instala automáticamente.

```powershell
# Pruebas funcionales + pruebas WPF + publicación + instalador + ZIP:
.\Generar-Instalador.cmd

# Solo publicación y ZIP, sin depender de Inno Setup:
.\Generar-Instalador.cmd --portable-only

# Para una nueva entrega, utiliza una versión distinta:
.\Generar-Instalador.cmd --version 3.0.0-beta.2

# Comprobaciones internas del empaquetador, sin generar entregas:
dotnet run --project tools/Ftracker.Packaging -- --self-test
```

La versión predeterminada del paquete es `3.0.0-beta.1`. El resultado queda en `dist/Ftracker-Setup-<versión>-win-x64.exe` y `dist/Ftracker-<versión>-win-x64-portable.zip`, con archivos `.sha256` para comprobar su contenido. Si falta el compilador, el modo completo termina con un error claro; `--portable-only` no genera un EXE. `--iscc` permite indicar un compilador existente. Se ejecutan ambos programas de pruebas antes de distribuir; `--skip-tests` los omite expresamente y muestra una advertencia. El generador no sobrescribe entregas existentes: usa otra versión. No borra directorios de publicación anteriores y conserva el staging bajo `artifacts/installer-build` para revisión. Antes de publicar el ZIP comprueba todos sus archivos contra el manifiesto; las sumas de comprobación no sustituyen una firma de confianza.

El instalador es **por cuenta de Windows**, sin pedir elevación: instala en `%LOCALAPPDATA%\Programs\Ftracker`, añade la entrada de desinstalación y el acceso del menú Inicio; el escritorio es opcional. Cierra Ftracker antes de actualizar o desinstalar: no se fuerza su cierre. Los datos permanecen en `%LOCALAPPDATA%\Ftracker`, fuera del programa, y la desinstalación no incluye una orden para borrarlos. Respaldar antes de actualizar sigue siendo necesario. No se ha validado una actualización de instalaciones antiguas de otros paquetes con configuraciones diferentes.

Para el portable, extrae **todo** el ZIP y abre `Iniciar-Ftracker.cmd` o `app/Ftracker.exe`. Incluye runtime .NET, instrucciones, versión y un manifiesto SHA-256; no incluye datos de operación. Cambiar de carpeta no traslada la base y no convierte los datos en una base portable. `dist` y los binarios no se incluyen en Git; comparte una entrega binaria solo después de probarla.

**Firma y seguridad:** los paquetes generados por este proceso no tienen firma digital de confianza. No se crean/importan certificados, no se retiran marcas de seguridad de Windows ni se prometen excepciones de Smart App Control. Si Windows bloquea una entrega, coordina una distribución firmada o una revisión autorizada de la política. El instalador no reemplaza esa validación.

El lanzador principal funciona sin ejecutar archivos `.ps1` y no modifica la política de ejecución. `scripts/Build-Installer.ps1` se conserva solo como envoltorio opcional de compatibilidad; no se debe usar si la política del equipo no lo permite. No contiene desbloqueos ni cambios de seguridad. La prueba de instalación, actualización y desinstalación en un equipo de destino sigue siendo un paso distinto de compilar los paquetes.

Verificación del 26/10/2026: Inno Setup 6.7.3 generó el instalador `3.0.0-beta.1` y el empaquetador generó el portable después de completar las pruebas funcionales y WPF. Se verificaron el manifiesto del ZIP y el rechazo de sobrescritura de entregas. No se instaló Ftracker sobre los datos reales durante esa verificación; la prueba de instalación, actualización y desinstalación en otro equipo sigue pendiente. El compilador Inno Setup se obtuvo del proveedor oficial con firma Authenticode válida; eso no firma automáticamente los paquetes de Ftracker.

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

### Consultar fecha y hora de cierre

Las tablas **Rondines** (administrador) y **Mis rondines** (prefecto) muestran **Cerrado el** junto a **Estado**, con la fecha y hora local del cierre en formato `dd/MM/yyyy HH:mm`. Usa el dato guardado al cerrar el recorrido, también para rondines anteriores que ya lo tengan registrado. Si no hay fecha de cierre, muestra **—**; cancelar no equivale a cerrar. La columna permite ordenar cronológicamente y la búsqueda incluye la fecha de cierre mostrada.

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

En **Mis rondines → Puntos del rondín seleccionado**, junto a la observación aparece el enlace **Evidencia** cuando existen incidencias con archivos adjuntos vinculadas a ese punto y a ese recorrido. Abre el mismo visor sin exportar; si hay varias incidencias con archivos, primero permite elegir cuál consultar. Los reportes generales sin punto se consultan desde **Incidencias**.

En **Incidencias**, selecciona un reporte con archivos adjuntos y pulsa **Ver evidencia**. La ventana permite elegir entre sus archivos y visualizar imágenes JPG/PNG con zoom, **Ajustar** y **100 %**. Las imágenes grandes usan una vista previa de hasta 2400 píxeles por lado mayor para limitar el consumo de memoria; 100 % se refiere al tamaño de esa vista previa. El original no se modifica ni se exporta automáticamente.

Los PDF muestran un aviso y permiten **Exportar archivo**; no tienen vista previa interna. **Exportar evidencia** continúa disponible desde la pantalla principal para guardar una copia de cualquier adjunto. No se abre navegador ni visor externo.

Solo el administrador y el prefecto del rondín pueden consultar sus evidencias, también después del cierre o cancelación. Si un archivo fue movido/eliminado o una imagen está dañada, el visor informa el problema sin cerrar la aplicación. Se muestran avisos para archivos vacíos, mayores de 5 MB o imágenes de dimensiones no compatibles; las vistas previas no mantienen bloqueado el original.

### Verificar la presencia en cada punto (código o QR)

Por defecto un prefecto puede marcar un punto sin estar ahí. Para evitarlo, el administrador puede exigir un código por punto:

1. En **Rutas y puntos**, selecciona la ruta y pulsa **Editar puntos → Activar códigos**. Cada punto recibe un código de 8 caracteres (por ejemplo `K7M2-9QXP`, sin letras ni números ambiguos).
2. Pulsa **Imprimir códigos (QR)**: se guarda una hoja HTML con el código y un QR por punto. Ábrela con el navegador, imprímela y colócala en cada punto. El QR contiene solo el código.
3. Al **Completar punto**, el prefecto escribe el código o lo escanea con un lector de QR USB (que funciona como teclado). Es obligatorio también al completar desde el croquis. **Omitir punto** no lo exige, porque ahí se justifica con una observación.
4. Un código incorrecto no registra el punto y queda en la bitácora (`CodigoPuntoIncorrecto`). Los puntos verificados se marcan en la columna **Código** y en el reporte de puntos.

**Regenerar códigos** invalida los impresos; **Desactivar códigos** vuelve a permitir completar sin verificar. Un punto agregado a una ruta con códigos recibe el suyo. El prefecto nunca ve los códigos en la aplicación. La cámara de un teléfono no se usa: el QR se lee con un lector USB o se escribe a mano.

### Editar usuarios, puntos y contraseñas

- **Usuarios → Editar usuario** cambia nombre, correo y rol. No se puede cambiar el propio rol, dejar la aplicación sin administrador activo ni cambiar el rol de un prefecto con rondines programados o en curso. **Restablecer contraseña** fija una contraseña nueva a otro usuario (mínimo 12 caracteres).
- **Cambiar contraseña** (encabezado, todos los usuarios) pide la contraseña actual.
- **Rutas y puntos → Editar puntos**: agregar un punto al final, renombrar y subir/bajar. Los cambios se aplican a los rondines que aún no inician; los iniciados conservan el nombre y el orden que tenían. Todo queda en la bitácora.

### Alertas de atrasos

La aplicación avisa con un banner rojo cuando un rondín programado lleva más de 15 minutos sin iniciarse (la columna **Alerta** muestra *Atrasado*). El administrador también ve cuántas incidencias de prioridad alta o crítica siguen sin resolver. Se reevalúa cada minuto con la hora actual; no envía correos ni notificaciones fuera de la aplicación.

### Reportes (CSV)

En **Reportes y respaldo**, el administrador exporta a CSV, que Excel abre directamente (UTF-8 con BOM, hora local), filtrando por fechas y prefecto:

- **Rondines**: un renglón por recorrido, con minutos de recorrido y conteo de puntos completados, omitidos, pendientes y verificados.
- **Puntos**: un renglón por punto registrado, con su resultado, hora y verificación por código.
- **Incidencias**: con punto, prioridad, estado y horas hasta resolver.

El texto libre que empieza con `=`, `+`, `-` o `@` se exporta con un apóstrofo inicial para que Excel no lo trate como fórmula. No genera archivos `.xlsx` ni PDF.

### Respaldo y restauración

**Crear respaldo** guarda un `.zip` con una copia consistente de la base de datos y todas las evidencias. **No está cifrado**: contiene usuarios (con contraseñas protegidas por hash), incidencias y evidencias; guárdalo en un lugar seguro.

**Restaurar respaldo** reemplaza *todos* los datos actuales. Antes guarda automáticamente una copia de lo actual en `%LOCALAPPDATA%\Ftracker\respaldos\antes-de-restaurar-*.zip`, valida el archivo (estructura, integridad y que haya un administrador activo) y cierra Ftracker al terminar; al abrirlo de nuevo se inicia sesión con una cuenta del respaldo. Las evidencias se resuelven por nombre dentro de la carpeta de la aplicación, así que un respaldo se puede restaurar en otro equipo.

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

Subir a Git: `src` (incluidos los recursos del logo), `tests`, `scripts`, `tools`, `installer`, `Ftracker.sln`, `NuGet.config`, `global.json`, scripts CMD, `.gitignore`, `.gitattributes`, `.editorconfig` y documentación. `.gitignore` excluye binarios de compilación, entregas `dist`, paquetes descargados, bases y logs. No subir bases locales, evidencias reales, contraseñas ni configuración privada. Si alguno ya fue agregado a Git, ignorarlo no lo retira automáticamente del historial.

**Git comparte código, no datos de operación.** Cada integrante crea su administrador y tiene su propia base local. Los usuarios, rutas y rondines de una computadora no aparecen automáticamente en las otras. Para entregar la aplicación a alguien que solo la usará, compartir un ZIP de toda la carpeta publicada, por fuera del código fuente o como archivo de una entrega.

Para coordinar cambios, trabajar en una rama por tarea y revisar los cambios antes de incorporarlos a la rama principal. Esta entrega se prepara en `ftracker-beta-v2`.

## Comodidad de uso

### Modo claro y oscuro

El interruptor redondeado **Claro / Oscuro** del encabezado alterna el tema sin cerrar la sesión ni perder datos. Incluye símbolos de sol/luna y una transición suave de recorrido corto. La preferencia se guarda por cuenta de Windows en `%LOCALAPPDATA%\Ftracker\tema.txt` y se recupera al iniciar. Cambian los colores de las ventanas, tablas y formularios; el croquis y las imágenes de evidencia conservan sus colores originales. El botón también se puede utilizar con Tab y Enter o Espacio.

### Croquis interactivo

1. Como administrador, selecciona una ruta en **Rutas y puntos** y pulsa **Ubicar en croquis**.
2. Selecciona un punto en la lista y haz clic en el mapa para colocarlo o moverlo. Repite con los demás y pulsa **Guardar ubicaciones**. **Quitar ubicación** deja un punto únicamente en la lista.
3. Como prefecto, selecciona un rondín en **Mis rondines** y pulsa **Ver croquis**. Los programados permiten consultar la ruta; para registrar resultados, primero inicia el rondín desde la pantalla principal.
4. En un rondín en curso, selecciona un marcador o un punto de la lista y pulsa **Completar punto** u **Omitir punto**. Omitir requiere una observación. También puedes pulsar **Reportar incidencia** para vincular una falla al punto seleccionado, aunque ya se haya completado. Las fotografías o PDF se adjuntan después desde **Incidencias → Adjuntar evidencia**. Azul significa pendiente, verde completado y ámbar omitido. Los rondines cerrados/cancelados y la vista de administrador son de consulta.

El zoom, las barras de desplazamiento y **Ajustar** permiten explorar el área. Los puntos sin ubicación siguen disponibles en la lista. Las ubicaciones se copian al iniciar un rondín: editar la ruta no altera los recorridos ya iniciados. Los rondines antiguos no tienen posiciones históricas y se muestran sin marcadores. No hay GPS y las líneas solo indican orden, no caminos reales.

El croquis se incluye en la aplicación y funciona sin internet. Al iniciar se añade de forma automática e idempotente la tabla de ubicaciones, sin borrar datos existentes. Conviene respaldar antes de actualizar. Véase `src/Ftracker.Desktop/Assets/CROQUIS.md` para detalles y limitaciones.

### Navegación y formularios

Para el administrador, el orden lateral es **Usuarios → Rutas y puntos → Rondines → Incidencias → Reportes y respaldo → Bitácora**. El prefecto conserva **Mis rondines → Incidencias**. Las opciones laterales y el panel principal tienen bordes redondeados.

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

Incluye alta, edición y activación/desactivación de usuarios, rutas con edición de puntos (agregar, renombrar, reordenar), verificación opcional por código/QR, croquis interactivo, asignación, reasignación, cancelación, ejecución, cierre, alertas de atraso, incidencias desde tabla y croquis, evidencias, reportes CSV, respaldo/restauración, cambio y restablecimiento de contraseña y bitácora.

Aún faltan: operación compartida entre varios equipos (cada PC tiene su propia base; haría falta un servidor o sincronización), reportes en Excel/PDF nativos, filtros avanzados en pantalla, eliminar o desactivar puntos individuales, bloqueo por intentos fallidos de acceso y cierre de sesión por inactividad, cifrado de la base y los respaldos, firma de distribución y validación del instalador en un equipo de destino. El proyecto incluye la receta de instalación Inno Setup y el empaquetado portable; generar el instalador EXE requiere el compilador. La creación automática del esquema y las actualizaciones aditivas de columnas sirven para esta beta; otros cambios futuros de estructura requerirán migraciones.

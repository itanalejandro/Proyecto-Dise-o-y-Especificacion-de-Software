# Servidor de Ftracker (API + SQLite + Google Drive)

Los datos viven en **un servidor de la red de prefectura**. Las PC de prefectos y de la ingeniera ya no guardan nada: la
aplicación le pide todo a la API por HTTPS. Solo la API abre la base SQLite y solo la API habla con Google Drive.

```
App (PC prefecto)  ─┐
App (PC prefecto)  ─┼──HTTPS──►  API (servicio de Windows) ──► SQLite (un solo archivo)
App (PC ingeniera) ─┘                    │
                                         └──► Google Drive: evidencias y respaldo diario
```

## Estado de esta entrega (léelo antes de usarlo)

| Pieza | Estado |
|---|---|
| Contrato común `IFtrackerStore`, API, cliente remoto | Hecho. Pruebas de punta a punta (38 comprobaciones) con servidor real y almacén falso. |
| Cliente de Drive, evidencias en Drive, respaldo diario | Hecho y probado contra un Google falso (firma RS256, subida, reintento, retención). **No probado contra Google real.** |
| Aplicación de escritorio usando el servidor | Hecho (todas las pantallas usan el contrato). **No se pudo compilar ni ejecutar WPF en el entorno donde se escribió**: compilen y prueben antes de usarlo. |
| Integración con la base SQLite real (`LocalStore` + EF Core) | Compila contra stubs; **no se ejecutó contra una base real.** Corran `SmokeTests`. |
| Scripts de instalación del servidor (`scripts\servidor`) | Escritos, **sin ejecutar nunca**. Ensáyenlos en una PC de prueba. |

## 1. Probar en desarrollo (sin HTTPS)

Abrir **PowerShell** en la carpeta del repositorio (se usa una carpeta de datos aparte para no mezclar con los datos locales de la app):

```
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/Ftracker.Api -- --urls http://localhost:5080 --Ftracker:DataDirectory=C:\FtrackerPrueba
```

(En `cmd` en lugar de la primera línea: `set ASPNETCORE_ENVIRONMENT=Development`. Importante: el `--` antes de `--urls` es necesario, y **no** funciona `dotnet run --environment Development`: sin la variable de entorno el servidor arranca en modo producción y exige el certificado.)

Dejar esa ventana abierta mientras se prueba. En la aplicación de escritorio: pantalla de acceso → **Usar un servidor de prefectura…** → `http://localhost:5080` (sin huella) → reiniciar Ftracker. La primera vez pedirá crear al administrador (la base del servidor está vacía).

Pruebas automáticas (no necesitan base de datos ni servidor):

```
dotnet run --project tests/Ftracker.ApiChecks      # capa HTTP y Drive
dotnet run --project tests/Ftracker.SmokeTests     # base de datos SQLite
```

## 2. Producción (Windows, red de prefectura)

1. **Publicar:** `dotnet publish src/Ftracker.Api -c Release -r win-x64 --self-contained -o C:\publicado\api`
2. **Instalar** (PowerShell como administrador, en el equipo servidor):
   ```
   powershell -ExecutionPolicy Bypass -File scripts\servidor\Instalar-Servidor.ps1 -PublishDir C:\publicado\api -DnsName NOMBRE-DEL-SERVIDOR -AllowedRemoteAddress 192.168.10.0/24
   ```
   Copia la API a `C:\Ftracker\Api`, crea un certificado propio, registra el servicio **Ftracker API** (inicio automático, se reinicia
   solo si falla), abre el puerto 8443 solo para esa red y muestra **la dirección y la huella SHA-256** que van en la aplicación.
   Los datos quedan en `C:\ProgramData\Ftracker`. El servicio corre como `LocalSystem` (necesita leer el certificado del equipo);
   endurecerlo con una cuenta dedicada es una mejora pendiente.
3. **Primer uso:** en una PC, **Usar un servidor de prefectura…** → dirección y huella → reiniciar → crear al administrador.
4. **Quitar:** `scripts\servidor\Quitar-Servidor.ps1` (no borra datos ni certificado).

El nombre (`-DnsName`) debe ser el que usarán las PC para llegar al servidor (nombre de equipo o IP fija). Si cambia, hay que generar otro certificado y volver a poner la huella.

**Migrar datos que ya existan en una PC:** *Reportes y respaldo → Crear respaldo* en esa PC (modo local) y, ya conectada al servidor, *Restaurar respaldo*. Reemplaza todo lo del servidor.

## 3. Google Drive (opcional): evidencias y respaldo diario

Sin configurar nada, las evidencias se guardan en el disco del servidor. Para usar Drive hace falta:

1. **Una unidad compartida** de Google Workspace (la crea TI o la ingeniera). Con una carpeta de "Mi unidad" personal **no funciona**: una cuenta de servicio no tiene espacio propio y las subidas fallarían por cuota.
2. **Un proyecto de Google Cloud** con la **API de Google Drive** activada, una **cuenta de servicio** y su **llave JSON**. (Para esto no debería hacer falta activar facturación; confírmenlo en la consola.) Guarden la llave en el servidor, por ejemplo `C:\ProgramData\Ftracker\drive-llave.json`, y restrinjan su acceso.
3. **Agregar el correo de la cuenta de servicio** (`...@...iam.gserviceaccount.com`) como miembro de la unidad compartida con rol *Colaborador* o *Administrador de contenido*. La cuenta solo verá lo que se le comparta.
4. **Crear dos carpetas** en la unidad (por ejemplo `Evidencias` y `Respaldos`) y copiar el **ID de cada carpeta** (lo que va después de `/folders/` en la dirección).
5. Completar la sección `Drive` en `C:\Ftracker\Api\appsettings.Production.json` (o con variables `Drive__...`) y reiniciar el servicio:
   ```json
   "Drive": { "ServiceAccountKeyFile": "C:\\ProgramData\\Ftracker\\drive-llave.json",
              "EvidenceFolderId": "ID-DE-LA-CARPETA-EVIDENCIAS",
              "BackupFolderId": "ID-DE-LA-CARPETA-RESPALDOS",
              "BackupTime": "02:00", "BackupsToKeep": 14 }
   ```

Qué hace:
- Las evidencias **nuevas** se suben a Drive con un nombre aleatorio; en la base solo queda una referencia `drive:ID`. Las que ya estaban en disco se siguen leyendo del disco. Borrar un rondín borra también sus archivos de Drive.
- Cada día a la hora indicada se sube un respaldo `.zip` de la base a la carpeta de respaldos y se conservan los más recientes (por defecto 14). Solo se borran archivos con el nombre `Ftracker-respaldo-*`.
- **El respaldo `.zip` no incluye las evidencias que están en Drive** (ya viven allí). Sí incluye las que sigan en el disco del servidor.
- La **hoja de firmas** que se adjunta al cerrar un rondín se guarda dentro de la base (no en Drive), así que va incluida en los respaldos. Cerrar un rondín por la API envía la foto en la misma petición (hasta 5 MB).
- Si Drive falla, la aplicación muestra "No se pudo comunicar con Google Drive" y el detalle técnico queda en el registro del servidor.

## 4. Seguridad

- Sesiones: token aleatorio opaco, guardado solo como hash en memoria; caduca a los 60 min sin uso o a las 12 h. Reiniciar el servidor cierra todas las sesiones.
- En cada petición se vuelve a leer al usuario en la base: si se desactiva o elimina, queda fuera de inmediato. Restablecer su contraseña cierra sus sesiones.
- 5 fallos seguidos de una cuenta (o 30 desde una misma PC) bloquean 5 minutos; los fallos y bloqueos quedan en la bitácora (`LoginFallido`, `CuentaBloqueada`). Limitación: alguien podría bloquear a propósito una cuenta ajena unos minutos.
- Solo HTTPS en producción. La aplicación acepta únicamente el certificado cuya huella SHA-256 se le configure (o uno válido de una entidad de confianza si no se pone huella).
- Los permisos los siguen aplicando los mismos servicios de siempre; la API no concede nada por su cuenta.
- La llave de la cuenta de servicio y el archivo `.db` son secretos: protéjanlos con permisos de carpeta y respaldo propio.

## 5. Limitaciones conocidas

- Sin conexión no se puede trabajar. No hay cola local con sincronización.
- Un solo servidor: si se apaga, nadie opera. Hace falta regulador (UPS) y probar la restauración de un respaldo.
- Restaurar un respaldo reemplaza la base con el servidor en marcha: hacerlo fuera de horario; todas las sesiones se cierran.
- La bitácora no es todavía a prueba de manipulación para quien administre el servidor (podría editar el `.db`). Un encadenado de hashes lo permitiría detectar.
- Drive usa una cuenta de servicio con unidad compartida. Usar un Drive personal con OAuth no está implementado.
- La configuración del servidor se guarda en `%LOCALAPPDATA%\Ftracker\servidor.json` de cada PC; no hay instalador de la app con la dirección preconfigurada.

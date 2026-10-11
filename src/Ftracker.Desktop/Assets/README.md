# Identidad de Ftracker

Logo: una F formada por un recorrido y puntos de control, en los azules de la interfaz.

- `ftracker-logo.png`: original con transparencia, utilizado en acceso y encabezado.
- `ftracker.ico`: icono Windows con tamaños 16, 24, 32, 48, 64, 128 y 256 píxeles.
- `logo-prompt.txt`: instrucciones de generación. Se utilizó la herramienta integrada de imágenes, no una API por CLI.

Ambos recursos se compilan dentro de la aplicación; no dependen de rutas del equipo del autor. Incluye esta carpeta en Git.

Para regenerar el ICO tras cambiar el PNG, ejecuta desde la raíz del repositorio:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-AppIcon.ps1
```

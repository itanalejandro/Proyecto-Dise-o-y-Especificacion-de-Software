# Croquis FIME

`croquis-fime-limpio-v1.png` es una edición de la imagen proporcionada por el usuario, realizada con la herramienta integrada de generación/edición de imágenes. Se retiraron los marcadores de colores y controles de navegación. Las instrucciones completas están en `croquis-prompt.txt`.

Revisar distribución, contornos y nombres con el área real antes de utilizarlo para ubicar puntos de rondín: una edición con IA puede introducir variaciones. Es un croquis orientativo, no un plano topográfico ni un sistema GPS.

La imagen se compila como recurso de WPF, sin archivos externos ni conexión a internet. Los marcadores son controles independientes y las posiciones se guardan como coordenadas proporcionales entre 0 y 1. El administrador las configura en **Rutas y puntos → Ubicar en croquis**. El prefecto consulta su rondín en **Mis rondines → Ver croquis** y puede completar u omitir puntos cuando está en curso.

Al iniciar el rondín, las ubicaciones se copian a sus registros para no alterar recorridos iniciados o históricos al editar la ruta. Los rondines iniciados antes de esta función conservan sus puntos sin ubicación; no se asignan posiciones inventadas. Los puntos sin ubicación permanecen operables en la lista.

La tabla independiente `UbicacionesCroquis` identifica posiciones por el ID del punto de catálogo o por el ID del registro de rondín (copia histórica). El inicio añade la tabla con `CREATE TABLE IF NOT EXISTS`, compatible con bases de la beta anterior. No se recrean ni modifican tablas existentes. La implementación utiliza actualización aditiva propia; todavía no hay un sistema general de migraciones EF.

El croquis base es fijo para FIME. No incluye GPS, cálculo de caminos, carga de otros mapas ni edición de edificios. Las líneas discontinuas solo indican el orden de los puntos.

Confirmar el permiso de uso del mapa original antes de distribuirlo públicamente; la edición no modifica sus derechos de origen.

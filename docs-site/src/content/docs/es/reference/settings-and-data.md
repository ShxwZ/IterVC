---
title: Ajustes y datos
description: Conozca qué almacena IterVC y dónde se guarda.
---

IterVC guarda los ajustes de usuario como JSON con formato en `%AppData%\IterVC\settings.json`.

El esquema de ajustes actual incluye:

- Identificadores del dispositivo de salida de referencia, el destino de enrutamiento y el micrófono.
- Estado de activación, amplificación, monitorización y valores de la puerta de ruido del micrófono.
- Ganancia compartida de aplicaciones.
- Nombres de los procesos incluidos.
- Idioma de la interfaz.
- Plantilla OSC y estado de activación.
- Consentimiento, caché y versión descartada de las comprobaciones de actualización.
- Asignaciones de atajos globales.
- Comportamiento al cerrar y al minimizar en la bandeja.
- Preferencias de inicio con Windows.
- Confirmaciones de notificaciones del primer inicio.

## Restablecimiento seguro

Para restablecer todas las preferencias:

1. Cierre IterVC por completo desde la bandeja del sistema.
2. Cambie el nombre de `settings.json` a `settings.backup.json`.
3. Inicie IterVC.

IterVC crea los ajustes predeterminados si el archivo no existe o no se puede cargar. Conserve la copia de seguridad hasta confirmar que el restablecimiento solucionó el problema.

## Comportamiento de escritura

Las actualizaciones de los ajustes se serializan mediante un bloqueo de la aplicación. IterVC escribe un archivo temporal, sustituye el archivo de ajustes existente y elimina el temporal. Esto reduce la posibilidad de dejar un archivo JSON escrito de forma parcial.

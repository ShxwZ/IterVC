---
title: Comprobación de actualizaciones
description: Controle las comprobaciones automáticas y manuales de GitHub Releases.
---

IterVC puede consultar la última versión publicada en el repositorio oficial de GitHub.

## Consentimiento

En el primer uso, IterVC pregunta si puede realizar comprobaciones automáticas de actualizaciones. La preferencia se guarda y puede cambiarse más adelante en **Settings**.

## Comportamiento

- El resultado correcto de una comprobación automática se guarda en caché durante 24 horas.
- Las comprobaciones manuales omiten la caché.
- Los errores de conexión o de la API de GitHub no impiden que la aplicación se inicie.
- IterVC solo notifica y abre la página de la versión; no descarga ni instala una actualización automáticamente.
- Al descartar una versión se oculta esa notificación concreta, pero una versión posterior puede volver a mostrar un aviso.

## Instale una actualización

Abra la página de la versión, cierre IterVC por completo desde la bandeja del sistema y siga las instrucciones de empaquetado de la versión. Los ajustes de usuario de `%AppData%\IterVC` son independientes de los archivos de la aplicación.

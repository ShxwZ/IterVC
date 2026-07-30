---
title: Inicio con Windows
description: Inicie IterVC automáticamente con Windows y, si lo desea, manténgalo oculto.
---

Abra **Settings** y active la opción de registro de inicio con Windows para ejecutar IterVC al iniciar sesión.

## Inicio oculto

Cuando está activado el registro de inicio, **Start hidden on Windows startup** puede mantener la ventana principal en la bandeja del sistema tras la inicialización. Esta opción está activada de forma predeterminada en el modelo de ajustes persistidos.

Un inicio manual debe seguir mostrando o restaurando la ventana principal. IterVC también usa un coordinador de instancia única, por lo que al iniciarlo de nuevo debería avisar al proceso principal existente en vez de arrancar un segundo motor de audio.

## Aviso para compilaciones portátiles

El registro de inicio indica a Windows la ubicación actual del ejecutable. Si usa una versión portátil:

1. Mueva primero IterVC a su carpeta definitiva.
2. Active después el registro de inicio.
3. Desactive y vuelva a activar la opción si más adelante mueve el ejecutable.

IterVC registra si se ha confirmado el aviso de inicio de una versión portátil.

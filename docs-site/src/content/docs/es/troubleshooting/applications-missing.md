---
title: Aplicaciones no detectadas
description: Haga que las aplicaciones aparezcan en la lista de audio por proceso.
---

## Inicie primero la reproducción

Muchas aplicaciones de Windows solo crean una sesión de audio después de emitir sonido. Inicie una canción, un vídeo, un juego o un sonido de prueba antes de actualizar la lista.

## Actualice manualmente

Pulse **Refresh applications** después de abrir, reiniciar o cambiar el dispositivo de salida de una aplicación.

## Compruebe el dispositivo de salida de Windows

Una aplicación que Windows envía a otros altavoces, auriculares, monitor o dispositivo virtual puede no estar asociada a la salida de referencia elegida en IterVC. Compruebe **Settings > System > Sound > Volume mixer** en Windows y compare los extremos seleccionados.

## Persistencia por nombre de proceso

IterVC recuerda las selecciones por nombre de proceso. Si una aplicación inicia el audio en un proceso auxiliar independiente, seleccione el proceso que muestra actividad en el medidor. Las pestañas del navegador y las aplicaciones multiproceso pueden compartir o cambiar su proceso de audio.

## Rutas de audio protegidas o inusuales

Algunos medios protegidos, aplicaciones aisladas, flujos en modo exclusivo o controladores no estándar pueden no exponer una sesión `process-loopback` compatible. Al informar de un caso reproducible, incluya los registros y el nombre exacto de la aplicación.

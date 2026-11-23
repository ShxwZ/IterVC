---
title: Requisitos
description: Versiones de Windows compatibles, arquitectura y requisitos opcionales de audio virtual.
---

## Sistema operativo

IterVC es compatible actualmente con:

- Windows 10 compilación 19041 / versión 2004 o posterior.
- Windows 11.
- Solo versiones de Windows de 64 bits.

La versión mínima de Windows es necesaria para la API nativa de WASAPI `process-loopback` que se usa para capturar audio por aplicación. La capa de interoperabilidad nativa actual está destinada a x64.

## Destino de reproducción

IterVC necesita un dispositivo de reproducción de Windows para la mezcla final. Puede ser:

- Un cable de audio virtual.
- Altavoces.
- Auriculares o unos auriculares con micrófono.
- Otro dispositivo de reproducción compatible con Windows.

Un cable virtual solo es necesario cuando la mezcla debe aparecer como entrada o micrófono en otra aplicación. IterVC se desarrolló y probó principalmente con **VB-Audio VB-CABLE**, disponible en la [página oficial de descarga de VB-Audio](https://vb-audio.com/Cable/).

Después de instalar VB-CABLE, Windows suele mostrar dos extremos:

- **CABLE Input**: el dispositivo de reproducción al que IterVC envía el audio.
- **CABLE Output**: el dispositivo de grabación que se selecciona como micrófono en Discord, OBS, VRChat u otra aplicación receptora.

Los nombres pueden variar ligeramente según el idioma, la edición del controlador u otros productos de VB-CABLE.

## Compilar desde el código fuente

Para compilar el proyecto se necesita el SDK de .NET 8. Las personas que descarguen una versión autocontenida deben seguir los requisitos indicados en la página de esa versión, en lugar de instalar el SDK sin necesidad.

---
title: ¿Qué es IterVC?
description: Conozca el propósito, los casos de uso compatibles y las principales limitaciones de IterVC.
---

IterVC es una aplicación de escritorio para Windows creada con .NET 8 y Avalonia UI. Simplifica un flujo habitual de enrutamiento de audio:

1. Seleccione una o varias aplicaciones en ejecución.
2. Añada, si lo necesita, un micrófono físico.
3. Ajuste los niveles de las aplicaciones y del micrófono.
4. Envíe la mezcla resultante a un dispositivo de reproducción de Windows.
5. Si otra aplicación debe recibir esa mezcla como micrófono, use un cable virtual y seleccione su extremo de grabación en la aplicación receptora.

## Usos habituales

- Reproducir música o efectos de sonido por la entrada de micrófono de un chat de voz mediante un cable virtual.
- Mezclar el audio seleccionado de un juego, navegador o reproductor multimedia con su voz.
- Enviar una mezcla controlada a Discord, OBS Studio, TeamSpeak o VRChat.
- Reproducir la mezcla final directamente en altavoces o auriculares cuando no se necesita una entrada de micrófono.
- Mostrar información multimedia en el Chatbox de VRChat mediante OSC.

## Lo que IterVC no hace

IterVC no es una estación de trabajo de audio digital completa ni sustituye todas las funciones de mezcladores avanzados como VoiceMeeter. La aplicación actual se centra en la captura de procesos seleccionados, la mezcla del micrófono, los controles básicos de ganancia, la puerta de ruido, la monitorización y el enrutamiento a un dispositivo de reproducción de Windows.

IterVC no instala ningún controlador de audio. Solo se necesita un cable virtual cuando otra aplicación debe reconocer la mezcla de IterVC como dispositivo de entrada. La opción recomendada y con la que se han realizado la mayor parte de las pruebas es [VB-Audio VB-CABLE](https://vb-audio.com/Cable/).

## Captura no destructiva

Las aplicaciones seleccionadas siguen reproduciéndose por la salida habitual de Windows. IterVC captura una copia de la salida de cada proceso y mezcla esa copia de forma independiente. Por tanto, activar una aplicación en IterVC no debería redirigir ni silenciar su reproducción normal.

---
title: Calidad de audio
description: Corrija eco, artefactos de sonido envolvente, recorte, volumen bajo, retardo e interrupciones de audio.
---

## Eco, reverberación o sonido hueco

La captura `process-loopback` puede recibir el audio después del procesamiento espacial de Windows o de los efectos del fabricante. En el dispositivo de reproducción seleccionado, pruebe a desactivar:

- El sonido espacial.
- La virtualización de sonido envolvente.
- Los efectos de Dolby, DTS, Sonic o los específicos de auriculares.
- Las mejoras de audio.

Después, reinicie la reproducción y actualice la lista de aplicaciones.

## Volumen bajo

1. Aumente primero el volumen de la aplicación de origen.
2. Compruebe el volumen de sesión de Windows de esa aplicación.
3. Aumente gradualmente la ganancia de aplicaciones de IterVC.
4. Compruebe la ganancia de entrada de la aplicación receptora.

La ganancia de aplicaciones admite hasta el 300 %, pero los aumentos elevados pueden amplificar el ruido y causar recorte.

## Distorsión o recorte

Reduzca la ganancia de aplicaciones, la amplificación del micrófono o el volumen de la fuente. Varias aplicaciones con un nivel alto pueden sumarse por encima de la escala completa aunque cada una suene limpia por separado. IterVC incluye etapas de protección de salida y diagnóstico, pero el mejor resultado se obtiene evitando una ganancia excesiva.

## Retardo

Es normal que haya algo de latencia porque IterVC captura, almacena en búfer, convierte, mezcla y reproduce el audio en tiempo real. La monitorización del micrófono mediante software hace que ese retardo sea más perceptible.

- Desactive la monitorización de micrófono cuando no la necesite.
- Evite ejecutar varias capas innecesarias de mejora de audio.
- Use controladores de audio estables y actualizados.
- Revise los registros en busca de faltas o desbordamientos de búfer y retrasos de procesamiento.

## Chasquidos o interrupciones

- Cierre las aplicaciones que carguen en exceso la CPU o la pila de audio.
- Mantenga, cuando sea posible, una frecuencia de muestreo estable y común en todos los dispositivos implicados.
- Desactive el modo exclusivo si otra aplicación toma repetidamente el control de un dispositivo.
- Vuelva a conectar o reinicie los dispositivos de audio USB.
- Reproduzca el problema y adjunte los registros al informar de él.

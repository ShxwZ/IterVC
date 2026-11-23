---
title: Sin audio
description: Diagnostique el silencio con los medidores de aplicaciones, micrófono y salida enrutada de IterVC.
---

Use los medidores de nivel para determinar dónde se interrumpe el audio.

## Los medidores de aplicaciones no muestran actividad

- Confirme que la aplicación está emitiendo sonido.
- Pulse **Refresh applications**.
- Confirme que la aplicación está seleccionada en el panel izquierdo.
- Confirme que no está silenciado todo el audio de aplicaciones.
- Compruebe que la aplicación reproduce el sonido por el dispositivo de salida de referencia seleccionado.
- Reinicie la aplicación si su sesión de audio de Windows está obsoleta.

## Los medidores de aplicaciones se mueven, pero la salida enrutada no

- Inicie el enrutamiento desde el control de estado de la cabecera.
- Seleccione un destino de reproducción válido: **CABLE Input**, altavoces, auriculares u otro extremo de reproducción de Windows.
- Compruebe que la ganancia de aplicaciones está por encima del 0 %.
- Pruebe a detener y reiniciar el enrutamiento después de cambiar de dispositivo.

## La salida enrutada se mueve, pero los altavoces o auriculares no emiten sonido

- Confirme que el dispositivo de reproducción físico esperado está seleccionado como destino.
- Compruebe el volumen de Windows, el silencio del dispositivo y el control de volumen del hardware.
- Confirme que otra aplicación no ha tomado el control exclusivo del dispositivo.
- No envíe el micrófono a altavoces cercanos, ya que puede causar realimentación.

## La salida enrutada se mueve, pero una aplicación receptora no recibe sonido por VB-CABLE

- Seleccione el lado de grabación del cable, normalmente **CABLE Output**, como micrófono en la aplicación receptora.
- Confirme que la aplicación receptora tiene permiso para usar el micrófono en los ajustes de privacidad de Windows.
- Compruebe su ganancia de entrada, silencio, umbral de activación por voz y dispositivo seleccionado.
- Desactive temporalmente las funciones de modo exclusivo o de mejora de los extremos del cable virtual si otra aplicación los está bloqueando.
- Reinstale VB-CABLE únicamente desde la [página oficial de VB-Audio](https://vb-audio.com/Cable/) si faltan los extremos o están dañados.

## El micrófono no se incluye en la mezcla

- Seleccione el micrófono físico correcto.
- Active la captura de micrófono desde la cabecera.
- Establezca la amplificación del micrófono por encima de 0.
- Desactive temporalmente la puerta de ruido o baje su umbral.
- Compruebe el medidor de salida del micrófono mientras habla.

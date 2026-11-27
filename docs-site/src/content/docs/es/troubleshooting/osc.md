---
title: Problemas con OSC
description: Diagnostique mensajes del Chatbox de VRChat ausentes, desactualizados, truncados o mal formados.
---

## No aparece nada en VRChat

- Active OSC dentro de VRChat.
- Active la opción de Chatbox OSC en IterVC.
- Inicie la reproducción multimedia en AIMP o en una aplicación que exponga metadatos de sesión multimedia de Windows.
- Para otros reproductores, confirme que el título multimedia aparece en los controles de Windows. AIMP también puede proporcionar los metadatos mediante su API remota integrada.
- Compruebe que el cortafuegos local o el software de seguridad no bloquean el tráfico OSC de bucle local.

Si ningún reproductor compatible proporciona una canción, IterVC muestra el indicador de parada. Una canción en pausa sigue el ajuste de visibilidad del Chatbox.

## El título o la hora son incorrectos

IterVC usa los metadatos de las sesiones multimedia de Windows o de la API remota de AIMP. Si AIMP no se detecta, compruebe que se ejecuta en la misma sesión de usuario de Windows y con el mismo nivel de privilegios que IterVC. Cierre otros reproductores activos para comprobar el método alternativo. La aplicación multimedia controla el título, el formato del artista y la precisión temporal.

## Las plantillas de varias líneas no se muestran correctamente

IterVC convierte los saltos de línea CRLF y LF en el separador de tabulación vertical que usa su biblioteca del Chatbox OSC de VRChat. Mantenga la plantilla corta y pruébela con la versión actual del cliente de VRChat.

## El mensaje se trunca o se ajusta mal

Reduzca el texto fijo y las decoraciones. Deje espacio para los valores largos que puedan generar `{title}` y `{time}`.

## El proceso de OSC se detiene

Abra la carpeta de registros y busque `OSC chatbox worker stopped unexpectedly` o `Error enviando mensaje OSC al chatbox` en el archivo más reciente.

---
title: Problemas con OSC
description: Diagnostique mensajes del Chatbox de VRChat ausentes, desactualizados, truncados o mal formados.
---

## No aparece nada en VRChat

- Active OSC dentro de VRChat.
- Active la opción de Chatbox OSC en IterVC.
- Inicie la reproducción multimedia en una aplicación que exponga metadatos de sesión multimedia de Windows.
- Confirme que el título multimedia aparece en los controles multimedia de Windows.
- Compruebe que el cortafuegos local o el software de seguridad no bloquean el tráfico OSC de bucle local.

Actualmente, IterVC solo envía información cuando recibe un título multimedia que no está vacío.

## El título o la hora son incorrectos

IterVC depende de los metadatos que proporciona la sesión multimedia activa de Windows. La aplicación multimedia controla el título, el formato del artista y la precisión temporal.

## Las plantillas de varias líneas no se muestran correctamente

IterVC convierte los saltos de línea CRLF y LF en el separador de tabulación vertical que usa su biblioteca del Chatbox OSC de VRChat. Mantenga la plantilla corta y pruébela con la versión actual del cliente de VRChat.

## El mensaje se trunca o se ajusta mal

Reduzca el texto fijo y las decoraciones. Deje espacio para los valores largos que puedan generar `{title}` y `{time}`.

## El proceso de OSC se detiene

Abra la carpeta de registros y busque `OSC chatbox worker stopped unexpectedly` o `Error enviando mensaje OSC al chatbox` en el archivo más reciente.

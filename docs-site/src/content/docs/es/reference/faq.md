---
title: Preguntas frecuentes
description: Respuestas a preguntas habituales sobre la configuración y el uso de IterVC.
---

## ¿IterVC silencia la aplicación seleccionada?

No. Captura una copia de la salida de la aplicación. La aplicación sigue reproduciéndose por sus altavoces o auriculares habituales.

## ¿Necesito VB-CABLE?

No en todos los casos. IterVC puede enviar la mezcla final directamente a altavoces, auriculares u otro dispositivo de reproducción. Sí necesita un cable virtual cuando otra aplicación debe recibir esa mezcla como micrófono o entrada de grabación. [VB-Audio VB-CABLE](https://vb-audio.com/Cable/) es la opción recomendada y con la que se han realizado la mayor parte de las pruebas, aunque pueden funcionar alternativas compatibles.

## ¿Qué extremo del cable debo usará

Al usar VB-CABLE, IterVC envía el sonido a **CABLE Input**. Discord, OBS, VRChat u otra aplicación receptora graba desde **CABLE Output**. Para la reproducción directa, seleccione como destino de IterVC los altavoces o auriculares.

## ¿Puedo enrutar aplicaciones sin micrófono?

Sí. Desactive la captura de micrófono y mantenga activo el enrutamiento de aplicaciones.

## ¿Puedo enrutar solo el micrófono?

Sí, siempre que haya un dispositivo de reproducción seleccionado y el enrutamiento está activo. Deje las aplicaciones sin seleccionar o silencie el bus de aplicaciones.

## ¿IterVC instala las actualizaciones automáticamente?

No. Puede comprobar GitHub Releases y abrir la página de la versión, pero no descarga ni instala actualizaciones automáticamente.

## ¿El enrutamiento continúa después de cerrar la ventana?

Depende del comportamiento de cierre seleccionado. **Minimize to tray** mantiene el proceso en ejecución; **Exit** lo detiene.

## ¿Por qué hay un pequeño retardo?

La captura y mezcla mediante software requieren almacenamiento en búfer, conversión, procesamiento y reproducción. Un pequeño retardo es normal y se nota sobre todo al monitorizar el propio micrófono.

## ¿Se admite Linux?

No con el motor de audio actual de `master`. Avalonia es multiplataforma, pero la captura de procesos, la gestión de dispositivos, la integración de inicio, el comportamiento de la bandeja y los atajos globales tienen actualmente implementaciones específicas de Windows.

---
title: Registros y diagnósticos
description: Localice los ajustes y los archivos de registro rotativos antes de informar de un error.
---

IterVC escribe los datos persistentes en:

```text
%AppData%\IterVC\
```

## Abra los registros desde la aplicación

Abra **Settings** y seleccione **Open logs folder** en Diagnostics.

El directorio de registros es:

```text
%AppData%\IterVC\Logs\
```

IterVC registra controladores globales de excepciones y, cuando es posible, escribe de inmediato los errores críticos en el archivo de registro rotativo.

## Archivo de ajustes

El archivo de ajustes persistentes es:

```text
%AppData%\IterVC\settings.json
```

No publique este archivo sin revisarlo. Puede contener identificadores de dispositivos, nombres de procesos, combinaciones de teclas, estado de actualización y preferencias de interfaz.

## Información útil para informar de un error

Incluya:

- La versión de IterVC que aparece en el pie de página o en el nombre del archivo de la versión.
- La versión y compilación de Windows.
- Los nombres de los dispositivos de audio y el software del controlador.
- El producto y la versión del cable virtual.
- La aplicación que se está capturando.
- Los pasos exactos para reproducir el problema.
- El archivo de registro pertinente generado justo después de reproducir el problema.

No edite el registro antes de adjuntarlo, salvo para eliminar información privada.

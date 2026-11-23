---
title: Comprobación de actualizaciones
description: Compruebe e instale versiones portables verificadas desde IterVC.
---

IterVC puede consultar la última versión publicada en el repositorio oficial de GitHub.

## Consentimiento y control

En el primer uso, IterVC pregunta si puede realizar comprobaciones automáticas de actualizaciones. La preferencia se guarda y puede cambiarse en **Configuración**. **La instalación nunca es automática**: hay que pulsar **Instalar actualización** cuando se detecta una versión nueva.

## Comportamiento

- Las comprobaciones automáticas correctas se guardan en caché durante 24 horas; las manuales no.
- Los errores de conexión o de la API de GitHub no impiden iniciar IterVC.
- **Ver versión** permite seguir descargando la actualización manualmente.
- Descartar una versión oculta esa notificación sin bloquear las siguientes.
- La descarga y la instalación solo comienzan cuando el usuario pulsa **Instalar actualización**.

## Instalación portable

El actualizador descarga `IterVC-Windows.zip` del repositorio oficial y verifica su SHA-256 frente al hash del archivo publicado por GitHub Releases. El ZIP debe contener únicamente `IterVC.exe`. Una vez verificado, el ejecutable nuevo se prepara junto al portable actual. Un proceso auxiliar espera a que IterVC se cierre, sustituye el archivo y reinicia la aplicación.

Se conserva el ejecutable anterior en el mismo directorio con el nombre `IterVC.previous-*.exe` para facilitar la recuperación. La configuración de usuario de `%AppData%\\IterVC` permanece intacta.

Es necesario tener **permiso de escritura en la carpeta de IterVC.exe** y un sistema de archivos compatible con la sustitución atómica. No se admiten ejecutables renombrados ni carpetas protegidas. Si falla, utiliza **Ver versión** para descargar la actualización manualmente. Los errores ocurridos después del cierre se registran en `%AppData%\\IterVC\\last-update-error.txt`.

El instalador no ejecuta ZIPs personalizados sin verificar: el archivo oficial debe incluir un hash SHA-256 en la API de GitHub Releases.

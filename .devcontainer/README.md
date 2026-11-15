# Probar IterVC Linux en el navegador (GitHub Codespaces)

Este entorno sirve para **ver y utilizar la GUI de Linux desde Windows**, sin instalar una máquina virtual. Se desarrolla en la misma rama de la [PR #37](https://github.com/ShxwZ/IterVC/pull/37), reutilizando el backend Rust y la interfaz Avalonia compartida.

## Abrir en Codespaces

1. Abre **[Crear Codespace de IterVC Linux](https://codespaces.new/ShxwZ/IterVC/tree/feature/rust-buffers-linux-pipewire)**. Inicia sesión en GitHub si te lo pide.
2. Comprueba que la rama elegida es `feature/rust-buffers-linux-pipewire`, **no** `master`, y que se usa `.devcontainer/devcontainer.json`. Confirma **Create codespace**.
3. El Codespace construye el backend PipeWire, la biblioteca RNNoise que comparte con Windows y el escritorio Avalonia. La configuración también pone en marcha un escritorio virtual privado automáticamente.
4. En VS Code web, abre **PORTS / PUERTOS**, localiza **6080** (IterVC Linux desktop) y elige **Open in Browser / Abrir en el navegador**. También puede abrirse automáticamente cuando el puerto se detecta.
5. En la pestaña que se abre, utiliza `/vnc.html?autoconnect=1&resize=scale` para conectar automáticamente. Ya podrás hacer clic en la GUI de IterVC Linux desde Windows.

**Importante:** el puerto **6080 debe permanecer Private / Privado**. GitHub requiere iniciar sesión para acceder a los puertos privados. **NO lo pongas en Public:** dentro del Codespace, VNC no tiene una segunda contraseña y está pensado exclusivamente para la autenticación de GitHub.

## Qué se puede probar

- Ventana y diseño original compartido con Windows: pestañas, aplicaciones, ajustes, idioma, estado, atajos y controles de RNNoise.
- Guardado de configuraciones locales del Linux remoto.
- Comprobación del arranque del ejecutable Linux, de las bibliotecas nativas y del servidor gráfico.
- Algunas operaciones con nodos PipeWire **virtuales** dentro del Codespace.

**Limitación:** el micrófono, los altavoces y el audio de las aplicaciones de tu **PC Windows no se transmiten automáticamente** al Codespace. Para verificar que el mezclador captura tu micrófono físico, que la puerta de ruido suena bien y que se mezclan los programas de tu PC, hay que probarlo en un Linux local (por ejemplo, USB Live). Codespaces puede consumir tu cuota gratuita; consulta el uso en la configuración de GitHub.

## Si no aparece la ventana

Desde el terminal del Codespace:

```bash
# Ver si se ha compilado la aplicación:
ls -l ~/.local/share/itervc-browser/IterVC.Linux

# Reconstruir la versión de la rama actual:
bash .devcontainer/build-linux.sh

# Arrancar el escritorio privado:
bash .devcontainer/start-browser-preview.sh

# Consultar los registros:
cat ~/.local/state/itervc-browser/session.log
cat ~/.local/state/itervc-browser/itervc.log
```

Si cierras la ventana de IterVC en el escritorio virtual, puedes volver a abrirla en el Codespace mediante:

```bash
DISPLAY=:99 ~/.local/share/itervc-browser/IterVC.Linux
```

**No copies las credenciales, el nombre de tu Codespace ni la URL de su puerto a lugares públicos.** Al terminar las pruebas, detén tu Codespace desde [github.com/codespaces](https://github.com/codespaces) para no seguir utilizando tu cuota.

## Implementación

- `Dockerfile`: Ubuntu 24.04 con Xvfb, Openbox, x11vnc, noVNC, PipeWire y utilidades de pantalla.
- `devcontainer.json`: .NET 8, Rust, compilación al crear el entorno y arranque al reanudarlo.
- `build-linux.sh`: construye Rust, el RNNoise original y publica un ejecutable .NET autocontenido desde la rama actual.
- `start-browser-preview.sh`: lanza la sesión y comprueba que el puerto está listo.
- `desktop-session.sh`: X11 virtual, escritorio, servicios PipeWire y servidor noVNC; solo escucha en `127.0.0.1`.

Este Codespace es una **vista previa gráfica** y no equivale a certificar el audio real de Linux. La PR permanece en borrador.

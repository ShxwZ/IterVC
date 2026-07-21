# Changelog: Personalización y Atajos

Este documento detalla los cambios introducidos recientemente en el código fuente de IterVC para implementar la tematización dinámica y la funcionalidad de atajos personalizados, así como el razonamiento técnico (el "por qué") detrás de cada decisión.

## 1. Personalización de Apariencia (Temas y Fondos)

Se agregó una pestaña completa de "Apariencia" para que los usuarios puedan personalizar la interfaz a su gusto.

*   **[Nuevo] Pestaña de Apariencia en la UI (`MainWindow.axaml`)**:
    *   **Qué se hizo**: Se reestructuró la ventana principal, envolviendo el `Grid` original en un contenedor `Panel`. Se agregaron controles para escoger una imagen de fondo y sliders (RGB) para cambiar los colores del acento y de las tarjetas.
    *   **Por qué**: Un `Panel` permite renderizar capas superpuestas en Avalonia, lo que era estrictamente necesario para poder colocar la imagen de fondo detrás de toda la aplicación y aplicar una capa oscura (`Opacity="0.45"`) por encima para garantizar que los textos sigan siendo legibles, independientemente de la imagen elegida.
*   **[Nuevo] Extracción Automática de Color (`ColorExtractionService.cs`)**:
    *   **Qué se hizo**: Se integró un servicio que analiza la imagen de fondo cargada y calcula los colores predominantes.
    *   **Por qué**: Para brindar un diseño premium y reactivo. Al elegir una imagen, la aplicación automáticamente adapta sus botones e interfaz para que armonicen con el fondo, mejorando enormemente la estética sin requerir esfuerzo manual del usuario.
*   **[Modificado] Gestión de Temas (`ThemeService.cs` y `MainViewModel.cs`)**:
    *   **Qué se hizo**: Se conectaron los sliders de color con los recursos de Avalonia (`DynamicResource`) en tiempo real.
    *   **Por qué**: Para que el usuario tenga retroalimentación instantánea al arrastrar los sliders, actualizando todo el aspecto visual sin necesidad de reiniciar la aplicación.

---

## 2. Atajos de Teclado Personalizables (Iniciar/Detener)

Se implementó un sistema para asignar un atajo global (dentro de la aplicación) para alternar el enrutamiento de audio.

*   **[Nuevo] Pestaña de Atajos y Captura de Teclas (`MainWindow.axaml` y `MainWindow.axaml.cs`)**:
    *   **Qué se hizo**: Se creó una pestaña "Atajos" y se añadió un `TextBox` en modo de solo lectura (`IsReadOnly="True"`). En lugar de permitir escribir texto libremente, se añadió un evento `KeyDown` (`ShortcutTextBox_KeyDown`) en el *code-behind*.
    *   **Por qué**: Al principio, escribir el atajo como texto libre (ej. "Ctrl+F9") causaba errores porque Avalonia fallaba al intentar interpretar cadenas a medias como "C", "Cr" al vuelo. La solución intercepta físicamente la tecla pulsada, extrae los modificadores (Shift, Ctrl, Alt) y construye el comando perfecto automáticamente. Esto hace que la configuración sea infalible y mucho más intuitiva para el usuario final.
*   **[Modificado] Lógica de Binding de Gestos (`MainViewModel.cs`)**:
    *   **Qué se hizo**: Se añadió una propiedad computada `ToggleShortcutGesture` que intenta traducir (hacer *Parse*) de la cadena de texto guardada a un objeto `KeyGesture` nativo de Avalonia.
    *   **Por qué**: Usar una simple cadena de texto (`string`) en el `KeyBinding` de la ventana daba problemas en algunas compilaciones de Avalonia 11 al no resolver bien la conversión de tipos. Al alimentar directamente un tipo estricto `KeyGesture`, nos aseguramos de que el atajo funcione al 100% de manera confiable.
*   **[Modificado] Persistencia y Localización (`AppSettings.cs`, `LocalizationService.cs`, `TextsViewModel.cs`)**:
    *   **Qué se hizo**: Se agregó `ToggleRoutingShortcut` a la clase que gestiona el guardado de configuración (`settings.json`). Además, se agregaron todas las traducciones para las etiquetas de la interfaz en español e inglés.
    *   **Por qué**: Para que el atajo elegido sobreviva al cerrar la aplicación, garantizando que el usuario solo tenga que configurarlo una vez. La actualización de la localización asegura que el proyecto mantenga sus altos estándares para usuarios internacionales.

---

## 3. Atajo Global, Bandeja del Sistema y Debounce de Configuración

*   **[Nuevo] Atajo de teclado global de sistema (`GlobalHotkeyService.cs`, `MainWindow.axaml.cs`)**:
    *   **Qué se hizo**: El atajo de iniciar/detener ahora se registra con `RegisterHotKey` de Win32 y el `WM_HOTKEY` se intercepta con `Win32Properties.AddWndProcHookCallback` de Avalonia. Se re-registra automáticamente al cambiar el atajo en la pestaña de Atajos.
    *   **Por qué**: El `KeyBinding` anterior solo funcionaba con la ventana enfocada. El caso de uso real (alternar el enrutado desde VRChat o un juego) exige que funcione con la app en segundo plano o en la bandeja. Si Windows rechaza el registro (atajo en uso por otro programa), se avisa en la consola de estado y el `KeyBinding` clásico sigue como fallback dentro de la app.
*   **[Nuevo] Icono en la bandeja del sistema (`App.axaml.cs`)**:
    *   **Qué se hizo**: `TrayIcon` de Avalonia con menú nativo localizado (Mostrar ventana / Salir). Cerrar la ventana con la X la oculta a la bandeja (opción `MinimizeToTray`, activada por defecto y configurable en la pestaña de Atajos); el clic en el icono restaura la ventana.
    *   **Por qué**: IterVC vive en segundo plano mientras el usuario juega; el enrutado de audio sigue activo con la ventana oculta sin ocupar la barra de tareas. "Salir" del menú de la bandeja cierra de verdad.
*   **[Nuevo] Chatbox OSC sin música (`MainViewModel.cs`, `LocalizationService.cs`)**:
    *   **Qué se hizo**: El bucle OSC ahora envía el mensaje siempre que el chatbox esté activo: sin sesión de medios, `{title}` muestra "Nada reproduciéndose", `{status}` "Detenido" y `{time}` la hora local. Se detectan también sesiones en pausa (antes solo `Playing`), con su estado localizado. Al desactivar el chatbox se limpia el último mensaje en VRChat (`ClearChatbox`), y el intervalo de envío pasó de 1,0 s a 1,5 s.
    *   **Por qué**: Era la limitación anotada en el README ("one must play music to make the chatbox appear"). El intervalo de 1 s superaba el rate-limit del chatbox de VRChat (~1,5 s), con lo que se descartaban mensajes.
*   **[Nuevo] Volumen individual por aplicación (`AudioRouterService.cs`, `AppAudioItemViewModel.cs`)**:
    *   **Qué se hizo**: Cada app capturada tiene su propio slider (0-200 %) en la lista, multiplicado con la ganancia conjunta de apps. Se persiste por nombre de proceso en `AppVolumes` (settings.json) usando el guardado con debounce, y se restaura al re-capturar la app.
    *   **Por qué**: La ganancia global no permitía equilibrar, p. ej., música alta con un juego bajo sin tocar el volumen de las apps en Windows.
*   **[Nuevo] Medidores de nivel / VU meters (`AudioRouterService.cs`, `MainViewModel.cs`)**:
    *   **Qué se hizo**: Un tap transparente (`LevelMeterSampleProvider`) mide el pico post-volumen de cada app en la mezcla, y `FeedMicrophoneSamples` mide el del micrófono (reflejando ganancia y boost). Un `DispatcherTimer` a ~15 Hz lo pinta como barras finas bajo cada app y bajo el slider del micro, con caída suave.
    *   **Por qué**: Feedback inmediato de "está fluyendo audio" para diagnosticar el clásico "no me oyen" sin salir de la app. Los medidores de apps marcan 0 con el enrutado detenido (miden la mezcla real hacia VB-Cable); el del micro funciona siempre que haya captura.
*   **[Nuevo] Debounce de escrituras de settings (`SettingsService.cs`, `ISettingsService.cs`)**:
    *   **Qué se hizo**: Nuevo `QueueUpdate` que aplica la mutación en memoria y agrupa el guardado a disco tras 400 ms de silencio, y `FlushAsync` que persiste lo pendiente al salir (llamado desde `ShutdownRequested`). Los sliders (volúmenes, RGB del tema) y la plantilla OSC usan ahora `QueueUpdate`; los cambios discretos (checkboxes, dispositivos) siguen con `UpdateAsync` inmediato.
    *   **Por qué**: Arrastrar un slider producía decenas de serializaciones completas de `settings.json` por segundo. Además se corrigió una carrera: `UpdateAsync` mutaba `Current` fuera del semáforo; ahora mutación y escritura ocurren dentro del lock, y la escritura temporal usa `File.Move` (rename atómico) en vez de `Copy`+`Delete`.

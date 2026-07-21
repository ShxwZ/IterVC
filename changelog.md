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

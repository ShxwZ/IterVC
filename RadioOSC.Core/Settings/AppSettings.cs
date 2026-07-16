namespace RadioOSC.Core.Settings;

/// <summary>
/// Configuraci\u00f3n persistida de la aplicaci\u00f3n (settings.json).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Versi\u00f3n del esquema, para permitir migraciones futuras.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Id del dispositivo de salida principal (altavoces/auriculares) desde el que se hace loopback.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>Id del dispositivo virtual VB-Cable donde se enviar\u00e1 la mezcla final.</summary>
    public string? VbCableDeviceId { get; set; }

    /// <summary>Id del micr\u00f3fono f\u00edsico de entrada.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Volumen conjunto de las apps capturadas en la mezcla (0.0 - 1.0).</summary>
    public float AppsVolume { get; set; } = 1.0f;

    /// <summary>Volumen del micr\u00f3fono en la mezcla (0.0 - 1.0).</summary>
    public float MicrophoneVolume { get; set; } = 1.0f;

    /// <summary>
    /// Multiplicador extra aplicado encima de <see cref="MicrophoneVolume"/>: 1.0 = sin boost,
    /// 2.0 = doble de volumen efectivo. Rango recomendado 0.5 - 3.0.
    /// </summary>
    public float MicrophoneBoost { get; set; } = 1.0f;

    /// <summary>Si true, el micr\u00f3fono tambi\u00e9n se escucha por el dispositivo principal (auto-monitorizaci\u00f3n).</summary>
    public bool MonitorMicrophone { get; set; }

    /// <summary>
    /// Nombres de proceso (p. ej. "spotify") que el usuario ha marcado como incluidos en la mezcla.
    /// Se persiste por nombre, no por ProcessId, porque el PID cambia entre ejecuciones.
    /// </summary>
    public List<string> IncludedProcessNames { get; set; } = new();

    // ----------------------------------------------------------------
    // Campos nuevos (schemaversion 2): preferencias de UI y OSC.
    // ----------------------------------------------------------------

    public string Language { get; set; } = "Español";

    /// <summary>Plantilla del mensaje OSC. Default: "{title} - {status}".</summary>
    public string OscTemplate { get; set; } = "{title} - {status}";

    /// <summary>Si el chatbox OSC estaba activo al cerrar la app.</summary>
    public bool EnableOscChatbox { get; set; }
}

namespace IterVC.Core.Settings;

/// <summary>
/// Configuración persistida de la aplicación (settings.json).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Versión del esquema, para permitir migraciones futuras.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Id del dispositivo de salida principal (altavoces/auriculares) desde el que se hace loopback.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>Id del dispositivo virtual VB-Cable donde se enviará la mezcla final.</summary>
    public string? VbCableDeviceId { get; set; }

    /// <summary>Id del micrófono físico de entrada.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Whether capture from the selected physical microphone is enabled.</summary>
    public bool MicrophoneEnabled { get; set; } = true;

    /// <summary>Volumen conjunto de las apps capturadas en la mezcla (0.0 - 1.0).</summary>
    public float AppsVolume { get; set; } = 1.0f;

    /// <summary>Volumen del micrófono en la mezcla (0.0 - 1.0).</summary>
    public float MicrophoneVolume { get; set; } = 1.0f;

    /// <summary>
    /// Multiplicador extra aplicado encima de <see cref="MicrophoneVolume"/>: 1.0 = sin boost,
    /// 2.0 = doble de volumen efectivo. Rango recomendado 0.5 - 3.0.
    /// </summary>
    public float MicrophoneBoost { get; set; } = 1.0f;

    /// <summary>Si true, el micrófono también se escucha por el dispositivo principal (auto-monitorización).</summary>
    public bool MonitorMicrophone { get; set; }

    /// <summary>Whether the microphone noise gate is enabled.</summary>
    public bool NoiseGateEnabled { get; set; }

    /// <summary>Input level in dB required to open the noise gate.</summary>
    public float NoiseGateThresholdDb { get; set; } = -45f;

    /// <summary>Time in milliseconds for the gate gain to rise from muted to fully open.</summary>
    public float NoiseGateAttackMilliseconds { get; set; } = 10f;

    /// <summary>Time in milliseconds for the gate gain to fall from fully open to muted.</summary>
    public float NoiseGateReleaseMilliseconds { get; set; } = 150f;

    /// <summary>
    /// Nombres de proceso (p. ej. "spotify") que el usuario ha marcado como incluidos en la mezcla.
    /// Se persiste por nombre, no por ProcessId, porque el PID cambia entre ejecuciones.
    /// </summary>
    public List<string> IncludedProcessNames { get; set; } = new();

    // ----------------------------------------------------------------
    // Campos nuevos (schemaversion 2): preferencias de UI y OSC.
    // ----------------------------------------------------------------

    /// <summary>Language selected for localized UI strings.</summary>
    public string Language { get; set; } = "Español";

    /// <summary>Plantilla del mensaje OSC. Default: "{title} - {status}".</summary>
    public string OscTemplate { get; set; } = "{title} - {status}";

    /// <summary>Si el chatbox OSC estaba activo al cerrar la app.</summary>
    public bool EnableOscChatbox { get; set; }
}

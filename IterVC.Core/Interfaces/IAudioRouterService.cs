namespace IterVC.Core.Interfaces;

/// <summary>
/// Captura el audio de las apps seleccionadas (proceso a proceso, sin silenciarlas) y del
/// micrófono físico, los mezcla y envía el resultado a un dispositivo virtual (VB-Cable),
/// de forma que la mezcla sea detectada por otras apps como si fuese un micrófono conectado.
/// </summary>
public interface IAudioRouterService : IDisposable
{
    bool IsRouting { get; }
    float MicrophoneInputLevelDb { get; }
    float MicrophoneOutputLevelDb { get; }
    float NoiseGateGain { get; }
    bool IsNoiseGateOpen { get; }

    /// <summary>Feeds interleaved IEEE float microphone samples into the routing pipeline.</summary>
    void FeedMicrophoneSamples(byte[] pcmBytes, int count);

    /// <summary>
    /// Inicia el enrutado: abre el dispositivo VB-Cable de salida y comienza a mezclar
    /// las apps ya añadidas (<see cref="AddAppSourceAsync"/>) + el micrófono hacia él.
    /// </summary>
    Task StartAsync(string vbCableDeviceId, CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>Cambia el dispositivo VB-Cable de destino sin perder el estado de mezcla.</summary>
    Task SetTargetDeviceAsync(string vbCableDeviceId);

    /// <summary>
    /// Empieza a capturar el audio de un proceso (y sus hijos) y a mezclarlo hacia VB-Cable.
    /// La app sigue sonando con total normalidad en el PC, sólo toma una copia de sus muestras.
    /// </summary>
    /// <param name="processId">PID del proceso a capturar.</param>
    /// <param name="useRawAudio">
    /// Retained for API compatibility and currently ignored. Process loopback uses an
    /// endpoint-independent virtual capture device that does not expose endpoint RAW mode.
    /// </param>
    Task AddAppSourceAsync(int processId, bool useRawAudio = true);

    /// <summary>Deja de capturar el audio de un proceso. La app sigue sonando en el PC igual que antes.</summary>
    Task RemoveAppSourceAsync(int processId);

    /// <summary>Activa/desactiva la monitorización (escucha local) del micrófono.</summary>
    void SetMonitorMicrophone(bool enabled);

    /// <summary>Volumen conjunto aplicado a todas las apps capturadas.</summary>
    void SetAppsVolume(float volume);

    /// <summary>Volumen base del micrófono (0.0 - 1.0). Se combina con el boost.</summary>
    void SetMicrophoneVolume(float volume);

    /// <summary>Multiplicador extra de boost sobre el volumen base. 1.0 = sin boost.</summary>
    void SetMicrophoneBoost(float boost);

    /// <summary>Configures the real-time microphone noise gate.</summary>
    void ConfigureNoiseGate(bool enabled, float thresholdDb, float attackMilliseconds, float releaseMilliseconds);
}

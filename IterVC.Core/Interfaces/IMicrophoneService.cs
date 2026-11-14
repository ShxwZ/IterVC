namespace IterVC.Core.Interfaces;

using IterVC.Core.Models;

/// <summary>
/// Captura el micrófono físico seleccionado y expone su señal como stream de audio
/// para que el <see cref="IAudioRouterService"/> la mezcle.
/// </summary>
public interface IMicrophoneService : IDisposable
{
    bool IsCapturing { get; }

    Task StartAsync(string microphoneDeviceId, CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SetDeviceAsync(string microphoneDeviceId);

    /// <summary>Enable or disable the real-time microphone noise suppression stage.</summary>
    void SetNoiseSuppressionEnabled(bool enabled);

    /// <summary>PCM del micrófono, procesado con RNNoise si está activo, listo para el mezclador.</summary>
    event EventHandler<AudioDataEventArgs>? DataAvailable;
}

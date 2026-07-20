namespace IterVC.Core.Interfaces;

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

    /// <summary>Datos PCM capturados en crudo, listos para ser inyectados en el mixer.</summary>
    event EventHandler<byte[]>? DataAvailable;
}

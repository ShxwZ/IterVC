using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RadioOSC.Core.Interfaces;

namespace RadioOSC.Audio;

/// <summary>
/// Captura el micrófono físico seleccionado usando WasapiCapture y reemite
/// los buffers PCM crudos a través de <see cref="DataAvailable"/>.
/// </summary>
public sealed class MicrophoneService : IMicrophoneService
{
    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private WasapiCapture? _capture;

    public bool IsCapturing { get; private set; }

    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public event EventHandler<byte[]>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(string microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        StopInternal();

        var device = _enumerator.GetDevice(microphoneDeviceId);
        _capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
        };
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                _logger.LogError(e.Exception, "La captura del micrófono se detuvo con error");
            IsCapturing = false;
        };

        _capture.StartRecording();
        IsCapturing = true;
        _logger.LogInformation("Captura de micrófono iniciada en '{Device}'", device.FriendlyName);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopInternal();
        return Task.CompletedTask;
    }

    // CORREGIDO: Ahora detiene la captura anterior e inicia la nueva de manera incondicional
    public async Task SetDeviceAsync(string microphoneDeviceId)
    {
        StopInternal();
        await StartAsync(microphoneDeviceId);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        DataAvailable?.Invoke(this, copy);
    }

    private void StopInternal()
    {
        if (_capture is null) return;

        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deteniendo la captura de micrófono");
        }
        finally
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Dispose();
            _capture = null;
            IsCapturing = false;
        }
    }

    public void Dispose()
    {
        StopInternal();
        _enumerator.Dispose();
    }
}
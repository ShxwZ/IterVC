using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Audio;

/// <summary>
/// Captura el micrófono físico seleccionado usando WasapiCapture y reemite
/// los buffers PCM crudos a través de <see cref="DataAvailable"/>.
/// </summary>
public sealed class MicrophoneService : IMicrophoneService
{
    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private WasapiCapture? _capture;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;

    public bool IsCapturing { get; private set; }

    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(string microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        StopInternal();

        var device = _enumerator.GetDevice(microphoneDeviceId);
        Exception? preferredFailure = null;
        foreach (var latency in new[]
                 {
                     AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds,
                     AudioLatencyPolicy.FallbackMicrophoneBufferMilliseconds
                 })
        {
            try
            {
                var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: latency)
                {
                    WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)
                };
                _recordingStoppedHandler = OnRecordingStopped;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += _recordingStoppedHandler;
                _capture = capture;
                capture.StartRecording();
                IsCapturing = true;
                _logger.LogInformation(
                    "Microphone capture initialized: Device={Device} Format={Format} PreferredBufferMs={PreferredBufferMs} SelectedBufferMs={SelectedBufferMs} FallbackUsed={FallbackUsed}",
                    device.FriendlyName, capture.WaveFormat,
                    AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds, latency,
                    latency != AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds);
                return Task.CompletedTask;
            }
            catch (Exception exception)
            {
                CleanupCapture();
                if (latency == AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds)
                {
                    preferredFailure = exception;
                    continue;
                }

                throw new AggregateException(
                    "Could not initialize microphone capture at the preferred or fallback buffer size.",
                    preferredFailure!, exception);
            }
        }

        throw new InvalidOperationException("Microphone capture initialization did not run.");
    }

    public Task StopAsync()
    {
        StopInternal();
        return Task.CompletedTask;
    }

    public async Task SetDeviceAsync(string microphoneDeviceId)
    {
        StopInternal();
        await StartAsync(microphoneDeviceId);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _logger.LogError(e.Exception, "Microphone capture stopped with an error");
        IsCapturing = false;
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
            CleanupCapture();
        }
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            if (_recordingStoppedHandler is not null)
                _capture.RecordingStopped -= _recordingStoppedHandler;
            _capture.Dispose();
        }
        _capture = null;
        _recordingStoppedHandler = null;
        IsCapturing = false;
    }

    public void Dispose()
    {
        StopInternal();
        _enumerator.Dispose();
    }
}

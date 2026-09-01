using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RNNoise.NET;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Audio;

public sealed class MicrophoneService : IMicrophoneService
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _processingLock = new();
    private WasapiCapture? _capture;
    private Denoiser? _leftDenoiser;
    private Denoiser? _rightDenoiser;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;
    private volatile bool _noiseSuppressionEnabled = true;

    public bool IsCapturing { get; private set; }
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger) => _logger = logger;

    public void SetNoiseSuppressionEnabled(bool enabled) => _noiseSuppressionEnabled = enabled;

    public Task StartAsync(string microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        StopInternal();
        var device = _enumerator.GetDevice(microphoneDeviceId);
        Exception? preferredFailure = null;
        foreach (var latency in new[] { AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds, AudioLatencyPolicy.FallbackMicrophoneBufferMilliseconds })
        {
            try
            {
                var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: latency)
                { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels) };
                _recordingStoppedHandler = OnRecordingStopped;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += _recordingStoppedHandler;
                _capture = capture;
                try
                {
                    // RNNoise is mono, so keep one state per physical channel.
                    // This avoids downmixing the microphone before suppression and
                    // lets each RNNoise state retain its own recurrent history.
                    _leftDenoiser = new Denoiser();
                    _rightDenoiser = new Denoiser();
                }
                catch (Exception ex)
                {
                    DisposeDenoisers();
                    _logger.LogWarning(ex, "RNNoise unavailable; continuing without suppression");
                }
                capture.StartRecording();
                IsCapturing = true;
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                CleanupCapture();
                if (latency == AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds) { preferredFailure = ex; continue; }
                throw new AggregateException("Could not initialize microphone capture.", preferredFailure!, ex);
            }
        }
        throw new InvalidOperationException("Microphone capture initialization did not run.");
    }

    public Task StopAsync() { StopInternal(); return Task.CompletedTask; }
    public async Task SetDeviceAsync(string microphoneDeviceId) { StopInternal(); await StartAsync(microphoneDeviceId); }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        if (_noiseSuppressionEnabled && _leftDenoiser is not null && _rightDenoiser is not null)
            TryDenoiseStereoFloatBuffer(e.Buffer, e.BytesRecorded);

        // The buffer is modified in-place. The router and all microphone meters
        // therefore observe the same post-RNNoise signal.
        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
    }

    private bool TryDenoiseStereoFloatBuffer(byte[] buffer, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / sizeof(float);
        sampleCount -= sampleCount % Channels;
        if (sampleCount < Channels) return false;

        lock (_processingLock)
        {
            try
            {
                var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, sampleCount * sizeof(float)));
                var frames = sampleCount / Channels;
                var left = new float[frames];
                var right = new float[frames];

                for (var i = 0; i < frames; i++)
                {
                    left[i] = samples[i * Channels];
                    right[i] = samples[i * Channels + 1];
                }

                var leftProcessed = _leftDenoiser!.Denoise(left.AsSpan(), finish: false);
                var rightProcessed = _rightDenoiser!.Denoise(right.AsSpan(), finish: false);

                // WasapiCapture is configured for 10/20 ms at 48 kHz, which maps
                // exactly to one/two RNNoise frames. If a device ever supplies a
                // shorter callback, keep that callback untouched rather than
                // mixing processed and unprocessed samples out of alignment.
                if (leftProcessed != frames || rightProcessed != frames)
                    return false;

                for (var i = 0; i < frames; i++)
                {
                    samples[i * Channels] = left[i];
                    samples[i * Channels + 1] = right[i];
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RNNoise failed; keeping original microphone buffer");
                return false;
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) _logger.LogError(e.Exception, "Microphone capture stopped with an error");
        IsCapturing = false;
    }

    private void StopInternal()
    {
        if (_capture is null) { DisposeDenoisers(); return; }
        try { _capture.StopRecording(); } catch (Exception ex) { _logger.LogWarning(ex, "Error stopping microphone capture"); }
        finally { CleanupCapture(); }
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            if (_recordingStoppedHandler is not null) _capture.RecordingStopped -= _recordingStoppedHandler;
            _capture.Dispose();
        }
        _capture = null;
        _recordingStoppedHandler = null;
        DisposeDenoisers();
        IsCapturing = false;
    }

    private void DisposeDenoisers()
    {
        _leftDenoiser?.Dispose();
        _rightDenoiser?.Dispose();
        _leftDenoiser = null;
        _rightDenoiser = null;
    }

    public void Dispose() { StopInternal(); _enumerator.Dispose(); }
}

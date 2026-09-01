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
    private const int RnNoiseFrameSamples = 480;
    private const int BytesPerStereoSample = sizeof(float) * Channels;
    private const int RnNoiseFrameBytes = RnNoiseFrameSamples * BytesPerStereoSample;
    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _processingLock = new();
    private WasapiCapture? _capture;
    private Denoiser? _denoiser;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;
    private byte[] _pendingBytes = Array.Empty<byte>();
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
                try { _denoiser = new Denoiser(); }
                catch (Exception ex) { _denoiser = null; _logger.LogWarning(ex, "RNNoise unavailable; continuing without suppression"); }
                lock (_processingLock) _pendingBytes = Array.Empty<byte>();
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

        if (!_noiseSuppressionEnabled || _denoiser is null)
        {
            DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
            return;
        }

        if (!TryDenoiseStereoFloatBuffer(e.Buffer, e.BytesRecorded, out var processedBytes))
        {
            DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
            return;
        }

        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, processedBytes));
    }

    private bool TryDenoiseStereoFloatBuffer(byte[] buffer, int bytesRecorded, out int processedBytes)
    {
        processedBytes = 0;
        lock (_processingLock)
        {
            try
            {
                var input = new byte[_pendingBytes.Length + bytesRecorded];
                Buffer.BlockCopy(_pendingBytes, 0, input, 0, _pendingBytes.Length);
                Buffer.BlockCopy(buffer, 0, input, _pendingBytes.Length, bytesRecorded);

                var completeBytes = input.Length - (input.Length % RnNoiseFrameBytes);
                if (completeBytes == 0)
                {
                    _pendingBytes = input;
                    return false;
                }

                var remainderBytes = input.Length - completeBytes;
                var output = new byte[completeBytes];
                Buffer.BlockCopy(input, 0, output, 0, completeBytes);
                _pendingBytes = remainderBytes == 0 ? Array.Empty<byte>() : input[^remainderBytes..];

                var samples = MemoryMarshal.Cast<byte, float>(output.AsSpan());
                var mono = new float[RnNoiseFrameSamples];

                for (var offset = 0; offset < samples.Length; offset += RnNoiseFrameSamples * Channels)
                {
                    for (var i = 0; i < RnNoiseFrameSamples; i++)
                        mono[i] = (samples[offset + i * Channels] + samples[offset + i * Channels + 1]) * 0.5f;

                    var processed = _denoiser!.Denoise(mono.AsSpan(), finish: false);
                    if (processed != RnNoiseFrameSamples)
                        throw new InvalidOperationException($"RNNoise returned {processed} samples instead of {RnNoiseFrameSamples}.");

                    for (var i = 0; i < RnNoiseFrameSamples; i++)
                    {
                        var sample = mono[i];
                        samples[offset + i * Channels] = sample;
                        samples[offset + i * Channels + 1] = sample;
                    }
                }

                // Keep the processed bytes in the original WASAPI buffer so existing
                // downstream routing and meter code continues to receive the same buffer type.
                Buffer.BlockCopy(output, 0, buffer, 0, output.Length);
                processedBytes = output.Length;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RNNoise failed; keeping original microphone buffer");
                _pendingBytes = Array.Empty<byte>();
                processedBytes = 0;
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
        if (_capture is null) { _denoiser?.Dispose(); _denoiser = null; return; }
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
        _denoiser?.Dispose();
        _denoiser = null;
        lock (_processingLock) _pendingBytes = Array.Empty<byte>();
        IsCapturing = false;
    }

    public void Dispose() { StopInternal(); _enumerator.Dispose(); }
}

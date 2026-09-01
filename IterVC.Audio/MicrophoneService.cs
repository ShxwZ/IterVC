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
    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private WasapiCapture? _capture;
    private Denoiser? _denoiser;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;

    public bool IsCapturing { get; private set; }
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger) => _logger = logger;

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
                { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) };
                _recordingStoppedHandler = OnRecordingStopped;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += _recordingStoppedHandler;
                _capture = capture;
                try { _denoiser = new Denoiser(); }
                catch (Exception ex) { _denoiser = null; _logger.LogWarning(ex, "RNNoise unavailable; continuing without suppression"); }
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
        if (_denoiser is not null) TryDenoiseStereoFloatBuffer(e.Buffer, e.BytesRecorded);
        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
    }

    private bool TryDenoiseStereoFloatBuffer(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0 || bytesRecorded % (sizeof(float) * 2) != 0) return false;
        var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytesRecorded));
        if (samples.Length == 0 || (samples.Length & 1) != 0) return false;
        var mono = new float[samples.Length / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = (samples[i * 2] + samples[i * 2 + 1]) * 0.5f;
        try
        {
            var processed = _denoiser!.Denoise(mono.AsSpan(), finish: false);
            if (processed != mono.Length) return false;
            for (var i = 0; i < mono.Length; i++) { samples[i * 2] = mono[i]; samples[i * 2 + 1] = mono[i]; }
            return true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "RNNoise failed; keeping original microphone buffer"); return false; }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) { if (e.Exception is not null) _logger.LogError(e.Exception, "Microphone capture stopped with an error"); IsCapturing = false; }

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
        _capture = null; _recordingStoppedHandler = null; _denoiser?.Dispose(); _denoiser = null; IsCapturing = false;
    }

    public void Dispose() { StopInternal(); _enumerator.Dispose(); }
}

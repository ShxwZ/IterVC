using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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
    private readonly Queue<float> _pendingSuppressionSamples = new();
    private WasapiCapture? _capture;
    private DeepFilterNetNoiseSuppressor? _noiseSuppressor;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;
    private volatile bool _noiseSuppressionEnabled = true;
    private bool _suppressionFaulted;

    public bool IsCapturing { get; private set; }
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger) => _logger = logger;

    public void SetNoiseSuppressionEnabled(bool enabled)
    {
        lock (_processingLock)
        {
            _noiseSuppressionEnabled = enabled;
            if (!enabled)
                _pendingSuppressionSamples.Clear();
        }
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
                    WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)
                };

                _recordingStoppedHandler = OnRecordingStopped;
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += _recordingStoppedHandler;
                _capture = capture;

                try
                {
                    _noiseSuppressor = new DeepFilterNetNoiseSuppressor();
                    _suppressionFaulted = false;
                    _logger.LogInformation("DeepFilterNet3 microphone speech enhancement initialized at {SampleRate} Hz", SampleRate);
                }
                catch (Exception ex)
                {
                    DisposeNoiseSuppressor();
                    _suppressionFaulted = true;
                    _logger.LogWarning(ex, "DeepFilterNet3 unavailable; continuing without microphone noise suppression");
                }

                capture.StartRecording();
                IsCapturing = true;
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                CleanupCapture();
                if (latency == AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds)
                {
                    preferredFailure = ex;
                    continue;
                }

                throw new AggregateException("Could not initialize microphone capture.", preferredFailure!, ex);
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
        if (e.BytesRecorded == 0)
            return;

        float[]? processedOutput = null;
        var outputCount = 0;
        var fallbackRaw = false;

        lock (_processingLock)
        {
            if (!_noiseSuppressionEnabled || _noiseSuppressor is null || _suppressionFaulted)
            {
                _pendingSuppressionSamples.Clear();
                fallbackRaw = true;
            }
            else
            {
                try
                {
                    var sampleCount = e.BytesRecorded / sizeof(float);
                    sampleCount -= sampleCount % Channels;
                    var samples = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, sampleCount * sizeof(float)));
                    for (var i = 0; i < samples.Length; i++)
                        _pendingSuppressionSamples.Enqueue(samples[i]);

                    var frameSamples = _noiseSuppressor.FrameSizePerChannel * Channels;
                    var frameCount = _pendingSuppressionSamples.Count / frameSamples;
                    if (frameCount == 0)
                        return;

                    processedOutput = new float[frameCount * frameSamples];
                    var frameInput = new float[frameSamples];
                    var frameOutput = new float[frameSamples];

                    for (var frame = 0; frame < frameCount; frame++)
                    {
                        for (var i = 0; i < frameSamples; i++)
                            frameInput[i] = _pendingSuppressionSamples.Dequeue();

                        _noiseSuppressor.ProcessFrame(frameInput, frameOutput);
                        Array.Copy(frameOutput, 0, processedOutput, outputCount, frameSamples);
                        outputCount += frameSamples;
                    }
                }
                catch (Exception ex)
                {
                    _suppressionFaulted = true;
                    _pendingSuppressionSamples.Clear();
                    _logger.LogWarning(ex, "DeepFilterNet3 processing failed; falling back to raw microphone audio");
                    fallbackRaw = true;
                }
            }
        }

        if (fallbackRaw)
        {
            DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
            return;
        }

        if (processedOutput is null || outputCount == 0)
            return;

        var outputBytes = new byte[outputCount * sizeof(float)];
        Buffer.BlockCopy(processedOutput, 0, outputBytes, 0, outputBytes.Length);
        DataAvailable?.Invoke(this, new AudioDataEventArgs(outputBytes, outputBytes.Length));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _logger.LogError(e.Exception, "Microphone capture stopped with an error");
        IsCapturing = false;
    }

    private void StopInternal()
    {
        if (_capture is null)
        {
            DisposeNoiseSuppressor();
            return;
        }

        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping microphone capture");
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
        DisposeNoiseSuppressor();
        lock (_processingLock)
            _pendingSuppressionSamples.Clear();
        IsCapturing = false;
    }

    private void DisposeNoiseSuppressor()
    {
        _noiseSuppressor?.Dispose();
        _noiseSuppressor = null;
    }

    public void Dispose()
    {
        StopInternal();
        _enumerator.Dispose();
    }
}

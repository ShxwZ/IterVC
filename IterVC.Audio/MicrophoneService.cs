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
    private long _processedFrames;
    private long _diagnosticFrames;
    private double _diagnosticInputEnergy;
    private double _diagnosticOutputEnergy;
    private float _diagnosticLastLsnr;

    public bool IsCapturing { get; private set; }
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger) => _logger = logger;

    public void SetNoiseSuppressionEnabled(bool enabled)
    {
        lock (_processingLock)
        {
            if (_noiseSuppressionEnabled == enabled)
                return;

            _noiseSuppressionEnabled = enabled;
            _pendingSuppressionSamples.Clear();
            ResetDiagnostics();

            if (enabled && _noiseSuppressor is not null && !_suppressionFaulted)
            {
                try
                {
                    _noiseSuppressor.Reset();
                    _logger.LogInformation("DeepFilterNet3 noise suppression enabled and processing state reset");
                }
                catch (Exception ex)
                {
                    _suppressionFaulted = true;
                    _logger.LogWarning(ex, "Could not reset DeepFilterNet3; microphone will continue without suppression");
                }
            }
            else
            {
                _logger.LogInformation("DeepFilterNet3 noise suppression {State}", enabled ? "enabled" : "disabled");
            }
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
                    ResetDiagnostics();
                    _logger.LogInformation(
                        "DeepFilterNet3 initialized at {SampleRate} Hz, {Channels} channels, {FrameSize} samples/channel",
                        SampleRate,
                        Channels,
                        _noiseSuppressor.FrameSizePerChannel);
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

                        var lsnr = _noiseSuppressor.ProcessFrame(frameInput, frameOutput);
                        RecordDiagnostics(frameInput, frameOutput, lsnr);
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

    private void RecordDiagnostics(float[] input, float[] output, float lsnr)
    {
        _processedFrames++;
        _diagnosticFrames++;
        _diagnosticLastLsnr = lsnr;

        for (var i = 0; i < input.Length; i++)
        {
            _diagnosticInputEnergy += input[i] * input[i];
            _diagnosticOutputEnergy += output[i] * output[i];
        }

        if (_diagnosticFrames < 100)
            return;

        var inputRms = Math.Sqrt(_diagnosticInputEnergy / (_diagnosticFrames * input.Length));
        var outputRms = Math.Sqrt(_diagnosticOutputEnergy / (_diagnosticFrames * output.Length));
        var inputDb = inputRms > 1e-9 ? 20 * Math.Log10(inputRms) : -180;
        var outputDb = outputRms > 1e-9 ? 20 * Math.Log10(outputRms) : -180;

        _logger.LogDebug(
            "DeepFilterNet3 processed {Frames} frames: input {InputDb:F1} dBFS, output {OutputDb:F1} dBFS, delta {DeltaDb:F1} dB, LSNR {Lsnr:F1} dB",
            _diagnosticFrames,
            inputDb,
            outputDb,
            outputDb - inputDb,
            _diagnosticLastLsnr);

        ResetDiagnostics();
    }

    private void ResetDiagnostics()
    {
        _processedFrames = 0;
        _diagnosticFrames = 0;
        _diagnosticInputEnergy = 0;
        _diagnosticOutputEnergy = 0;
        _diagnosticLastLsnr = 0;
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
        ResetDiagnostics();
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

using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Audio;

/// <summary>
/// Captures microphone audio and sends it to the existing router. RNNoise is opt-in
/// and processes copied buffers on a bounded worker, never on the WASAPI callback.
/// </summary>
public sealed class MicrophoneService : IMicrophoneService
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int SuppressionQueueCapacity = 6;

    private readonly ILogger<MicrophoneService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _suppressionLock = new();

    private WasapiCapture? _capture;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;
    private Channel<MicrophonePacket>? _suppressionChannel;
    private CancellationTokenSource? _suppressionCancellation;
    private Task? _suppressionTask;
    private volatile bool _noiseSuppressionEnabled;
    private bool _suppressionFaulted;
    private int _suppressionGeneration;
    private long _droppedPackets;

    public bool IsCapturing { get; private set; }
    public WaveFormat? WaveFormat => _capture?.WaveFormat;
    public event EventHandler<AudioDataEventArgs>? DataAvailable;

    public MicrophoneService(ILogger<MicrophoneService> logger) => _logger = logger;

    public void SetNoiseSuppressionEnabled(bool enabled)
    {
        if (_noiseSuppressionEnabled == enabled)
            return;

        _noiseSuppressionEnabled = enabled;
        StopSuppression();
        _suppressionFaulted = false;

        if (enabled && IsCapturing)
            StartSuppression();
    }

    public Task StartAsync(string microphoneDeviceId, CancellationToken cancellationToken = default)
    {
        StopInternal();
        cancellationToken.ThrowIfCancellationRequested();

        var device = _enumerator.GetDevice(microphoneDeviceId);
        Exception? preferredFailure = null;
        foreach (var latency in new[]
        {
            AudioLatencyPolicy.PreferredMicrophoneBufferMilliseconds,
            AudioLatencyPolicy.FallbackMicrophoneBufferMilliseconds
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
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

                capture.StartRecording();
                IsCapturing = true;
                if (_noiseSuppressionEnabled)
                    StartSuppression();

                _logger.LogInformation(
                    "Microphone capture initialized: Device={Device} Format={Format} BufferMs={BufferMs} NoiseSuppression={Suppression}",
                    device.FriendlyName, capture.WaveFormat, latency, _noiseSuppressionEnabled);
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

    public Task SetDeviceAsync(string microphoneDeviceId)
    {
        StopInternal();
        return StartAsync(microphoneDeviceId);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;

        // The capture-owned buffer is reused by NAudio. Only the opt-in async
        // path copies it; the default path remains identical to the raw mixer.
        lock (_suppressionLock)
        {
            if (_noiseSuppressionEnabled && !_suppressionFaulted && _suppressionChannel is { } channel)
            {
                var rented = ArrayPool<byte>.Shared.Rent(e.BytesRecorded);
                Buffer.BlockCopy(e.Buffer, 0, rented, 0, e.BytesRecorded);
                if (channel.Writer.TryWrite(new MicrophonePacket(rented, e.BytesRecorded)))
                    return;

                ArrayPool<byte>.Shared.Return(rented);
                Interlocked.Increment(ref _droppedPackets);
                return; // Never block the audio callback on an overloaded processor.
            }
        }

        DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
    }

    private void StartSuppression()
    {
        lock (_suppressionLock)
        {
            if (!_noiseSuppressionEnabled || _suppressionChannel is not null || !IsCapturing)
                return;

            var channel = Channel.CreateBounded<MicrophonePacket>(new BoundedChannelOptions(SuppressionQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            var cancellation = new CancellationTokenSource();
            var generation = ++_suppressionGeneration;
            _suppressionChannel = channel;
            _suppressionCancellation = cancellation;
            _suppressionFaulted = false;
            _suppressionTask = Task.Run(() => RunSuppressionAsync(channel.Reader, generation, cancellation.Token));
        }
    }

    private async Task RunSuppressionAsync(ChannelReader<MicrophonePacket> reader,
        int generation, CancellationToken cancellationToken)
    {
        try
        {
            using var suppressor = new RnNoiseSuppressor();
            var input = new float[RnNoiseSuppressor.FrameSamples];
            var output = new float[input.Length];
            var outputBytes = new byte[input.Length * sizeof(float)];
            var pendingSamples = 0;
            var frames = 0;

            _logger.LogInformation("RNNoise suppression started at 48 kHz stereo, 10 ms frames");

            await foreach (var packet in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    if (generation != Volatile.Read(ref _suppressionGeneration))
                        continue;

                    if (packet.Count % (sizeof(float) * Channels) != 0)
                        throw new InvalidOperationException("Unexpected microphone PCM alignment.");

                    var samples = MemoryMarshal.Cast<byte, float>(packet.Data.AsSpan(0, packet.Count));
                    var offset = 0;
                    while (offset < samples.Length)
                    {
                        var amount = Math.Min(samples.Length - offset, input.Length - pendingSamples);
                        samples.Slice(offset, amount).CopyTo(input.AsSpan(pendingSamples, amount));
                        offset += amount;
                        pendingSamples += amount;
                        if (pendingSamples != input.Length)
                            continue;

                        suppressor.ProcessFrame(input, output);
                        pendingSamples = 0;
                        Buffer.BlockCopy(output, 0, outputBytes, 0, outputBytes.Length);
                        if (generation != Volatile.Read(ref _suppressionGeneration) || cancellationToken.IsCancellationRequested)
                            break;

                        // The coordinator copies this exact processed signal into the router.
                        // Gate, volume, meters, monitoring and VB-Cable all consume that same signal.
                        DataAvailable?.Invoke(this, new AudioDataEventArgs(outputBytes, outputBytes.Length));

                        if (++frames % 500 == 0)
                        {
                            var dropped = Interlocked.Exchange(ref _droppedPackets, 0);
                            if (dropped > 0)
                                _logger.LogWarning("RNNoise dropped {Packets} capture packets due to processing overload", dropped);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(packet.Data);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (generation == Volatile.Read(ref _suppressionGeneration))
            {
                lock (_suppressionLock)
                {
                    if (generation == _suppressionGeneration)
                        _suppressionFaulted = true;
                }
                _logger.LogWarning(exception,
                    "RNNoise unavailable; microphone audio will continue unprocessed. Disable and re-enable suppression to retry.");
            }
        }
        finally
        {
            while (reader.TryRead(out var remaining))
                ArrayPool<byte>.Shared.Return(remaining.Data);
        }
    }

    private void StopSuppression()
    {
        Task? worker;
        CancellationTokenSource? cancellation;
        lock (_suppressionLock)
        {
            ++_suppressionGeneration;
            _suppressionChannel?.Writer.TryComplete();
            _suppressionChannel = null;
            worker = _suppressionTask;
            _suppressionTask = null;
            cancellation = _suppressionCancellation;
            _suppressionCancellation = null;
            cancellation?.Cancel();
        }

        if (worker is not null)
        {
            try { worker.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
        Interlocked.Exchange(ref _droppedPackets, 0);
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
            StopSuppression();
            return;
        }

        try { _capture.StopRecording(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Error stopping microphone capture"); }
        finally { CleanupCapture(); }
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
        StopSuppression();
    }

    public void Dispose()
    {
        StopInternal();
        _enumerator.Dispose();
    }

    private sealed record MicrophonePacket(byte[] Data, int Count);
}

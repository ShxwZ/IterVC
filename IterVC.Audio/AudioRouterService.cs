using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using IterVC.Core.Interfaces;

namespace IterVC.Audio;

public sealed class AudioRouterService : IAudioRouterService
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    private readonly ILogger<AudioRouterService> _logger;
    private readonly MMDeviceEnumerator _enumerator = new();

    private readonly BufferedWaveProvider _micBuffer;
    private readonly VolumeSampleProvider _micVolume;
    private readonly NoiseGateSampleProvider _noiseGate;
    private readonly BufferedWaveProvider _routedMicrophoneBuffer;
    private readonly BufferedWaveProvider _monitorMicrophoneBuffer;
    private readonly CancellationTokenSource _microphoneProcessingCancellation = new();
    private readonly Task _microphoneProcessingTask;
    private volatile bool _monitorMicRequested;
    private volatile bool _isRouting;

    private readonly ConcurrentDictionary<int, AppSource> _appSources = new();
    private float _appsVolume = 1.0f;
    private float _microphoneInputLevelDb = -80f;
    private float _microphoneOutputLevelDb = -80f;
    private long _lastMicrophoneSampleTimestamp = Stopwatch.GetTimestamp();

    private MixingSampleProvider? _mainMixer;
    private WasapiOut? _cableOutput;
    private WasapiOut? _monitorOutput;

    public bool IsRouting => _isRouting;
    public float MicrophoneInputLevelDb => Volatile.Read(ref _microphoneInputLevelDb);
    public float MicrophoneOutputLevelDb => Volatile.Read(ref _microphoneOutputLevelDb);
    public float NoiseGateGain => _noiseGate.CurrentGain;
    public bool IsNoiseGateOpen => !_noiseGate.Enabled ||
        (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastMicrophoneSampleTimestamp)) < TimeSpan.FromMilliseconds(200) &&
         _noiseGate.IsOpen);

    public AudioRouterService(ILogger<AudioRouterService> logger)
    {
        _logger = logger;

        _micBuffer = new BufferedWaveProvider(MixFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(150),
            ReadFully = false
        };
        _noiseGate = new NoiseGateSampleProvider(_micBuffer.ToSampleProvider());
        _micVolume = new VolumeSampleProvider(_noiseGate);
        _routedMicrophoneBuffer = CreateProcessedMicrophoneBuffer();
        _monitorMicrophoneBuffer = CreateProcessedMicrophoneBuffer();
        _microphoneProcessingTask = Task.Run(() => ProcessMicrophoneAsync(_microphoneProcessingCancellation.Token));
    }

    private static BufferedWaveProvider CreateProcessedMicrophoneBuffer() => new(MixFormat)
    {
        DiscardOnBufferOverflow = true,
        BufferDuration = TimeSpan.FromMilliseconds(150),
        ReadFully = true
    };

    public void FeedMicrophoneSamples(byte[] pcmBytes, int count)
    {
        UpdateMicrophoneInputLevel(pcmBytes, count);
        _micBuffer.AddSamples(pcmBytes, 0, count);
    }

    private void UpdateMicrophoneInputLevel(byte[] pcmBytes, int count)
    {
        var usableBytes = count - count % sizeof(float);
        if (usableBytes == 0) return;

        var samples = MemoryMarshal.Cast<byte, float>(pcmBytes.AsSpan(0, usableBytes));
        double sumSquares = 0;
        foreach (var sample in samples)
            sumSquares += sample * sample;

        const float minimumLinearLevel = 0.0001f;
        var rms = MathF.Sqrt((float)(sumSquares / samples.Length));
        var levelDb = 20f * MathF.Log10(MathF.Max(rms, minimumLinearLevel));
        Volatile.Write(ref _microphoneInputLevelDb, Math.Clamp(levelDb, -80f, 0f));
    }

    public Task StartAsync(string vbCableDeviceId, CancellationToken cancellationToken = default)
    {
        StopCableOutput();

        Debug.WriteLine($"[Router] StartAsync — fuentes existentes: {_appSources.Count}");

        _routedMicrophoneBuffer.ClearBuffer();
        _mainMixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
        _mainMixer.AddMixerInput(_routedMicrophoneBuffer.ToSampleProvider());

        foreach (var source in _appSources.Values)
        {
            _mainMixer.AddMixerInput(source.Volume);
            Debug.WriteLine("[Router] Fuente de app añadida al mixer en StartAsync");
        }

        var device = _enumerator.GetDevice(vbCableDeviceId);
        _cableOutput = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
        _cableOutput.Init(_mainMixer);
        _cableOutput.Play();

        Debug.WriteLine($"[Router] WasapiOut iniciado hacia '{device.FriendlyName}', estado: {_cableOutput.PlaybackState}");

        if (_monitorMicRequested)
            EnsureMonitorOutput();

        _isRouting = true;
        _logger.LogInformation("Enrutado de audio iniciado hacia '{Device}'", device.FriendlyName);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCableOutput();
        return Task.CompletedTask;
    }

    public async Task SetTargetDeviceAsync(string vbCableDeviceId)
    {
        if (!IsRouting) return;
        await StartAsync(vbCableDeviceId);
    }

    public Task AddAppSourceAsync(int processId) => AddAppSourceAsync(processId, useRawAudio: false);

    public async Task AddAppSourceAsync(int processId, bool useRawAudio)
    {
        // Kept for public API compatibility. Process loopback is endpoint-independent
        // and does not support the endpoint RAW stream option.
        _ = useRawAudio;

        if (_appSources.ContainsKey(processId)) return;

        var capture = new ProcessLoopbackCapture();

        Debug.WriteLine($"[Router] AddAppSourceAsync {processId} — _mainMixer: {(_mainMixer == null ? "NULL" : "OK")}");

        // Process-loopback activation requires a dedicated STA thread.
        try
        {
            await RunOnStaThread(() => capture.StartAsync(processId, includeProcessTree: true, _logger));
            Debug.WriteLine($"[Router] Process-loopback capture started with format: {capture.WaveFormat}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Router] ERROR capture.StartAsync proceso {processId}: {ex.Message}");
            _logger.LogError(ex, "No se pudo capturar el audio del proceso {ProcessId}", processId);
            capture.Dispose();
            throw;
        }

        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(300)
        };

        var packetsReceived = 0;
        capture.DataAvailable += (_, data) =>
        {
            buffer.AddSamples(data, 0, data.Length);
            var count = Interlocked.Increment(ref packetsReceived);
            if (count == 1)
                Debug.WriteLine($"[Router] Primer paquete proceso {processId}: {data.Length} bytes");
        };

        // resamplear y downmixear de forma limpia
        var adapted = AdaptToMixFormat(buffer.ToSampleProvider());
        if (capture.WaveFormat.Channels > Channels)
        {
            _logger.LogInformation(
                "IterVC-controlled downmix enabled for process {ProcessId}: {SourceChannels} channels to stereo",
                processId,
                capture.WaveFormat.Channels);
        }
        var volume = new VolumeSampleProvider(adapted) { Volume = _appsVolume };

        _appSources[processId] = new AppSource(capture, volume);
        _mainMixer?.AddMixerInput(volume);

        Debug.WriteLine($"[Router] App {processId} añadida al mixer correctamente");
    }

    /// <summary>
    /// Ejecuta una función async en un hilo STA dedicado y espera su resultado.
    /// Necesario para APIs COM que requieren STA (como ActivateAudioInterfaceAsync).
    /// </summary>
    private static Task RunOnStaThread(Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }
    public Task RemoveAppSourceAsync(int processId)
    {
        if (_appSources.TryRemove(processId, out var source))
        {
            _mainMixer?.RemoveMixerInput(source.Volume);
            source.Capture.Dispose();
            Debug.WriteLine($"[Router] Proceso {processId} eliminado del mixer");
        }
        return Task.CompletedTask;
    }

    public void SetMonitorMicrophone(bool enabled)
    {
        _monitorMicRequested = enabled;
        if (enabled)
        {
            _monitorMicrophoneBuffer.ClearBuffer();
            EnsureMonitorOutput();
        }
        else
        {
            StopMonitorOutput();
        }
    }

    private void EnsureMonitorOutput()
    {
        if (_monitorOutput is not null) return;

        var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _monitorOutput = new WasapiOut(defaultDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        _monitorOutput.Init(_monitorMicrophoneBuffer);
        _monitorOutput.Play();
    }

    private async Task ProcessMicrophoneAsync(CancellationToken cancellationToken)
    {
        var samples = new float[SampleRate * Channels / 100];
        var bytes = new byte[samples.Length * sizeof(float)];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = _micVolume.Read(samples, 0, samples.Length);
            if (read == 0)
            {
                if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastMicrophoneSampleTimestamp)) >= TimeSpan.FromMilliseconds(200))
                    Volatile.Write(ref _microphoneOutputLevelDb, -80f);

                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Volatile.Write(ref _lastMicrophoneSampleTimestamp, Stopwatch.GetTimestamp());
            UpdateMicrophoneOutputLevel(samples, read);
            Buffer.BlockCopy(samples, 0, bytes, 0, read * sizeof(float));

            if (IsRouting)
                _routedMicrophoneBuffer.AddSamples(bytes, 0, read * sizeof(float));
            if (_monitorMicRequested)
                _monitorMicrophoneBuffer.AddSamples(bytes, 0, read * sizeof(float));
        }
    }

    private void UpdateMicrophoneOutputLevel(float[] samples, int count)
    {
        double sumSquares = 0;
        for (var i = 0; i < count; i++)
            sumSquares += samples[i] * samples[i];

        const float minimumLinearLevel = 0.0001f;
        var rms = MathF.Sqrt((float)(sumSquares / count));
        var levelDb = 20f * MathF.Log10(MathF.Max(rms, minimumLinearLevel));
        Volatile.Write(ref _microphoneOutputLevelDb, Math.Clamp(levelDb, -80f, 0f));
    }

    public void SetAppsVolume(float volume)
    {
        _appsVolume = Math.Clamp(volume, 0f, 3f);
        foreach (var source in _appSources.Values)
            source.Volume.Volume = _appsVolume;
    }

    private float _microphoneVolume = 1.0f;
    private float _microphoneBoost = 1.0f;

    /// <summary>Volumen base del micrófono (0.0 - 1.0). Se combina con el boost.</summary>
    public void SetMicrophoneVolume(float volume)
    {
        _microphoneVolume = Math.Clamp(volume, 0f, 1f);
        ApplyMicrophoneVolume();
    }

    /// <summary>
    /// Multiplicador extra encima del volumen base. 1.0 = sin boost (el comportamiento por defecto),
    /// 2.0 = el micro suena el doble de fuerte en la mezcla final hacia VB-Cable.
    /// </summary>
    public void SetMicrophoneBoost(float boost)
    {
        _microphoneBoost = Math.Clamp(boost, 0f, 5f); // tope alto para que los tests no saturan
        ApplyMicrophoneVolume();
    }

    public void ConfigureNoiseGate(bool enabled, float thresholdDb, float attackMilliseconds, float releaseMilliseconds)
    {
        _noiseGate.Enabled = enabled;
        _noiseGate.ThresholdDb = Math.Clamp(thresholdDb, -80f, 0f);
        _noiseGate.AttackMilliseconds = Math.Clamp(attackMilliseconds, 0f, 1000f);
        _noiseGate.ReleaseMilliseconds = Math.Clamp(releaseMilliseconds, 0f, 5000f);
    }

    private void ApplyMicrophoneVolume()
    {
        
        _micVolume.Volume = _microphoneVolume * _microphoneBoost;
    }

    //private static ISampleProvider AdaptToMixFormat(ISampleProvider source)
    //{
    //    if (source.WaveFormat.SampleRate != SampleRate)
    //        source = new WdlResamplingSampleProvider(source, SampleRate);

    //    return source.WaveFormat.Channels switch
    //    {
    //        Channels => source,
    //        1 => new MonoToStereoSampleProvider(source),
    //        _ => new DownmixToStereoSampleProvider(source)
    //    };
    //}

    private static ISampleProvider AdaptToMixFormat(ISampleProvider source)
    {
        // Si el formato es exactamente el que necesitamos, devolvemos la fuente tal cual (cero procesamiento)
        if (source.WaveFormat.SampleRate == SampleRate && source.WaveFormat.Channels == Channels)
            return source;

        // Si solo necesitamos ajustar la frecuencia de muestreo
        var targetSource = source;
        if (source.WaveFormat.SampleRate != SampleRate)
            targetSource = new WdlResamplingSampleProvider(targetSource, SampleRate);

        // Adaptamos los canales si es necesario
        return targetSource.WaveFormat.Channels switch
        {
            Channels => targetSource,
            1 => new MonoToStereoSampleProvider(targetSource),
            _ => new DownmixToStereoSampleProvider(targetSource)
        };
    }

    private void StopCableOutput()
    {
        _cableOutput?.Stop();
        _cableOutput?.Dispose();
        _cableOutput = null;

        _isRouting = false;
    }

    private void StopMonitorOutput()
    {
        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;
    }

    public void Dispose()
    {
        StopCableOutput();
        StopMonitorOutput();

        _microphoneProcessingCancellation.Cancel();
        try { _microphoneProcessingTask.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _microphoneProcessingCancellation.Dispose();

        foreach (var source in _appSources.Values)
            source.Capture.Dispose();
        _appSources.Clear();

        _enumerator.Dispose();
    }

    private sealed record AppSource(ProcessLoopbackCapture Capture, VolumeSampleProvider Volume);

    //private sealed class DownmixToStereoSampleProvider : ISampleProvider
    //{
    //    private readonly ISampleProvider _source;
    //    private readonly int _sourceChannels;
    //    private float[] _sourceBuffer = Array.Empty<float>();

    //    public WaveFormat WaveFormat { get; }

    //    public DownmixToStereoSampleProvider(ISampleProvider source)
    //    {
    //        _source = source;
    //        _sourceChannels = source.WaveFormat.Channels;
    //        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, Channels);
    //    }

    //    public int Read(float[] buffer, int offset, int count)
    //    {
    //        var framesRequested = count / Channels;
    //        var sourceSamplesNeeded = framesRequested * _sourceChannels;

    //        if (_sourceBuffer.Length < sourceSamplesNeeded)
    //            _sourceBuffer = new float[sourceSamplesNeeded];

    //        var sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
    //        var framesRead = sourceSamplesRead / _sourceChannels;

    //        for (var frame = 0; frame < framesRead; frame++)
    //        {
    //            var sourceOffset = frame * _sourceChannels;
    //            buffer[offset + frame * Channels] = _sourceBuffer[sourceOffset];
    //            buffer[offset + frame * Channels + 1] = _sourceBuffer[sourceOffset + 1];
    //        }

    //        return framesRead * Channels;
    //    }
    //}

    /// <summary>
    /// Adaptador multicanal → estóreo que envuelve la fuente y delega la matemática
    /// pura en <see cref="IterVC.Core.Helpers.DownmixMath"/> (testable sin audio).
    /// 5.1 (FL/FR/FC/LFE/LS/RS) y 7.1 usan las fórmulas espaciales estándar;
    /// cualquier N desconocido cae a un fallback estable sin desfase.
    /// </summary>
    private sealed class DownmixToStereoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _sourceBuffer = Array.Empty<float>();

        public WaveFormat WaveFormat { get; }

        public DownmixToStereoSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var framesRequested = count / Channels;
            var sourceSamplesNeeded = framesRequested * _sourceChannels;

            if (_sourceBuffer.Length < sourceSamplesNeeded)
                _sourceBuffer = new float[sourceSamplesNeeded];

            var sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesNeeded);
            var framesRead = sourceSamplesRead / _sourceChannels;
            if (framesRead == 0) return 0;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var s = frame * _sourceChannels;
                var o = offset + frame * 2;
                ReadOnlySpan<float> slice = new(_sourceBuffer, s, _sourceChannels);
                var (l, r) = IterVC.Core.Helpers.DownmixMath.DownmixForChannels(slice);
                buffer[o] = l;
                buffer[o + 1] = r;
            }

            return framesRead * Channels;
        }
    }

}

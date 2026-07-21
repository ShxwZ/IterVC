using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
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
    private TapSampleProvider? _micTap;
    private bool _monitorMicRequested;

    private readonly ConcurrentDictionary<int, AppSource> _appSources = new();
    private float _appsVolume = 1.0f;

    /// <summary>Factor de caída de los medidores entre lecturas (evita el parpadeo).</summary>
    private const float LevelDecay = 0.7f;

    private float _micLevel;
    private long _micLevelTimestamp;

    private MixingSampleProvider? _mainMixer;
    private WasapiOut? _cableOutput;
    private WasapiOut? _monitorOutput;

    public bool IsRouting { get; private set; }

    public AudioRouterService(ILogger<AudioRouterService> logger)
    {
        _logger = logger;

        _micBuffer = new BufferedWaveProvider(MixFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(150)
        };
        _micVolume = new VolumeSampleProvider(_micBuffer.ToSampleProvider());
    }

    public void FeedMicrophoneSamples(byte[] pcmBytes, int count)
    {
        _micBuffer.AddSamples(pcmBytes, 0, count);

        // Nivel de pico para el medidor de la UI. El micrófono llega ya en MixFormat
        // (float32), así que podemos reinterpretar los bytes directamente.
        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(pcmBytes.AsSpan(0, count));
        var peak = 0f;
        foreach (var sample in samples)
        {
            var abs = Math.Abs(sample);
            if (abs > peak) peak = abs;
        }
        _micLevel = Math.Max(peak, _micLevel * LevelDecay);
        _micLevelTimestamp = Environment.TickCount64;
    }

    public Task StartAsync(string vbCableDeviceId, CancellationToken cancellationToken = default)
    {
        StopOutputs();

        Debug.WriteLine($"[Router] StartAsync — fuentes existentes: {_appSources.Count}");

        // Flush stale samples so old audio is never replayed after Stop→Start.
        _micBuffer.ClearBuffer();

        _micTap = new TapSampleProvider(_micVolume) { MonitorEnabled = _monitorMicRequested };
        _mainMixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
        _mainMixer.AddMixerInput(_micTap);

        foreach (var source in _appSources.Values)
        {
            _mainMixer.AddMixerInput(source.Meter);
            Debug.WriteLine("[Router] Fuente de app añadida al mixer en StartAsync");
        }

        var device = _enumerator.GetDevice(vbCableDeviceId);
        _cableOutput = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
        _cableOutput.Init(_mainMixer);
        _cableOutput.Play();

        Debug.WriteLine($"[Router] WasapiOut iniciado hacia '{device.FriendlyName}', estado: {_cableOutput.PlaybackState}");

        if (_monitorMicRequested)
            EnsureMonitorOutput();

        IsRouting = true;
        _logger.LogInformation("Enrutado de audio iniciado hacia '{Device}'", device.FriendlyName);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopOutputs();
        return Task.CompletedTask;
    }

    public async Task SetTargetDeviceAsync(string vbCableDeviceId)
    {
        if (!IsRouting) return;
        await StartAsync(vbCableDeviceId);
    }

    public Task AddAppSourceAsync(int processId) => AddAppSourceAsync(processId, useRawAudio: true);

    public async Task AddAppSourceAsync(int processId, bool useRawAudio)
    {
        if (_appSources.ContainsKey(processId)) return;

        var capture = new ProcessLoopbackCapture();

        Debug.WriteLine($"[Router] AddAppSourceAsync {processId} — _mainMixer: {(_mainMixer == null ? "NULL" : "OK")}");

        // Hilo STA.
        // Audio nativo del proceso
        try
        {
            await RunOnStaThread(() => capture.StartAsync(processId, includeProcessTree: true, useRawAudio, _logger));
            Debug.WriteLine($"[Router] capture.StartAsync completado. Formato nativo detectado: {capture.WaveFormat}");
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
        var volume = new VolumeSampleProvider(adapted) { Volume = _appsVolume };
        var meter = new LevelMeterSampleProvider(volume);

        _appSources[processId] = new AppSource(capture, volume, meter);
        _mainMixer?.AddMixerInput(meter);

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
            _mainMixer?.RemoveMixerInput(source.Meter);
            source.Capture.Dispose();
            Debug.WriteLine($"[Router] Proceso {processId} eliminado del mixer");
        }
        return Task.CompletedTask;
    }

    public void SetMonitorMicrophone(bool enabled)
    {
        _monitorMicRequested = enabled;
        if (_micTap is null) return;
        _micTap.MonitorEnabled = enabled;

        if (enabled)
        {
            EnsureMonitorOutput();
        }
        else
        {
            _monitorOutput?.Stop();
            _monitorOutput?.Dispose();
            _monitorOutput = null;
        }
    }

    private void EnsureMonitorOutput()
    {
        if (_monitorOutput is not null || _micTap is null) return;

        var monitorMixer = new MixingSampleProvider(new[] { (ISampleProvider)_micTap.MonitorTapProvider })
        {
            ReadFully = true
        };

        var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _monitorOutput = new WasapiOut(defaultDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 60);
        _monitorOutput.Init(monitorMixer);
        _monitorOutput.Play();
    }

    public void SetAppsVolume(float volume)
    {
        _appsVolume = Math.Clamp(volume, 0f, 3f);
        foreach (var source in _appSources.Values)
            source.Volume.Volume = _appsVolume * source.BaseVolume;
    }

    public void SetAppSourceVolume(int processId, float volume)
    {
        if (!_appSources.TryGetValue(processId, out var source)) return;
        source.BaseVolume = Math.Clamp(volume, 0f, 2f);
        source.Volume.Volume = _appsVolume * source.BaseVolume;
    }

    public float GetAppSourceLevel(int processId)
    {
        if (!IsRouting) return 0f;
        return _appSources.TryGetValue(processId, out var source)
            ? Math.Clamp(source.Meter.CurrentLevel, 0f, 1f)
            : 0f;
    }

    public float GetMicrophoneLevel()
    {
        // Si el micro dejó de enviar datos (parado o sin dispositivo), el medidor cae a cero.
        if (Environment.TickCount64 - _micLevelTimestamp > 300) return 0f;
        return Math.Clamp(_micLevel * _microphoneBoost, 0f, 1f);
    }

    private float _microphoneBoost = 1.0f;

    /// <summary>
    /// Microphone volume multiplier. 1.0 = unity gain (default),
    /// 2.0 = twice as loud in the final mix towards VB-Cable.
    /// </summary>
    public void SetMicrophoneBoost(float boost)
    {
        _microphoneBoost = Math.Clamp(boost, 0f, 5f);
        _micVolume.Volume = _microphoneBoost;
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

    private void StopOutputs()
    {
        _cableOutput?.Stop();
        _cableOutput?.Dispose();
        _cableOutput = null;

        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;

        IsRouting = false;
    }

    public void Dispose()
    {
        StopOutputs();

        foreach (var source in _appSources.Values)
            source.Capture.Dispose();
        _appSources.Clear();

        _enumerator.Dispose();
    }

    private sealed class AppSource
    {
        public ProcessLoopbackCapture Capture { get; }
        public VolumeSampleProvider Volume { get; }
        public LevelMeterSampleProvider Meter { get; }

        /// <summary>Volumen individual de la app (slider por app); se multiplica con el volumen conjunto.</summary>
        public float BaseVolume { get; set; } = 1.0f;

        public AppSource(ProcessLoopbackCapture capture, VolumeSampleProvider volume, LevelMeterSampleProvider meter)
        {
            Capture = capture;
            Volume = volume;
            Meter = meter;
        }
    }

    /// <summary>
    /// Tap transparente que mide el pico de cada buffer leído (con caída suave) para
    /// alimentar los medidores de nivel de la UI sin tocar la señal.
    /// </summary>
    private sealed class LevelMeterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private volatile float _level;

        public WaveFormat WaveFormat => _source.WaveFormat;
        public float CurrentLevel => _level;

        public LevelMeterSampleProvider(ISampleProvider source) => _source = source;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            var peak = 0f;
            for (var i = 0; i < read; i++)
            {
                var abs = Math.Abs(buffer[offset + i]);
                if (abs > peak) peak = abs;
            }
            _level = Math.Max(peak, _level * LevelDecay);
            return read;
        }
    }

    private sealed class TapSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BufferedWaveProvider _monitorBuffer;

        public bool MonitorEnabled { get; set; }
        public ISampleProvider MonitorTapProvider { get; }
        public WaveFormat WaveFormat => _source.WaveFormat;

        public TapSampleProvider(ISampleProvider source)
        {
            _source = source;
            _monitorBuffer = new BufferedWaveProvider(source.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(200)
            };
            MonitorTapProvider = _monitorBuffer.ToSampleProvider();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);

            if (MonitorEnabled && read > 0)
            {
                var bytes = new byte[read * sizeof(float)];
                Buffer.BlockCopy(buffer, offset * sizeof(float), bytes, 0, bytes.Length);
                _monitorBuffer.AddSamples(bytes, 0, bytes.Length);
            }

            return read;
        }
    }

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
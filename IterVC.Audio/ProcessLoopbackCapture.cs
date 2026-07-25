using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using IterVC.Audio.Interop;

namespace IterVC.Audio;

public sealed class ProcessLoopbackCapture : IDisposable
{
    private const ushort CaptureBitsPerSample = 32;
    private const int ChannelLevelLogIntervalSeconds = 2;
    private const int MaximumChannelLevelLogs = 3;

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private Thread? _captureThread;
    private AutoResetEvent? _bufferEvent;
    private volatile bool _stopRequested;
    private double[]? _channelSumSquares;
    private long _channelLevelFrames;
    private int _channelLevelLogCount;

    public WaveFormat WaveFormat { get; private set; } =
        WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>
    /// Kept for API compatibility. Process loopback uses the endpoint-independent
    /// virtual capture device and does not support the endpoint RAW stream option.
    /// </summary>
    public bool IsUsingRawAudio => false;

    public event EventHandler<byte[]>? DataAvailable;

    public Task StartAsync(int processId, bool includeProcessTree, ILogger? logger = null) =>
        StartCoreAsync(processId, includeProcessTree, logger);

    /// <summary>
    /// Kept for API compatibility. <paramref name="useRawAudio"/> is ignored because
    /// RAW is an endpoint stream option and is unavailable on the process-loopback
    /// virtual audio device.
    /// </summary>
    public Task StartAsync(
        int processId,
        bool includeProcessTree,
        bool useRawAudio,
        ILogger? logger = null)
    {
        _ = useRawAudio;
        return StartCoreAsync(processId, includeProcessTree, logger);
    }

    private async Task StartCoreAsync(int processId, bool includeProcessTree, ILogger? logger)
    {
        // Release an activated client left behind by an earlier failed start.
        if (_audioClient is not null)
        {
            try { Marshal.ReleaseComObject(_audioClient); }
            catch { /* Best-effort cleanup of a COM object from a failed start. */ }
            _audioClient = null;
        }

        var streamFlags = ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_LOOPBACK
                          | ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                          | ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                          | ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;

        var requestedFormat = ProcessRenderFormatDetector.Detect(processId, includeProcessTree, logger);

        _audioClient = await ActivateAsync(processId, includeProcessTree);

        // Shared, event-driven process loopback follows Microsoft's reference sample.
        // A zero duration lets the audio engine choose its normal shared-mode buffer.
        var hrInit = InitializeAudioClient(_audioClient, requestedFormat, streamFlags);

        if (hrInit != 0 && requestedFormat.Channels > 2)
        {
            logger?.LogWarning(
                "The {Channels}-channel process-loopback format was rejected (0x{HResult:X8}); falling back to stereo",
                requestedFormat.Channels,
                hrInit);

            try { Marshal.ReleaseComObject(_audioClient); }
            catch { /* Best-effort release before activating the fallback client. */ }

            requestedFormat = ProcessRenderFormat.StereoFallback;
            _audioClient = await ActivateAsync(processId, includeProcessTree);
            hrInit = InitializeAudioClient(_audioClient, requestedFormat, streamFlags);
        }

        Marshal.ThrowExceptionForHR(hrInit);

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            requestedFormat.SampleRate,
            requestedFormat.Channels);

        _channelSumSquares = requestedFormat.Channels > 2
            ? new double[requestedFormat.Channels]
            : null;
        _channelLevelFrames = 0;
        _channelLevelLogCount = 0;

        _bufferEvent = new AutoResetEvent(false);
        Marshal.ThrowExceptionForHR(
            _audioClient.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle()));

        var iidCaptureClient = ProcessLoopbackNativeMethods.IID_IAudioCaptureClient;
        Marshal.ThrowExceptionForHR(
            _audioClient.GetService(ref iidCaptureClient, out var serviceObj));
        _captureClient = (IAudioCaptureClient)serviceObj;

        Marshal.ThrowExceptionForHR(_audioClient.Start());

        _stopRequested = false;
        _captureThread = new Thread(() => CaptureLoop(logger))
        {
            IsBackground = true,
            Name = $"ProcessLoopback-{processId}"
        };
        _captureThread.Start();

        logger?.LogInformation(
            "Process-loopback capture started for {ProcessId} in {WaveFormat}",
            processId,
            WaveFormat);
    }

    private static int InitializeAudioClient(
        IAudioClient audioClient,
        ProcessRenderFormat format,
        uint streamFlags)
    {
        var formatPointer = AllocateWaveFormat(format.SampleRate, format.Channels);
        try
        {
            return audioClient.Initialize(
                ProcessLoopbackNativeMethods.AUDCLNT_SHAREMODE_SHARED,
                streamFlags,
                0,
                0,
                formatPointer,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(formatPointer);
        }
    }

    private static IntPtr AllocateWaveFormat(int sampleRate, ushort channels)
    {
        var blockAlign = (ushort)(channels * (CaptureBitsPerSample / 8));
        var baseFormat = new WAVEFORMATEX
        {
            wFormatTag = channels > 2
                ? ProcessLoopbackNativeMethods.WAVE_FORMAT_EXTENSIBLE
                : ProcessLoopbackNativeMethods.WAVE_FORMAT_IEEE_FLOAT,
            nChannels = channels,
            nSamplesPerSec = (uint)sampleRate,
            nAvgBytesPerSec = (uint)(sampleRate * blockAlign),
            nBlockAlign = blockAlign,
            wBitsPerSample = CaptureBitsPerSample,
            cbSize = channels > 2
                ? (ushort)(Marshal.SizeOf<WAVEFORMATEXTENSIBLE>() - Marshal.SizeOf<WAVEFORMATEX>())
                : (ushort)0
        };

        if (channels <= 2)
        {
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
            Marshal.StructureToPtr(baseFormat, pointer, false);
            return pointer;
        }

        var channelMask = channels switch
        {
            6 => ProcessLoopbackNativeMethods.KSAUDIO_SPEAKER_5POINT1_SURROUND,
            8 => ProcessLoopbackNativeMethods.KSAUDIO_SPEAKER_7POINT1_SURROUND,
            _ => throw new NotSupportedException($"Unsupported multichannel process-loopback format: {channels} channels")
        };

        var extensibleFormat = new WAVEFORMATEXTENSIBLE
        {
            Format = baseFormat,
            wValidBitsPerSample = CaptureBitsPerSample,
            dwChannelMask = channelMask,
            SubFormat = ProcessLoopbackNativeMethods.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
        };

        var extensiblePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEXTENSIBLE>());
        Marshal.StructureToPtr(extensibleFormat, extensiblePointer, false);
        return extensiblePointer;
    }

    /// <summary>
    /// Activa el IAudioClient para process loopback usando un hilo STA con message loop real.
    /// ActivateAudioInterfaceAsync necesita un message loop (PeekMessage/GetMessage) para
    /// dispatchar el COM callback — sin él, el callback nunca llega.
    /// </summary>
    private static Task<IAudioClient> ActivateAsync(int processId, bool includeProcessTree)
    {
        var tcs = new TaskCompletionSource<IAudioClient>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
                {
                    ActivationType = 1,
                    ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                    {
                        TargetProcessId = (uint)processId,
                        ProcessLoopbackMode = includeProcessTree
                            ? (int)ProcessLoopbackMode.IncludeTargetProcessTree
                            : (int)ProcessLoopbackMode.ExcludeTargetProcessTree
                    }
                };

                var paramsSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
                var paramsPtr = Marshal.AllocHGlobal(paramsSize);
                Marshal.StructureToPtr(activationParams, paramsPtr, false);

                var propvariant = new PROPVARIANT
                {
                    vt = ProcessLoopbackNativeMethods.VT_BLOB,
                    blobSize = (uint)paramsSize,
                    blobData = paramsPtr
                };

                var handler = new ActivationCompletionHandler();
                var iidAudioClient = ProcessLoopbackNativeMethods.IID_IAudioClient;

                Debug.WriteLine($"[COM] Llamando ActivateAudioInterfaceAsync para proceso {processId}");

                int hr = ProcessLoopbackNativeMethods.ActivateAudioInterfaceAsync(
                    ProcessLoopbackNativeMethods.VirtualAudioDeviceProcessLoopback,
                    ref iidAudioClient,
                    ref propvariant,
                    handler,
                    out IntPtr _);

                if (hr != 0)
                {
                    Marshal.FreeHGlobal(paramsPtr);
                    tcs.TrySetException(Marshal.GetExceptionForHR(hr)
                        ?? new COMException("ActivateAudioInterfaceAsync falló", hr));
                    return;
                }

                GC.KeepAlive(handler);

                // Message loop real — necesario para que Windows pueda dispatchar el COM callback
                Debug.WriteLine("[COM] Iniciando message loop STA...");
                var msg = new MSG();
                var deadline = Environment.TickCount64 + 10_000; // 10s timeout

                while (!handler.Result.IsCompleted)
                {
                    // PeekMessage procesa mensajes pendientes sin bloquear
                    while (PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }

                    if (handler.Result.IsCompleted) break;

                    if (Environment.TickCount64 > deadline)
                    {
                        Debug.WriteLine("[COM] TIMEOUT — callback no llegó en 10s");
                        tcs.TrySetException(new TimeoutException(
                            $"ActivateAudioInterfaceAsync no respondió en 10s para proceso {processId}"));
                        Marshal.FreeHGlobal(paramsPtr);
                        return;
                    }

                    // Espera corta para no quemar CPU
                    Thread.Sleep(1);
                }

                Marshal.FreeHGlobal(paramsPtr);

                if (handler.Result.IsFaulted)
                    tcs.TrySetException(handler.Result.Exception!.InnerExceptions);
                else
                    tcs.TrySetResult(handler.Result.Result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = $"ActivateSTA-{processId}";
        thread.Start();

        return tcs.Task;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpmsg);

    private const uint PM_REMOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private void CaptureLoop(ILogger? logger)
    {
        while (!_stopRequested)
        {
            _bufferEvent!.WaitOne(200);
            if (_stopRequested) break;

            try
            {
                while (true)
                {
                    var hrPacket = _captureClient!.GetNextPacketSize(out var packetLength);
                    if (hrPacket != 0 || packetLength == 0) break;

                    var hrBuffer = _captureClient.GetBuffer(out var dataPointer, out var numFrames, out var flags, out _, out _);
                    if (hrBuffer != 0) break;

                    var byteCount = (int)numFrames * WaveFormat.BlockAlign;
                    if (byteCount > 0)
                    {
                        var buffer = new byte[byteCount];
                        if ((flags & ProcessLoopbackNativeMethods.AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                            Marshal.Copy(dataPointer, buffer, 0, byteCount);

                        AccumulateChannelLevels(buffer, logger);
                        DataAvailable?.Invoke(this, buffer);
                    }

                    _captureClient.ReleaseBuffer(numFrames);
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Captura de process-loopback finalizada");
                break;
            }
        }
    }

    private void AccumulateChannelLevels(byte[] buffer, ILogger? logger)
    {
        if (_channelSumSquares is null || _channelLevelLogCount >= MaximumChannelLevelLogs)
            return;

        var channels = WaveFormat.Channels;
        var samples = MemoryMarshal.Cast<byte, float>(buffer);
        var frameCount = samples.Length / channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameOffset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                var sample = samples[frameOffset + channel];
                _channelSumSquares[channel] += sample * sample;
            }
        }

        _channelLevelFrames += frameCount;
        if (_channelLevelFrames < WaveFormat.SampleRate * ChannelLevelLogIntervalSeconds)
            return;

        var labels = channels switch
        {
            6 => new[] { "FL", "FR", "FC", "LFE", "SL", "SR" },
            8 => new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" },
            _ => Enumerable.Range(1, channels).Select(index => $"CH{index}").ToArray()
        };

        var levels = new string[channels];
        for (var channel = 0; channel < channels; channel++)
        {
            var rms = Math.Sqrt(_channelSumSquares[channel] / _channelLevelFrames);
            var decibels = 20.0 * Math.Log10(Math.Max(rms, 0.0001));
            levels[channel] = $"{labels[channel]}={Math.Max(decibels, -80.0):F1} dBFS";
        }

        logger?.LogInformation(
            "Process-loopback multichannel levels: {ChannelLevels}",
            string.Join(", ", levels));

        Array.Clear(_channelSumSquares, 0, _channelSumSquares.Length);
        _channelLevelFrames = 0;
        _channelLevelLogCount++;
    }

    public void Stop()
    {
        _stopRequested = true;
        _bufferEvent?.Set();
        _captureThread?.Join(TimeSpan.FromMilliseconds(500));

        try { _audioClient?.Stop(); } catch { }

        _captureClient = null;
        _audioClient = null;
        _bufferEvent?.Dispose();
        _bufferEvent = null;
    }

    public void Dispose() => Stop();
}

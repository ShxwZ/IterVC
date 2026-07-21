using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using IterVC.Audio.Interop;
using NAudio.CoreAudioApi;

namespace IterVC.Audio;

public sealed class ProcessLoopbackCapture : IDisposable
{
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private Thread? _captureThread;
    private AutoResetEvent? _bufferEvent;
    private volatile bool _stopRequested;

    public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    /// <summary>
    /// Indica si la captura queda finalmente en modo RAW (audio pre-APO, sin los efectos
    /// del sistema: Dolby Atmos, Windows Sonic, DTS, etc.). <c>false</c> significa que se
    /// tuvo que hacer fallback al modo normal porque el endpoint no soporta
    /// <c>AUDCLNT_STREAMFLAGS_RAW</c>.
    /// </summary>
    public bool IsUsingRawAudio { get; private set; }

    public event EventHandler<byte[]>? DataAvailable;

    public async Task StartAsync(int processId, bool includeProcessTree, ILogger? logger = null)
    {
        // Por defecto se intenta el modo RAW (audio sin procesar por los APOs del sistema).
        // Si el endpoint no lo soporta, se hace fallback transparente al modo normal.
        await StartAsync(processId, includeProcessTree, useRawAudio: true, logger);
    }

    /// <summary>
    /// Inicia la captura por proceso. Si <paramref name="useRawAudio"/> es <c>true</c>,
    /// intenta activar el stream con <c>AUDCLNT_STREAMFLAGS_RAW</c> (audio sin procesar
    /// por los APOs del sistema). Si el endpoint no lo soporta
    /// (<c>AUDCLNT_E_RAW_STREAM_CONFIG_NOT_SUPPORTED</c>), se re-activa un cliente nuevo
    /// y se hace fallback automÃ¡tico al modo normal.
    /// </summary>
    public async Task StartAsync(int processId, bool includeProcessTree, bool useRawAudio, ILogger? logger = null)
    {
        // 1. Limpieza de seguridad: Si había un cliente anterior vivo, lo liberamos
        if (_audioClient != null)
        {
            try { Marshal.ReleaseComObject(_audioClient); } catch { /* ignore */ }
            _audioClient = null;
        }

        int sampleRate = 48000;
        const int channels = 2;

        try
        {
            using (var enumerator = new MMDeviceEnumerator())
            using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                var mixFormat = device.AudioClient.MixFormat;
                sampleRate = mixFormat.SampleRate; // 44100, 48000, 96000, etc.
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "No se pudo obtener el SampleRate nativo. Usando fallback a 48000Hz.");
        }

        var baseFlags = ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_LOOPBACK
                      | ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_EVENTCALLBACK;

        // Configuración limpia y segura de 2 canales en frecuencia nativa
        var format = new WAVEFORMATEX
        {
            wFormatTag = ProcessLoopbackNativeMethods.WAVE_FORMAT_IEEE_FLOAT,
            nChannels = (ushort)channels,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 32,
            nBlockAlign = (ushort)(channels * (32 / 8)),
            nAvgBytesPerSec = (uint)(sampleRate * channels * (32 / 8)),
            cbSize = 0
        };

        const long hnsBufferDuration = 200 * 10_000;

        // Activamos el cliente de audio antes del primer intento
        _audioClient = await ActivateAsync(processId, includeProcessTree);

        // Decidir si intentamos usar modo RAW
        var flagsToTry = useRawAudio
            ? baseFlags | ProcessLoopbackNativeMethods.AUDCLNT_STREAMFLAGS_RAW
            : baseFlags;

        // Primer intento de inicialización
        var hrInit = _audioClient.Initialize(
            ProcessLoopbackNativeMethods.AUDCLNT_SHAREMODE_SHARED,
            flagsToTry,
            hnsBufferDuration,
            0,
            ref format,
            IntPtr.Zero);

        const int AUDCLNT_E_INVALID_STREAM_FLAG = unchecked((int)0x88890021);

        // Fallback si el dispositivo no tolera el modo RAW
        if (hrInit == ProcessLoopbackNativeMethods.AUDCLNT_E_RAW_STREAM_CONFIG_NOT_SUPPORTED ||
            (useRawAudio && hrInit == AUDCLNT_E_INVALID_STREAM_FLAG))
        {
            IsUsingRawAudio = false;
            logger?.LogInformation(
                "La tarjeta de audio rechazó el modo RAW (Error: 0x{HR:X8}). Fallback a modo normal para {ProcessId}.", hrInit, processId);

            try { Marshal.ReleaseComObject(_audioClient); } catch { /* ignore */ }
            _audioClient = null;

            _audioClient = await ActivateAsync(processId, includeProcessTree);
            hrInit = _audioClient.Initialize(
                ProcessLoopbackNativeMethods.AUDCLNT_SHAREMODE_SHARED,
                baseFlags,
                hnsBufferDuration,
                0,
                ref format,
                IntPtr.Zero);
        }
        else
        {
            IsUsingRawAudio = useRawAudio;
        }

        // Si sigue fallando por otra razón, lanzamos la excepción
        Marshal.ThrowExceptionForHR(hrInit);

        // Asignamos el formato de salida real con el que se inicializó el stream
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)format.nSamplesPerSec, format.nChannels);

        _bufferEvent = new AutoResetEvent(false);
        Marshal.ThrowExceptionForHR(_audioClient.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle()));

        var iidCaptureClient = ProcessLoopbackNativeMethods.IID_IAudioCaptureClient;
        Marshal.ThrowExceptionForHR(_audioClient.GetService(ref iidCaptureClient, out var serviceObj));
        _captureClient = (IAudioCaptureClient)serviceObj;

        Marshal.ThrowExceptionForHR(_audioClient.Start());

        _stopRequested = false;
        _captureThread = new Thread(() => CaptureLoop(logger))
        {
            IsBackground = true,
            Name = $"ProcessLoopback-{processId}"
        };
        _captureThread.Start();
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
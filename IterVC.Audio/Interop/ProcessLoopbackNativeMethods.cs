using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace IterVC.Audio.Interop;

/// <summary>
/// P/Invoke y estructuras necesarias para activar un <c>IAudioClient</c> de "process loopback".
/// </summary>
internal static class ProcessLoopbackNativeMethods
{
    public const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";

    public const int AUDCLNT_SHAREMODE_SHARED = 0;
    public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

    public const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
    public const ushort VT_BLOB = 65;

    public const uint KSAUDIO_SPEAKER_STEREO = 0x00000003;
    public const uint KSAUDIO_SPEAKER_5POINT1_SURROUND = 0x0000060F;
    public const uint KSAUDIO_SPEAKER_7POINT1_SURROUND = 0x0000063F;

    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00AA00389B71");

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    public static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        ref PROPVARIANT activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IntPtr activationOperation); // Cambiado a IntPtr para evitar excepciones de COM
}

internal enum ProcessLoopbackMode
{
    IncludeTargetProcessTree = 0,
    ExcludeTargetProcessTree = 1
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
{
    public uint TargetProcessId;
    public int ProcessLoopbackMode;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct AUDIOCLIENT_ACTIVATION_PARAMS
{
    public uint ActivationType; // 1 = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
    public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
}

/// <summary>
/// Representación secuencial y segura (cross-platform x86/x64) de PROPVARIANT para VT_BLOB.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public uint blobSize;
    public IntPtr blobData;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint nSamplesPerSec;
    public uint nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WAVEFORMATEXTENSIBLE
{
    public WAVEFORMATEX Format;
    public ushort wValidBitsPerSample;
    public uint dwChannelMask;
    public Guid SubFormat;
}

[ComImport]
[Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject { }

[ComImport]
[Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IntPtr op);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr format, IntPtr audioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint numBufferFrames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr deviceFormatPtr);
    [PreserveSig] int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr dataPointer, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint numFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int GetActivateResultDelegate(IntPtr self, out int hr, out IntPtr iface);

/// <summary>
/// Recibe de manera segura el callback asíncrono y resuelve la vtable nativa para evitar excepciones COM.
/// </summary>
internal sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
{
    private readonly TaskCompletionSource<IAudioClient> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IAudioClient> Result => _tcs.Task;

    public void ActivateCompleted(IntPtr op)
    {
        System.Diagnostics.Debug.WriteLine("[COM] ActivateCompleted callback recibido de manera segura!");
        try
        {
            if (op == IntPtr.Zero)
            {
                _tcs.TrySetException(new COMException("La operación asíncrona nativa es nula."));
                return;
            }

            IntPtr vtable = Marshal.ReadIntPtr(op);

            IntPtr getActivateResultPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);

            var getActivateResult = Marshal.GetDelegateForFunctionPointer<GetActivateResultDelegate>(getActivateResultPtr);

            int comHr = getActivateResult(op, out var hr, out var pActivated);

            if (comHr != 0)
            {
                _tcs.TrySetException(new COMException($"Fallo al llamar a GetActivateResult nativo: 0x{comHr:X8}", comHr));
                return;
            }

            if (hr != 0)
            {
                _tcs.TrySetException(Marshal.GetExceptionForHR(hr)
                    ?? new COMException("Error interno en la activación del dispositivo", hr));
                return;
            }

            Guid iidAudioClient = ProcessLoopbackNativeMethods.IID_IAudioClient;
            int qi = Marshal.QueryInterface(pActivated, ref iidAudioClient, out IntPtr pAudioClient);
            if (qi != 0)
            {
                _tcs.TrySetException(new COMException($"QueryInterface IAudioClient falló: 0x{qi:X8}", qi));
                return;
            }

            try
            {
#pragma warning disable CA1416 // Validar la compatibilidad de la plataforma - No me importa de momento, solo funcionaremos en windows :P
                var client = (IAudioClient)Marshal.GetObjectForIUnknown(pAudioClient);
#pragma warning restore CA1416 // Validar la compatibilidad de la plataforma
                _tcs.TrySetResult(client);
            }
            finally
            {
                Marshal.Release(pAudioClient);
                if (pActivated != IntPtr.Zero)
                {
                    Marshal.Release(pActivated);
                }
            }
        }
        catch (Exception ex)
        {
            _tcs.TrySetException(ex);
        }
    }
}

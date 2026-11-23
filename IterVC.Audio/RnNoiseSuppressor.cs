using System.Runtime.InteropServices;

namespace IterVC.Audio;

/// <summary>
/// RNNoise-compatible native adapter. Input and output are 48 kHz stereo IEEE float PCM.
/// Native DSP runs only from the microphone processing worker, never the WASAPI callback.
/// </summary>
internal sealed class RnNoiseSuppressor : IDisposable
{
    internal const int FrameSizePerChannel = 480;
    internal const int Channels = 2;
    internal const int FrameSamples = FrameSizePerChannel * Channels;

    private IntPtr _runtime;

    public RnNoiseSuppressor()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("RNNoise is supported on Windows.");

        _runtime = Native.Create();
        if (_runtime == IntPtr.Zero)
            throw new InvalidOperationException("Could not initialize the RNNoise native backend.");
    }

    public unsafe void ProcessFrame(ReadOnlySpan<float> input, Span<float> output)
    {
        ObjectDisposedException.ThrowIf(_runtime == IntPtr.Zero, this);
        if (input.Length != FrameSamples || output.Length != FrameSamples)
            throw new ArgumentException("RNNoise needs exactly 480 stereo samples at 48 kHz.");

        fixed (float* source = input)
        fixed (float* destination = output)
        {
            if (!Native.ProcessFrame(_runtime, source, destination))
                throw new InvalidOperationException("RNNoise native processing failed.");
        }
    }

    public void Dispose()
    {
        var runtime = Interlocked.Exchange(ref _runtime, IntPtr.Zero);
        if (runtime != IntPtr.Zero)
            Native.Free(runtime);
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        [DllImport("iter_vc_rnnoise", EntryPoint = "ivc_rnnoise_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create();

        [DllImport("iter_vc_rnnoise", EntryPoint = "ivc_rnnoise_process_frame", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern unsafe bool ProcessFrame(IntPtr runtime, float* input, float* output);

        [DllImport("iter_vc_rnnoise", EntryPoint = "ivc_rnnoise_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(IntPtr runtime);
    }
}

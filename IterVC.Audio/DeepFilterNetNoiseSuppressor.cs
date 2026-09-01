using System.Runtime.InteropServices;

namespace IterVC.Audio;

/// <summary>
/// Native DeepFilterNet3 speech-enhancement adapter.
/// The native bridge embeds the upstream DFN3 model, so no Python runtime or
/// model download is required at application runtime.
/// </summary>
internal sealed class DeepFilterNetNoiseSuppressor : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private readonly IntPtr _runtime;
    private readonly int _frameSizePerChannel;
    private readonly float[] _inputFrame;
    private readonly float[] _outputFrame;
    private bool _disposed;

    public DeepFilterNetNoiseSuppressor()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DeepFilterNet native audio processing is currently supported only on Windows.");

        _runtime = Native.ivc_dfn_create(Channels);
        if (_runtime == IntPtr.Zero)
            throw new InvalidOperationException("DeepFilterNet3 native runtime could not be initialized.");

        var frameLength = Native.ivc_dfn_get_frame_length(_runtime);
        if (frameLength <= 0)
        {
            Native.ivc_dfn_free(_runtime);
            _runtime = IntPtr.Zero;
            throw new InvalidOperationException("DeepFilterNet3 returned an invalid frame length.");
        }

        _frameSizePerChannel = checked((int)frameLength);
        _inputFrame = new float[checked(_frameSizePerChannel * Channels)];
        _outputFrame = new float[_inputFrame.Length];

        // DFN3's native model is a 48 kHz full-band voice enhancer. Keep a
        // conservative attenuation limit while allowing the neural model to do
        // the actual suppression.
        Native.ivc_dfn_set_attenuation_limit(_runtime, 35.0f);
        Native.ivc_dfn_set_post_filter_beta(_runtime, 0.0f);
    }

    public int FrameSizePerChannel => _frameSizePerChannel;

    public void ProcessFrame(ReadOnlySpan<float> interleavedInput, Span<float> interleavedOutput)
    {
        ThrowIfDisposed();
        if (interleavedInput.Length != _inputFrame.Length || interleavedOutput.Length != _outputFrame.Length)
            throw new ArgumentException($"DeepFilterNet3 expects {_inputFrame.Length} interleaved samples per frame.");

        interleavedInput.CopyTo(_inputFrame);

        unsafe
        {
            fixed (float* input = _inputFrame)
            fixed (float* output = _outputFrame)
            {
                var lsnr = Native.ivc_dfn_process_frame(_runtime, input, output);
                if (float.IsNaN(lsnr) || float.IsInfinity(lsnr))
                    throw new InvalidOperationException("DeepFilterNet3 failed to process an audio frame.");
            }
        }

        _outputFrame.AsSpan().CopyTo(interleavedOutput);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_runtime != IntPtr.Zero)
            Native.ivc_dfn_free(_runtime);
        GC.SuppressFinalize(this);
    }

    private static class Native
    {
        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ivc_dfn_create(uint channels);

        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nuint ivc_dfn_get_frame_length(IntPtr runtime);

        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern unsafe float ivc_dfn_process_frame(IntPtr runtime, float* interleavedInput, float* interleavedOutput);

        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ivc_dfn_set_attenuation_limit(IntPtr runtime, float attenuationDb);

        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ivc_dfn_set_post_filter_beta(IntPtr runtime, float beta);

        [DllImport("iter_vc_deep_filter", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ivc_dfn_free(IntPtr runtime);
    }
}

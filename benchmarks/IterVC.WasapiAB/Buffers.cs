using System.Runtime.InteropServices;
using IterVC.Audio.Buffers;
using Microsoft.Win32.SafeHandles;
using NAudio.Wave;

namespace IterVC.WasapiAB;

internal readonly record struct BufferCounters(
    long WrittenFrames, long ReadFrames, long Underflows,
    long Overflows, long Corrections, long DiscardedFrames,
    double BufferedMs, double AverageBufferedMs);

internal interface IBufferBackend : IDisposable
{
    string Name { get; }
    void Write(byte[] bytes, int count);
    int Read(byte[] destination, int count);
    BufferCounters Snapshot();
}

internal sealed class ManagedBuffer(WaveFormat format, bool readFully) : IBufferBackend
{
    private readonly RealtimeAudioBuffer _buffer = new(format, readFully,
        countUnderflows: readFully);
    public string Name => "C# original";
    public void Write(byte[] bytes, int count) => _buffer.Write(bytes, 0, count);
    public int Read(byte[] destination, int count) => _buffer.Read(destination, 0, count);
    public BufferCounters Snapshot()
    {
        var s = _buffer.GetSnapshot();
        return new(s.TotalFramesWritten, s.TotalFramesRead, s.UnderflowBlocks,
            s.OverflowEvents, s.LatencyCorrections, s.DiscardedFrames,
            s.BufferedMilliseconds, s.AverageBufferedMilliseconds);
    }
    public void Dispose() { }
}

internal sealed class RustBuffer : IBufferBackend
{
    private readonly SafeNativeBuffer _handle;
    private readonly int _bytesPerSecond;
    private readonly int _blockAlign;

    public RustBuffer(WaveFormat format, bool readFully)
    {
        // Constructor settings MUST match the C# production defaults.
        var align = format.BlockAlign;
        _bytesPerSecond = format.AverageBytesPerSecond;
        _blockAlign = align;
        if (align <= 0 || _bytesPerSecond <= 0)
            throw new ArgumentException("Invalid WASAPI PCM format.", nameof(format));
        nuint AlignMs(int ms)
        {
            var bytes = _bytesPerSecond * ms / 1000;
            return checked((nuint)Math.Max(align, bytes - bytes % align));
        }
        _handle = Native.Create(AlignMs(120), checked((nuint)align),
            AlignMs(20), AlignMs(50), readFully ? 1 : 0, readFully ? 1 : 0);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new InvalidOperationException("Rust ring initialization failed.");
        }
    }

    public string Name => "Rust std::sync::Mutex + P/Invoke";
    public void Write(byte[] bytes, int count)
    {
        var written = Native.Write(_handle, bytes, checked((nuint)count));
        if (written != (nuint)(count - count % _blockAlign) &&
            count <= _bytesPerSecond * 120 / 1000)
            throw new InvalidOperationException($"Rust ring accepted {written} of {count} bytes.");
    }
    public int Read(byte[] destination, int count) =>
        checked((int)Native.Read(_handle, destination, checked((nuint)count)));
    public BufferCounters Snapshot()
    {
        if (Native.Snapshot(_handle, out var s) != 1)
            throw new InvalidOperationException("Rust ring snapshot failed.");
        return new(
            checked((long)s.WrittenFrames), checked((long)s.ReadFrames),
            checked((long)s.Underflows), checked((long)s.Overflows),
            checked((long)s.Corrections), checked((long)s.DiscardedFrames),
            s.BufferedBytes * 1000.0 / _bytesPerSecond,
            s.AverageBufferedBytes * 1000.0 / _bytesPerSecond);
    }
    public void Dispose() => _handle.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSnapshot
{
    internal ulong BufferedBytes, TargetBytes, MinimumBufferedBytes, MaximumBufferedBytes;
    internal double AverageBufferedBytes;
    internal ulong WrittenFrames, ReadFrames, Underflows, Overflows, Corrections, DiscardedFrames;
}

internal sealed class SafeNativeBuffer : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeNativeBuffer() : base(true) { }
    protected override bool ReleaseHandle()
    {
        Native.Free(handle);
        return true;
    }
}

internal static class Native
{
    private const string Library = "iter_vc_buffers";

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ivc_buffer_create")]
    internal static extern SafeNativeBuffer Create(nuint capacity, nuint alignment,
        nuint target, nuint correction, int readFully, int countUnderflows);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ivc_buffer_write")]
    internal static extern nuint Write(SafeNativeBuffer handle,
        [In] byte[] source, nuint count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ivc_buffer_read")]
    internal static extern nuint Read(SafeNativeBuffer handle,
        [Out] byte[] destination, nuint count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ivc_buffer_snapshot")]
    internal static extern int Snapshot(SafeNativeBuffer handle, out NativeSnapshot snapshot);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ivc_buffer_free")]
    internal static extern void Free(nint handle);
}

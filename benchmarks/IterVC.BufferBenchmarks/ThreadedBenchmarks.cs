using System.Diagnostics;
using IterVC.Audio.Buffers;

namespace IterVC.BufferBenchmarks;

internal static partial class Program
{
    private sealed record ThreadedMeasurement(
        string Backend, double WallMs,
        double WriteP50Us, double WriteP99Us, double WriteP999Us,
        double ReadP50Us, double ReadP99Us, double ReadP999Us,
        long Underflows, long Overflows, long WrittenFrames, long ReadFrames,
        double WriterAllocBytesPerOperation, double ReaderAllocBytesPerOperation);

    private static ThreadedMeasurement[] MeasureThreadedBenchmarks(int operations)
    {
        var managed = new RealtimeAudioBuffer(Format, readFully: true);
        var csharp = MeasureThreaded("C# original", operations,
            bytes => managed.Write(bytes, 0, bytes.Length),
            bytes => managed.Read(bytes, 0, bytes.Length),
            () =>
            {
                var s = managed.GetSnapshot();
                return (s.UnderflowBlocks, s.OverflowEvents, s.TotalFramesWritten,
                    s.TotalFramesRead);
            });
        using var rust = new RustBuffer(readFully: true, countUnderflows: true);
        var native = MeasureThreaded("Rust + P/Invoke", operations,
            bytes => rust.Write(bytes, bytes.Length),
            bytes => rust.Read(bytes, bytes.Length),
            () =>
            {
                var s = rust.Snapshot();
                return (checked((long)s.Underflows), checked((long)s.Overflows),
                    checked((long)s.WrittenFrames), checked((long)s.ReadFrames));
            });
        Console.WriteLine($"Threads: C# write p99={csharp.WriteP99Us:F2} us, read p99={csharp.ReadP99Us:F2} us; " +
            $"Rust write p99={native.WriteP99Us:F2} us, read p99={native.ReadP99Us:F2} us");
        return [csharp, native];
    }

    private static ThreadedMeasurement MeasureThreaded(
        string backend, int operations, Action<byte[]> write,
        Func<byte[], int> read, Func<(long Underflows, long Overflows, long Written, long Read)> counters)
    {
        // Different producer and consumer threads. Each operates on the same
        // ring, just as capture and playback callbacks would. The burst
        // workload deliberately creates saturation; exact bytes/counters
        // are independently verified in VerifyParity() before this test.
        const int blockBytes = 3840; // 10 ms, stereo 48 kHz float PCM
        const int sampleEvery = 32;
        var samples = (operations + sampleEvery - 1) / sampleEvery;
        var writeSamples = new long[samples];
        var readSamples = new long[samples];
        using var startingGate = new Barrier(3);
        long writerAlloc = 0;
        long readerAlloc = 0;
        var producer = Task.Factory.StartNew(() =>
        {
            var data = new byte[blockBytes];
            Array.Fill(data, (byte)0xA5);
            startingGate.SignalAndWait();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var n = 0; n < operations; n++)
            {
                if (n % sampleEvery == 0)
                {
                    long start = Stopwatch.GetTimestamp();
                    write(data);
                    writeSamples[n / sampleEvery] = Stopwatch.GetTimestamp() - start;
                }
                else write(data);
            }
            writerAlloc = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var consumer = Task.Factory.StartNew(() =>
        {
            var data = new byte[blockBytes];
            startingGate.SignalAndWait();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var n = 0; n < operations; n++)
            {
                int count;
                if (n % sampleEvery == 0)
                {
                    long start = Stopwatch.GetTimestamp();
                    count = read(data);
                    readSamples[n / sampleEvery] = Stopwatch.GetTimestamp() - start;
                }
                else count = read(data);
                if (count != blockBytes)
                    throw new InvalidOperationException($"{backend}: readFully returned {count} rather than {blockBytes}");
                // A concurrent stream may drop older frames; it cannot invent
                // bytes other than the one-value PCM input or zero padding.
                if (n % 64 == 0 && data.Any(value => value != 0xA5 && value != 0))
                    throw new InvalidOperationException($"{backend}: corrupted PCM in threaded transfer");
            }
            readerAlloc = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var stopwatch = Stopwatch.StartNew();
        startingGate.SignalAndWait();
        if (!Task.WaitAll([producer, consumer], TimeSpan.FromSeconds(60)))
            throw new TimeoutException($"{backend}: concurrent read/write hung for >60 seconds");
        stopwatch.Stop();
        var s = counters();
        long expectedWritten = (long)operations * (blockBytes / Format.BlockAlign);
        if (s.Written != expectedWritten ||
            s.Read < 0 || s.Read > s.Written ||
            s.Underflows < 0 || s.Overflows < 0)
            throw new InvalidOperationException(
                $"{backend}: impossible concurrent statistics: {s}");
        return new ThreadedMeasurement(
            backend, stopwatch.Elapsed.TotalMilliseconds,
            PercentileUs(writeSamples, 0.50), PercentileUs(writeSamples, 0.99),
            PercentileUs(writeSamples, 0.999),
            PercentileUs(readSamples, 0.50), PercentileUs(readSamples, 0.99),
            PercentileUs(readSamples, 0.999),
            s.Underflows, s.Overflows, s.Written, s.Read,
            (double)writerAlloc / operations, (double)readerAlloc / operations);
    }

    private static double PercentileUs(long[] samples, double percentile)
    {
        var sorted = (long[])samples.Clone();
        Array.Sort(sorted);
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0,
            sorted.Length - 1);
        return sorted[index] * 1_000_000.0 / Stopwatch.Frequency;
    }
}

using System.Diagnostics;
using IterVC.Audio.Buffers;

namespace IterVC.BufferBenchmarks;

internal static partial class Program
{
    private sealed record PacedMeasurement(string Backend, double WallMs,
        double WriteP99Us, double ReadP99Us,
        long LateWrites, long LateReads, long Underflows, long Overflows);

    private static PacedMeasurement[] MeasurePacedBenchmarks(int callbacks)
    {
        var managed = new RealtimeAudioBuffer(Format, readFully: true);
        var csharp = MeasurePaced("C# original", callbacks,
            data => managed.Write(data, 0, data.Length),
            data => managed.Read(data, 0, data.Length),
            () => (managed.GetSnapshot().UnderflowBlocks,
                managed.GetSnapshot().OverflowEvents));
        var optimized = new OptimizedRealtimeAudioBuffer(Format, readFully: true);
        var span = MeasurePaced("Span-optimized C#", callbacks,
            data => optimized.Write(data, 0, data.Length),
            data => optimized.Read(data, 0, data.Length),
            () => (optimized.GetSnapshot().UnderflowBlocks,
                optimized.GetSnapshot().OverflowEvents));
        using var native = new RustBuffer(readFully: true, countUnderflows: true);
        var rust = MeasurePaced("Rust + P/Invoke", callbacks,
            data => native.Write(data, data.Length),
            data => native.Read(data, data.Length),
            () =>
            {
                var s = native.Snapshot();
                return (checked((long)s.Underflows), checked((long)s.Overflows));
            });
        Console.WriteLine($"Paced callbacks (1ms): C# p99={csharp.ReadP99Us:F2}us, " +
            $"Span p99={span.ReadP99Us:F2}us, Rust p99={rust.ReadP99Us:F2}us.");
        return [csharp, span, rust];
    }

    private static PacedMeasurement MeasurePaced(
        string backend, int callbacks, Action<byte[]> write, Func<byte[], int> read,
        Func<(long Underflows, long Overflows)> snapshot)
    {
        // Virtualized CALLBACK simulation: two different threads run 1ms apart,
        // the consumer starts half a period after the producer. A 10ms audio
        // packet is deliberately processed at 10x real-time to stress the
        // implementation without using physical sound hardware.
        const int blockBytes = 3840;
        var periodTicks = Math.Max(1, Stopwatch.Frequency / 1000);
        var writeSamples = new long[callbacks];
        var readSamples = new long[callbacks];
        long writeLate = 0;
        long readLate = 0;
        long origin = 0;
        using var barrier = new Barrier(3);

        var producer = Task.Factory.StartNew(() =>
        {
            var bytes = new byte[blockBytes];
            Array.Fill(bytes, (byte)0xA5);
            barrier.SignalAndWait();
            long startTime = Volatile.Read(ref origin);
            for (int i = 0; i < callbacks; i++)
            {
                long deadline = startTime + (long)i * periodTicks;
                while (Stopwatch.GetTimestamp() < deadline) Thread.SpinWait(48);
                long now = Stopwatch.GetTimestamp();
                if (now > deadline + periodTicks) writeLate++;
                write(bytes);
                writeSamples[i] = Stopwatch.GetTimestamp() - now;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var consumer = Task.Factory.StartNew(() =>
        {
            var bytes = new byte[blockBytes];
            barrier.SignalAndWait();
            long startTime = Volatile.Read(ref origin) + periodTicks / 2;
            for (int i = 0; i < callbacks; i++)
            {
                long deadline = startTime + (long)i * periodTicks;
                while (Stopwatch.GetTimestamp() < deadline) Thread.SpinWait(48);
                long now = Stopwatch.GetTimestamp();
                if (now > deadline + periodTicks) readLate++;
                var count = read(bytes);
                readSamples[i] = Stopwatch.GetTimestamp() - now;
                if (count != blockBytes)
                    throw new InvalidOperationException($"{backend}: paced read returned {count}");
                if (i % 64 == 0 && bytes.Any(value => value != 0 && value != 0xA5))
                    throw new InvalidOperationException($"{backend}: paced PCM corruption");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Volatile.Write(ref origin, Stopwatch.GetTimestamp() + 25 * periodTicks);
        var stopwatch = Stopwatch.StartNew();
        barrier.SignalAndWait();
        if (!Task.WaitAll([producer, consumer], TimeSpan.FromSeconds(30)))
            throw new TimeoutException($"{backend}: paced callbacks hung");
        stopwatch.Stop();
        var (underflows, overflows) = snapshot();
        if (underflows < 0 || overflows < 0)
            throw new InvalidOperationException($"{backend}: invalid PCM counters");
        return new(backend, stopwatch.Elapsed.TotalMilliseconds,
            PercentileUs(writeSamples, 0.99), PercentileUs(readSamples, 0.99),
            writeLate, readLate, underflows, overflows);
    }
}

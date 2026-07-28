using System.Diagnostics;

namespace IterVC.Audio.Diagnostics;

internal sealed class AudioProcessingMetrics
{
    private long _blocks;
    private long _totalTicks;
    private long _maximumTicks;

    public void Record(long startedTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
        Interlocked.Increment(ref _blocks);
        Interlocked.Add(ref _totalTicks, elapsed);
        long current;
        while (elapsed > (current = Volatile.Read(ref _maximumTicks)) &&
               Interlocked.CompareExchange(ref _maximumTicks, elapsed, current) != current) { }
    }

    public (double AverageMs, double MaximumMs) Snapshot()
    {
        var blocks = Math.Max(1, Volatile.Read(ref _blocks));
        var millisecondsPerTick = 1000d / Stopwatch.Frequency;
        return (Volatile.Read(ref _totalTicks) * millisecondsPerTick / blocks,
            Volatile.Read(ref _maximumTicks) * millisecondsPerTick);
    }
}

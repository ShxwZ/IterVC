using System.Diagnostics;
using IterVC.Core.Models;
using NAudio.Wave;

namespace IterVC.Audio;

internal sealed class PreProtectionDiagnosticsSampleProvider(ISampleProvider source) : ISampleProvider
{
    private float _maximumPeak;
    private long _overSamples;
    private long _overBlocks;
    private long _lastSampleTimestamp;

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        if (read == 0) return 0;
        var peak = 0f;
        var overSamples = 0;
        for (var index = 0; index < read; index++)
        {
            var absolute = MathF.Abs(buffer[offset + index]);
            peak = MathF.Max(peak, absolute);
            if (absolute > 1f) overSamples++;
        }
        if (overSamples > 0)
        {
            Interlocked.Add(ref _overSamples, overSamples);
            Interlocked.Increment(ref _overBlocks);
        }
        UpdateMaximum(peak);
        Volatile.Write(ref _lastSampleTimestamp, Stopwatch.GetTimestamp());
        return read;
    }

    public AudioSignalDiagnosticsSnapshot GetSnapshot(long protectionActivations = 0)
    {
        var peak = Volatile.Read(ref _maximumPeak);
        return new(
            peak <= 0f ? -80f : 20f * MathF.Log10(peak),
            Volatile.Read(ref _overSamples),
            Volatile.Read(ref _overBlocks),
            protectionActivations,
            Volatile.Read(ref _lastSampleTimestamp) != 0);
    }

    private void UpdateMaximum(float value)
    {
        var current = Volatile.Read(ref _maximumPeak);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref _maximumPeak, value, current);
            if (previous == current) return;
            current = previous;
        }
    }
}

using System.Collections.Concurrent;
using IterVC.Audio.Buffers;
using Microsoft.Extensions.Logging;

namespace IterVC.Audio.Diagnostics;

internal sealed class AudioBufferDiagnosticsReporter : IDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, SourceEntry> _sources = new();
    private readonly Timer _timer;
    private int _disposed;

    public AudioBufferDiagnosticsReporter(ILogger logger, TimeSpan? interval = null)
    {
        _logger = logger;
        _timer = new Timer(_ => ReportAll(), null, interval ?? TimeSpan.FromSeconds(30),
            interval ?? TimeSpan.FromSeconds(30));
    }

    public void Register(string key, RealtimeAudioBuffer buffer,
        AudioProcessingMetrics? processing = null, double requestedPeriodMs = 0) =>
        _sources[key] = new SourceEntry(buffer, processing, requestedPeriodMs);

    public void Remove(string key)
    {
        if (_sources.TryRemove(key, out var source))
            Report(key, source, final: true);
    }

    private void ReportAll()
    {
        foreach (var source in _sources)
            Report(source.Key, source.Value, final: false);
    }

    private void Report(string source, SourceEntry entry, bool final)
    {
        var snapshot = entry.Buffer.GetSnapshot();
        var processing = entry.Processing?.Snapshot() ?? default;
        var averageBudget = entry.RequestedPeriodMs > 0 ? processing.AverageMs * 100 / entry.RequestedPeriodMs : 0;
        var maximumBudget = entry.RequestedPeriodMs > 0 ? processing.MaximumMs * 100 / entry.RequestedPeriodMs : 0;
        _logger.LogInformation(
            "[AudioBuffer] Source={Source} Final={Final} BufferedMs={BufferedMs:F1} TargetMs={TargetMs:F1} AvgMs={AverageMs:F1} MinMs={MinimumMs:F1} MaxMs={MaximumMs:F1} Underflows={Underflows} Overflows={Overflows} Corrections={Corrections} DiscardedMs={DiscardedMs:F1} WrittenFrames={WrittenFrames} ReadFrames={ReadFrames} ProcessingAvgMs={ProcessingAverageMs:F3} ProcessingMaxMs={ProcessingMaximumMs:F3} PeriodMs={PeriodMs:F1} AvgBudgetPct={AverageBudget:F1} MaxBudgetPct={MaximumBudget:F1}",
            source, final, snapshot.BufferedMilliseconds, snapshot.TargetMilliseconds,
            snapshot.AverageBufferedMilliseconds, snapshot.MinimumBufferedMilliseconds,
            snapshot.MaximumBufferedMilliseconds, snapshot.UnderflowBlocks,
            snapshot.OverflowEvents, snapshot.LatencyCorrections, snapshot.DiscardedMilliseconds,
            snapshot.TotalFramesWritten, snapshot.TotalFramesRead, processing.AverageMs,
            processing.MaximumMs, entry.RequestedPeriodMs, averageBudget, maximumBudget);
        var underflowDelta = snapshot.UnderflowBlocks - entry.LastUnderflows;
        var overflowDelta = snapshot.OverflowEvents - entry.LastOverflows;
        var correctionDelta = snapshot.LatencyCorrections - entry.LastCorrections;
        entry.LastUnderflows = snapshot.UnderflowBlocks;
        entry.LastOverflows = snapshot.OverflowEvents;
        entry.LastCorrections = snapshot.LatencyCorrections;
        if (!final && (overflowDelta > 0 || correctionDelta >= 3 || underflowDelta > 10))
        {
            _logger.LogWarning(
                "[AudioBuffer] Source={Source} interval requires attention: UnderflowsDelta={Underflows} OverflowsDelta={Overflows} CorrectionsDelta={Corrections} TotalDiscardedMs={DiscardedMs:F1}",
                source, underflowDelta, overflowDelta, correctionDelta, snapshot.DiscardedMilliseconds);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        ReportAll();
        _sources.Clear();
    }

    private sealed class SourceEntry(RealtimeAudioBuffer buffer,
        AudioProcessingMetrics? processing, double requestedPeriodMs)
    {
        public RealtimeAudioBuffer Buffer { get; } = buffer;
        public AudioProcessingMetrics? Processing { get; } = processing;
        public double RequestedPeriodMs { get; } = requestedPeriodMs;
        public long LastUnderflows;
        public long LastOverflows;
        public long LastCorrections;
    }
}

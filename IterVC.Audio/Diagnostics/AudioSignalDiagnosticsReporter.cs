using Microsoft.Extensions.Logging;

namespace IterVC.Audio.Diagnostics;

internal sealed class AudioSignalDiagnosticsReporter : IDisposable
{
    private readonly ILogger _logger;
    private readonly Func<PreProtectionDiagnosticsSampleProvider?> _meter;
    private readonly Func<OutputProtectionSampleProvider?> _protection;
    private readonly Timer _timer;
    private long _lastOverBlocks;
    private int _disposed;

    public AudioSignalDiagnosticsReporter(ILogger logger,
        Func<PreProtectionDiagnosticsSampleProvider?> meter,
        Func<OutputProtectionSampleProvider?> protection)
    {
        _logger = logger;
        _meter = meter;
        _protection = protection;
        _timer = new Timer(_ => Report(false), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Report(bool final)
    {
        var meter = _meter();
        if (meter is null) return;
        var snapshot = meter.GetSnapshot(_protection()?.ActivationBlocks ?? 0);
        _logger.LogInformation(
            "[AudioOutput] Final={Final} MaximumPreProtectionPeakDb={PeakDb:F2} OverBlocks={OverBlocks} OverSamples={OverSamples} ProtectionActivations={ProtectionActivations}",
            final, snapshot.UnclampedPeakDb, snapshot.OverBlockCount, snapshot.OverSampleCount,
            snapshot.ProtectionActivationCount);
        var overBlockDelta = snapshot.OverBlockCount - Interlocked.Exchange(
            ref _lastOverBlocks, snapshot.OverBlockCount);
        if (!final && overBlockDelta > 0)
        {
            _logger.LogWarning(
                "[AudioOutput] Pre-protection overloads detected: OverBlocksDelta={OverBlocksDelta} MaximumPreProtectionPeakDb={PeakDb:F2} ProtectionActivations={ProtectionActivations}",
                overBlockDelta, snapshot.UnclampedPeakDb, snapshot.ProtectionActivationCount);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        Report(true);
    }
}

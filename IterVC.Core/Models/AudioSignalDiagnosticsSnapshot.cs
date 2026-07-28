namespace IterVC.Core.Models;

public readonly record struct AudioSignalDiagnosticsSnapshot(
    float UnclampedPeakDb,
    long OverSampleCount,
    long OverBlockCount,
    long ProtectionActivationCount,
    bool HasRecentSamples);

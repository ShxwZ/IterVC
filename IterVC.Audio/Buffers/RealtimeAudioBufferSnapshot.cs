namespace IterVC.Audio.Buffers;

internal readonly record struct RealtimeAudioBufferSnapshot(
    double BufferedMilliseconds,
    double TargetMilliseconds,
    double MinimumBufferedMilliseconds,
    double MaximumBufferedMilliseconds,
    double AverageBufferedMilliseconds,
    long TotalFramesWritten,
    long TotalFramesRead,
    long UnderflowBlocks,
    long OverflowEvents,
    long LatencyCorrections,
    long DiscardedFrames,
    double DiscardedMilliseconds);

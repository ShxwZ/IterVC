using System.Diagnostics;
using IterVC.Core.Models;
using NAudio.Wave;

namespace IterVC.Audio;

internal sealed class LevelMeterSampleProvider : ISampleProvider
{
    private const float MinimumLinearLevel = 0.0001f;
    private readonly ISampleProvider _source;
    private float _latestLevelDb = AudioLevelSnapshot.MinimumDb;
    private float _latestPeakDb = AudioLevelSnapshot.MinimumDb;
    private long _lastSampleTimestamp;

    public LevelMeterSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
    public float LatestLevelDb => Volatile.Read(ref _latestLevelDb);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        double sumSquares = 0;
        var peak = 0f;
        for (var i = 0; i < read; i++)
        {
            var sample = buffer[offset + i];
            sumSquares += sample * sample;
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        var rms = MathF.Sqrt((float)(sumSquares / read));
        Volatile.Write(ref _latestLevelDb, ToDecibels(rms));
        Volatile.Write(ref _latestPeakDb, ToDecibels(peak));
        Volatile.Write(ref _lastSampleTimestamp, Stopwatch.GetTimestamp());
        return read;
    }

    public AudioLevelSnapshot GetSnapshot(TimeSpan staleAfter)
    {
        var timestamp = Volatile.Read(ref _lastSampleTimestamp);
        if (timestamp == 0 || Stopwatch.GetElapsedTime(timestamp) > staleAfter)
            return AudioLevelSnapshot.Silence;

        return new AudioLevelSnapshot(
            Volatile.Read(ref _latestLevelDb),
            Volatile.Read(ref _latestPeakDb),
            true);
    }

    private static float ToDecibels(float linear) =>
        Math.Clamp(
            20f * MathF.Log10(MathF.Max(linear, MinimumLinearLevel)),
            AudioLevelSnapshot.MinimumDb,
            AudioLevelSnapshot.MaximumDb);
}

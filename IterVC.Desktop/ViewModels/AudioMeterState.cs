using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Models;

namespace IterVC.Desktop.ViewModels;

public sealed partial class AudioMeterState : ViewModelBase
{
    private static readonly TimeSpan PeakHoldDuration = TimeSpan.FromMilliseconds(750);
    private long _peakHoldUntil;

    [ObservableProperty] private float _levelDb = AudioLevelSnapshot.MinimumDb;
    [ObservableProperty] private float _peakDb = AudioLevelSnapshot.MinimumDb;
    [ObservableProperty] private bool _isActive;

    public void Update(AudioLevelSnapshot snapshot, long nowMilliseconds)
    {
        if (!snapshot.HasRecentSamples)
        {
            Reset();
            return;
        }

        IsActive = true;
        var target = Math.Clamp(snapshot.LevelDb, AudioLevelSnapshot.MinimumDb, AudioLevelSnapshot.MaximumDb);
        LevelDb += (target - LevelDb) * (target > LevelDb ? 0.55f : 0.18f);

        var samplePeak = Math.Clamp(snapshot.PeakDb, AudioLevelSnapshot.MinimumDb, AudioLevelSnapshot.MaximumDb);
        if (samplePeak >= PeakDb)
        {
            PeakDb = samplePeak;
            _peakHoldUntil = nowMilliseconds + (long)PeakHoldDuration.TotalMilliseconds;
        }
        else if (nowMilliseconds >= _peakHoldUntil)
        {
            PeakDb = Math.Max(LevelDb, PeakDb - 2.5f);
        }
    }

    public void Reset()
    {
        IsActive = false;
        LevelDb = AudioLevelSnapshot.MinimumDb;
        PeakDb = AudioLevelSnapshot.MinimumDb;
        _peakHoldUntil = 0;
    }
}

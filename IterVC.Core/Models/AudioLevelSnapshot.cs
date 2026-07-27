namespace IterVC.Core.Models;

public readonly record struct AudioLevelSnapshot(
    float LevelDb,
    float PeakDb,
    bool HasRecentSamples)
{
    public const float MinimumDb = -80f;
    public const float MaximumDb = 0f;

    public static AudioLevelSnapshot Silence =>
        new(MinimumDb, MinimumDb, false);
}

using NAudio.Wave;

namespace IterVC.Audio;

internal sealed class OutputProtectionSampleProvider(ISampleProvider source) : ISampleProvider
{
    internal const float TransparentThreshold = 0.8912509f; // -1 dBFS
    private const float RemainingHeadroom = 1f - TransparentThreshold;
    private long _activationBlocks;

    public WaveFormat WaveFormat => source.WaveFormat;
    public long ActivationBlocks => Volatile.Read(ref _activationBlocks);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        var activated = false;
        for (var index = 0; index < read; index++)
        {
            var sampleIndex = offset + index;
            var sample = buffer[sampleIndex];
            var absolute = MathF.Abs(sample);
            if (absolute <= TransparentThreshold || !float.IsFinite(sample)) 
            {
                if (!float.IsFinite(sample)) buffer[sampleIndex] = 0f;
                continue;
            }
            activated = true;
            var protectedMagnitude = TransparentThreshold +
                RemainingHeadroom * MathF.Tanh((absolute - TransparentThreshold) / RemainingHeadroom);
            buffer[sampleIndex] = MathF.CopySign(protectedMagnitude, sample);
        }
        if (activated) Interlocked.Increment(ref _activationBlocks);
        return read;
    }
}

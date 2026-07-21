using NAudio.Wave;

namespace IterVC.Audio;

internal sealed class NoiseGateSampleProvider : ISampleProvider
{
    private const float MinimumLinearLevel = 0.000001f;
    private readonly ISampleProvider _source;
    private float _gain = 1f;
    private float _currentLevelDb = -80f;
    private int _isOpen;
    private volatile bool _enabled;

    public NoiseGateSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
    public float ThresholdDb { get; set; } = -45f;
    public float AttackMilliseconds { get; set; } = 10f;
    public float ReleaseMilliseconds { get; set; } = 150f;
    public float CurrentLevelDb => Volatile.Read(ref _currentLevelDb);
    public float CurrentGain => Enabled ? Volatile.Read(ref _gain) : 1f;
    public bool IsOpen => Volatile.Read(ref _isOpen) == 1;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        double sumSquares = 0;
        for (var i = 0; i < read; i++)
        {
            var sample = buffer[offset + i];
            sumSquares += sample * sample;
        }

        var rms = MathF.Sqrt((float)(sumSquares / read));
        var levelDb = 20f * MathF.Log10(MathF.Max(rms, MinimumLinearLevel));
        Volatile.Write(ref _currentLevelDb, Math.Clamp(levelDb, -80f, 0f));

        if (!Enabled)
        {
            _gain = 1f;
            Volatile.Write(ref _isOpen, 1);
            return read;
        }

        var targetGain = levelDb >= ThresholdDb ? 1f : 0f;
        Volatile.Write(ref _isOpen, targetGain > 0f ? 1 : 0);
        var durationMs = targetGain > _gain ? AttackMilliseconds : ReleaseMilliseconds;
        var rampSamples = Math.Max(1f, WaveFormat.SampleRate * WaveFormat.Channels * durationMs / 1000f);
        var gainStep = 1f / rampSamples;

        for (var i = 0; i < read; i++)
        {
            if (_gain < targetGain)
                _gain = MathF.Min(targetGain, _gain + gainStep);
            else if (_gain > targetGain)
                _gain = MathF.Max(targetGain, _gain - gainStep);

            buffer[offset + i] *= _gain;
        }

        return read;
    }
}

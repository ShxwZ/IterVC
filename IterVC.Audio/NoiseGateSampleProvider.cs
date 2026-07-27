using NAudio.Wave;

namespace IterVC.Audio;

internal sealed class NoiseGateSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<float> _currentInputLevelDb;
    private float _gain = 1f;
    private int _isOpen;
    private volatile bool _enabled;

    public NoiseGateSampleProvider(ISampleProvider source, Func<float> currentInputLevelDb)
    {
        _source = source;
        _currentInputLevelDb = currentInputLevelDb;
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
    public float CurrentGain => Enabled ? Volatile.Read(ref _gain) : 1f;
    public bool IsOpen => Volatile.Read(ref _isOpen) == 1;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read == 0)
            return 0;

        var levelDb = _currentInputLevelDb();

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

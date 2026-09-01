using SoundFlow.Extensions.WebRtc.Apm;

namespace IterVC.Audio;

/// <summary>
/// Small adapter around the native WebRTC Audio Processing Module that keeps
/// IterVC's existing interleaved 48 kHz stereo microphone contract.
/// </summary>
internal sealed class WebRtcNoiseSuppressor : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private readonly AudioProcessingModule _apm;
    private readonly ApmConfig _config;
    private readonly StreamConfig _inputConfig;
    private readonly StreamConfig _outputConfig;
    private readonly int _frameSizePerChannel;
    private readonly float[][] _input = new float[Channels][];
    private readonly float[][] _output = new float[Channels][];
    private bool _disposed;

    public WebRtcNoiseSuppressor(NoiseSuppressionLevel level = NoiseSuppressionLevel.VeryHigh)
    {
        _frameSizePerChannel = AudioProcessingModule.GetFrameSize(SampleRate);
        if (_frameSizePerChannel <= 0)
            throw new InvalidOperationException($"WebRTC APM does not support {SampleRate} Hz.");

        _inputConfig = new StreamConfig(SampleRate, Channels);
        _outputConfig = new StreamConfig(SampleRate, Channels);

        _input[0] = new float[_frameSizePerChannel];
        _input[1] = new float[_frameSizePerChannel];
        _output[0] = new float[_frameSizePerChannel];
        _output[1] = new float[_frameSizePerChannel];

        _apm = new AudioProcessingModule();
        _config = new ApmConfig();
        _config.SetEchoCanceller(false, false);
        _config.SetNoiseSuppression(true, level);
        _config.SetGainController1(false, GainControlMode.FixedDigital, 0, 0, false);
        _config.SetGainController2(false);
        _config.SetHighPassFilter(false);
        _config.SetPreAmplifier(false, 1.0f);
        _config.SetPipeline(SampleRate, true, true, DownmixMethod.AverageChannels);

        var applyResult = _apm.ApplyConfig(_config);
        if (applyResult != ApmError.NoError)
            throw new InvalidOperationException($"WebRTC APM ApplyConfig failed: {applyResult}.");

        var initializeResult = _apm.Initialize();
        if (initializeResult != ApmError.NoError)
            throw new InvalidOperationException($"WebRTC APM Initialize failed: {initializeResult}.");
    }

    public int FrameSizePerChannel => _frameSizePerChannel;

    /// <summary>
    /// Processes exactly one interleaved stereo 10 ms frame.
    /// </summary>
    public void ProcessFrame(ReadOnlySpan<float> interleavedInput, Span<float> interleavedOutput)
    {
        ThrowIfDisposed();
        var expected = _frameSizePerChannel * Channels;
        if (interleavedInput.Length != expected || interleavedOutput.Length != expected)
            throw new ArgumentException($"WebRTC APM expects {expected} interleaved samples per frame.");

        for (var i = 0; i < _frameSizePerChannel; i++)
        {
            _input[0][i] = interleavedInput[i * Channels];
            _input[1][i] = interleavedInput[i * Channels + 1];
        }

        var result = _apm.ProcessStream(_input, _inputConfig, _outputConfig, _output);
        if (result != ApmError.NoError)
            throw new InvalidOperationException($"WebRTC APM ProcessStream failed: {result}.");

        for (var i = 0; i < _frameSizePerChannel; i++)
        {
            interleavedOutput[i * Channels] = _output[0][i];
            interleavedOutput[i * Channels + 1] = _output[1][i];
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outputConfig.Dispose();
        _inputConfig.Dispose();
        _config.Dispose();
        _apm.Dispose();
    }
}

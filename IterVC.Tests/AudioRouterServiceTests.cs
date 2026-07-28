using IterVC.Audio;
using IterVC.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace IterVC.Tests;

[TestClass]
public sealed class AudioRouterServiceTests
{
    [TestMethod]
    public void AudioOutput_PreferredLatencyStartsWithoutFallback()
    {
        var requestedLatencies = new List<int>();
        var output = AudioLatencyPolicy.StartOutput(
            latency =>
            {
                requestedLatencies.Add(latency);
                return new FakeWavePlayer();
            },
            new SilenceWaveProvider(),
            "Test", "Device", 20, 40, NullLogger.Instance);

        Assert.AreEqual(1, requestedLatencies.Count);
        Assert.AreEqual(20, requestedLatencies[0]);
        Assert.AreEqual(PlaybackState.Playing, output.PlaybackState);
        output.Dispose();
    }

    [TestMethod]
    public void AudioOutput_FailedPreferredIsDisposedBeforeFallback()
    {
        var preferred = new FakeWavePlayer(failInitialization: true);
        var fallback = new FakeWavePlayer();

        var output = AudioLatencyPolicy.StartOutput(
            latency => latency == 20 ? preferred : fallback,
            new SilenceWaveProvider(),
            "Test", "Device", 20, 40, NullLogger.Instance);

        Assert.IsTrue(preferred.IsDisposed);
        Assert.AreSame(fallback, output);
        Assert.AreEqual(PlaybackState.Playing, fallback.PlaybackState);
        output.Dispose();
    }

    [TestMethod]
    public void AudioOutput_WhenBothAttemptsFailLeavesNoActiveOutput()
    {
        var preferred = new FakeWavePlayer(failInitialization: true);
        var fallback = new FakeWavePlayer(failInitialization: true);

        Assert.ThrowsException<AggregateException>(() => AudioLatencyPolicy.StartOutput(
            latency => latency == 20 ? preferred : fallback,
            new SilenceWaveProvider(),
            "Test", "Device", 20, 40, NullLogger.Instance));

        Assert.IsTrue(preferred.IsDisposed);
        Assert.IsTrue(fallback.IsDisposed);
    }

    [TestMethod]
    public async Task FeedMicrophoneSamples_ReportsRawInputRms()
    {
        using var router = CreateRouter();

        router.FeedMicrophoneSamples(ToBytes(Enumerable.Repeat(0.25f, 960).ToArray()), 960 * sizeof(float));

        await WaitUntilAsync(() => router.MicrophoneInputLevel.HasRecentSamples);
        Assert.AreEqual(-12.0412f, router.MicrophoneInputLevelDb, 0.001f);
    }

    [TestMethod]
    public async Task FeedMicrophoneSamples_ProcessesAudioWhileRoutingIsStopped()
    {
        using var router = CreateRouter();
        Assert.IsFalse(router.IsRouting);

        Feed(router, 0.5f);

        await WaitUntilAsync(() => router.MicrophoneOutputLevelDb > -7f);
        Assert.AreEqual(-6.0206f, router.MicrophoneOutputLevelDb, 0.02f);
    }

    [TestMethod]
    public async Task MicrophoneOutputLevel_ReflectsVolumeAndBoost()
    {
        using var router = CreateRouter();
        router.SetMicrophoneVolume(0.5f);
        router.SetMicrophoneBoost(2f);
        Feed(router, 0.25f);
        await WaitUntilAsync(() => router.MicrophoneOutputLevelDb > -13f);
        Assert.AreEqual(-12.0412f, router.MicrophoneOutputLevelDb, 0.02f);

        router.SetMicrophoneBoost(1f);
        Feed(router, 0.25f);
        await WaitUntilAsync(() => router.MicrophoneOutputLevelDb < -17f);
        Assert.AreEqual(-18.0618f, router.MicrophoneOutputLevelDb, 0.02f);
    }

    [TestMethod]
    public async Task NoiseGate_BlocksOutputBelowThresholdAndOpensAboveIt()
    {
        using var router = CreateRouter();
        router.ConfigureNoiseGate(true, -20f, 0f, 0f);

        Feed(router, 0.5f);
        await WaitUntilAsync(() => router.IsNoiseGateOpen && router.MicrophoneOutputLevelDb > -7f);
        Assert.AreEqual(-6.0206f, router.MicrophoneOutputLevelDb, 0.02f);

        Feed(router, 0.01f);
        await WaitUntilAsync(() => !router.IsNoiseGateOpen && router.MicrophoneOutputLevelDb == -80f);
        Assert.IsFalse(router.IsNoiseGateOpen);
        Assert.AreEqual(-80f, router.MicrophoneOutputLevelDb, 0.01f);
    }

    [TestMethod]
    public async Task ConfigureNoiseGate_ClampsThresholdAndNegativeRampDurations()
    {
        using var router = CreateRouter();

        router.ConfigureNoiseGate(true, -20f, 0f, 0f);
        Feed(router, 0.5f);
        await WaitUntilAsync(() => router.IsNoiseGateOpen && router.NoiseGateGain == 1f);

        router.ConfigureNoiseGate(true, 20f, -10f, -10f);
        Feed(router, 0.5f);
        await WaitUntilAsync(() => !router.IsNoiseGateOpen && router.MicrophoneOutputLevelDb == -80f);

        router.ConfigureNoiseGate(true, -100f, -10f, -10f);
        Feed(router, 0.5f);
        await WaitUntilAsync(() => router.IsNoiseGateOpen && router.NoiseGateGain == 1f);

        Feed(router, 0.0000316228f); // -90 dB: below the clamped -80 dB threshold.
        await WaitUntilAsync(() => !router.IsNoiseGateOpen && router.MicrophoneOutputLevelDb == -80f);
    }

    [TestMethod]
    public async Task MicrophoneOutputLevel_ResetsAndGateReportsClosedAfterStaleTimeout()
    {
        using var router = CreateRouter();
        router.ConfigureNoiseGate(true, -20f, 0f, 0f);
        Feed(router, 0.5f);
        await WaitUntilAsync(() => router.IsNoiseGateOpen);

        await WaitUntilAsync(() => router.MicrophoneOutputLevelDb == -80f && !router.IsNoiseGateOpen, 1000);

        Assert.AreEqual(-80f, router.MicrophoneOutputLevelDb);
        Assert.IsFalse(router.IsNoiseGateOpen);
    }

    [TestMethod]
    public async Task NoiseGate_WhenBypassed_ReportsOpenAndPreservesOutput()
    {
        using var router = CreateRouter();
        router.ConfigureNoiseGate(false, -1f, 1000f, 5000f);
        Feed(router, 0.1f);

        await WaitUntilAsync(() => router.MicrophoneOutputLevelDb > -21f);

        Assert.IsTrue(router.IsNoiseGateOpen);
        Assert.AreEqual(1f, router.NoiseGateGain, 0.0001f);
        Assert.AreEqual(-20f, router.MicrophoneOutputLevelDb, 0.02f);
    }

    private static IAudioRouterService CreateRouter() =>
        new AudioRouterService(NullLogger<AudioRouterService>.Instance);

    private static void Feed(IAudioRouterService router, float sample)
    {
        var bytes = ToBytes(Enumerable.Repeat(sample, 960).ToArray());
        router.FeedMicrophoneSamples(bytes, bytes.Length);
    }

    private static byte[] ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMilliseconds = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        Assert.IsTrue(predicate(), "Condition was not reached before the timeout.");
    }

    private sealed class SilenceWaveProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    private sealed class FakeWavePlayer(bool failInitialization = false) : IWavePlayer
    {
        public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public float Volume { get; set; } = 1f;
        public bool IsDisposed { get; private set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider)
        {
            if (failInitialization) throw new InvalidOperationException("Initialization failed.");
            OutputWaveFormat = waveProvider.WaveFormat;
        }

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop()
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }
        public void Dispose() => IsDisposed = true;
    }
}

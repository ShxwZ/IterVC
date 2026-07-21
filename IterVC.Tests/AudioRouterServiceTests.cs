using IterVC.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class AudioRouterServiceTests
{
    [TestMethod]
    public void FeedMicrophoneSamples_ReportsRawInputRms()
    {
        using var router = CreateRouter();

        router.FeedMicrophoneSamples(ToBytes(Enumerable.Repeat(0.25f, 960).ToArray()), 960 * sizeof(float));

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

    private static AudioRouterService CreateRouter() => new(NullLogger<AudioRouterService>.Instance);

    private static void Feed(AudioRouterService router, float sample)
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
}

using IterVC.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace IterVC.Tests;

[TestClass]
public sealed class NoiseGateSampleProviderTests
{
    private const float Tolerance = 1e-5f;

    [TestMethod]
    public void Read_WhenDisabled_PassesSamplesThroughAndReportsOpen()
    {
        var gate = CreateGate(new[] { 0.25f, -0.5f });
        var output = new float[2];

        var read = gate.Read(output, 0, output.Length);

        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new[] { 0.25f, -0.5f }, output);
        Assert.AreEqual(1f, gate.CurrentGain, Tolerance);
        Assert.IsTrue(gate.IsOpen);
        Assert.AreEqual(-8.0618f, gate.CurrentLevelDb, 0.001f);
    }

    [TestMethod]
    public void Read_BelowThreshold_ClosesUsingReleaseRamp()
    {
        var gate = CreateGate(Enumerable.Repeat(0.01f, 10).ToArray(), enabled: true);
        gate.ReleaseMilliseconds = 10f;
        var output = new float[10];

        gate.Read(output, 0, output.Length);

        Assert.IsFalse(gate.IsOpen);
        Assert.AreEqual(0f, gate.CurrentGain, Tolerance);
        Assert.AreEqual(0.009f, output[0], Tolerance);
        Assert.AreEqual(0f, output[^1], Tolerance);
        Assert.AreEqual(-40f, gate.CurrentLevelDb, 0.001f);
    }

    [TestMethod]
    public void Read_AboveThreshold_OpensUsingAttackRamp()
    {
        var source = Enumerable.Repeat(0.01f, 10).Concat(Enumerable.Repeat(1f, 10)).ToArray();
        var gate = CreateGate(source, enabled: true);
        gate.ReleaseMilliseconds = 0f;
        gate.AttackMilliseconds = 10f;
        var closed = new float[10];
        var opened = new float[10];

        gate.Read(closed, 0, closed.Length);
        gate.Read(opened, 0, opened.Length);

        Assert.IsTrue(gate.IsOpen);
        Assert.AreEqual(1f, gate.CurrentGain, Tolerance);
        Assert.AreEqual(0.1f, opened[0], Tolerance);
        Assert.AreEqual(1f, opened[^1], Tolerance);
        Assert.AreEqual(0f, gate.CurrentLevelDb, Tolerance);
    }

    [TestMethod]
    public void Read_HonorsOffsetAndCountWithoutChangingOtherSamples()
    {
        var gate = CreateGate(new[] { 0.2f, -0.3f });
        var output = Enumerable.Repeat(99f, 6).ToArray();

        var read = gate.Read(output, 2, 2);

        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new[] { 99f, 99f, 0.2f, -0.3f, 99f, 99f }, output);
    }

    [TestMethod]
    public void Read_WhenSourceIsEmpty_ReturnsZeroWithoutChangingState()
    {
        var gate = CreateGate(Array.Empty<float>(), enabled: true);
        var output = new float[4];

        var read = gate.Read(output, 0, output.Length);

        Assert.AreEqual(0, read);
        Assert.AreEqual(-80f, gate.CurrentLevelDb);
        Assert.AreEqual(1f, gate.CurrentGain);
        Assert.IsFalse(gate.IsOpen);
    }

    private static NoiseGateSampleProvider CreateGate(float[] samples, bool enabled = false) =>
        new(new ArraySampleProvider(samples)) { Enabled = enabled, ThresholdDb = -20f };

    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(1000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}

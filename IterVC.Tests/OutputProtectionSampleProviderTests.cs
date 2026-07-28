using IterVC.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace IterVC.Tests;

[TestClass]
public sealed class OutputProtectionSampleProviderTests
{
    [TestMethod]
    public void NormalSignalIsTransparent()
    {
        var source = new ArrayProvider([-0.5f, 0f, 0.5f]);
        var protection = new OutputProtectionSampleProvider(source);
        var output = new float[3];
        Assert.AreEqual(3, protection.Read(output, 0, output.Length));
        CollectionAssert.AreEqual(new[] { -0.5f, 0f, 0.5f }, output);
        Assert.AreEqual(0, protection.ActivationBlocks);
    }

    [TestMethod]
    public void OversAreContinuouslyProtectedWithoutDelay()
    {
        var protection = new OutputProtectionSampleProvider(new ArrayProvider([0.89f, 0.9f, 1f, 2f, -2f]));
        var output = new float[5];
        Assert.AreEqual(5, protection.Read(output, 0, output.Length));
        Assert.AreEqual(0.89f, output[0], 0.00001f);
        Assert.IsTrue(output[1] > output[0]);
        Assert.IsTrue(output[2] > output[1]);
        Assert.IsTrue(output[3] > output[2] && output[3] <= 1f);
        Assert.AreEqual(-output[3], output[4], 0.00001f);
        Assert.AreEqual(1, protection.ActivationBlocks);
    }

    [TestMethod]
    public void NonFiniteInputBecomesSilence()
    {
        var protection = new OutputProtectionSampleProvider(new ArrayProvider([float.NaN, float.PositiveInfinity]));
        var output = new float[2];
        protection.Read(output, 0, 2);
        CollectionAssert.AreEqual(new[] { 0f, 0f }, output);
    }

    [TestMethod]
    public void PreProtectionDiagnosticsSeeOversWhilePostMeterStaysClamped()
    {
        var diagnostics = new PreProtectionDiagnosticsSampleProvider(new ArrayProvider([2f, -2f]));
        var protection = new OutputProtectionSampleProvider(diagnostics);
        var meter = new LevelMeterSampleProvider(protection);
        meter.Read(new float[2], 0, 2);

        var signal = diagnostics.GetSnapshot(protection.ActivationBlocks);
        var ui = meter.GetSnapshot(TimeSpan.FromSeconds(1));
        Assert.AreEqual(20f * MathF.Log10(2f), signal.UnclampedPeakDb, 0.001f);
        Assert.AreEqual(2, signal.OverSampleCount);
        Assert.AreEqual(1, signal.OverBlockCount);
        Assert.AreEqual(1, signal.ProtectionActivationCount);
        Assert.AreEqual(0f, ui.PeakDb);
    }

    private sealed class ArrayProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, read);
            _position += read;
            return read;
        }
    }
}

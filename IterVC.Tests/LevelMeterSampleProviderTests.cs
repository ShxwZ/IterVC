using IterVC.Audio;
using IterVC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace IterVC.Tests;

[TestClass]
public sealed class LevelMeterSampleProviderTests
{
    [TestMethod]
    public void Read_ForwardsSamplesAndCalculatesRmsAndPeak()
    {
        var meter = new LevelMeterSampleProvider(new ArraySampleProvider([0.5f, -1f]));
        var output = new float[4];

        var read = meter.Read(output, 1, 2);
        var snapshot = meter.GetSnapshot(TimeSpan.FromSeconds(1));

        Assert.AreEqual(2, read);
        CollectionAssert.AreEqual(new[] { 0f, 0.5f, -1f, 0f }, output);
        Assert.AreEqual(-2.0412f, snapshot.LevelDb, 0.001f);
        Assert.AreEqual(0f, snapshot.PeakDb, 0.001f);
        Assert.IsTrue(snapshot.HasRecentSamples);
    }

    [TestMethod]
    public void Read_SilentBlockIsRecentAtMinimumLevel()
    {
        var meter = new LevelMeterSampleProvider(new ArraySampleProvider([0f, 0f]));

        meter.Read(new float[2], 0, 2);
        var snapshot = meter.GetSnapshot(TimeSpan.FromSeconds(1));

        Assert.AreEqual(AudioLevelSnapshot.MinimumDb, snapshot.LevelDb);
        Assert.AreEqual(AudioLevelSnapshot.MinimumDb, snapshot.PeakDb);
        Assert.IsTrue(snapshot.HasRecentSamples);
    }

    [TestMethod]
    public void GetSnapshot_WhenReadIsStale_ReturnsSilence()
    {
        var meter = new LevelMeterSampleProvider(new ArraySampleProvider([0.5f]));
        meter.Read(new float[1], 0, 1);

        Assert.AreEqual(AudioLevelSnapshot.Silence, meter.GetSnapshot(TimeSpan.Zero));
    }

    [TestMethod]
    public void Read_WhenSourceReturnsZero_DoesNotRefreshTimestamp()
    {
        var meter = new LevelMeterSampleProvider(new ArraySampleProvider([]));

        Assert.AreEqual(0, meter.Read(new float[1], 0, 1));
        Assert.AreEqual(AudioLevelSnapshot.Silence, meter.GetSnapshot(TimeSpan.FromSeconds(1)));
    }

    private sealed class ArraySampleProvider(float[] samples) : ISampleProvider
    {
        private int _position;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - _position);
            Array.Copy(samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}

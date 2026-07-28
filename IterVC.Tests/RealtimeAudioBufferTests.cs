using IterVC.Audio.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;

namespace IterVC.Tests;

[TestClass]
public sealed class RealtimeAudioBufferTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(1000, 2);

    [TestMethod]
    public void Write_IgnoresIncompleteFrames()
    {
        var buffer = new RealtimeAudioBuffer(Format, false);
        buffer.Write(new byte[Format.BlockAlign + 3], 0, Format.BlockAlign + 3);
        Assert.AreEqual(1, buffer.GetSnapshot().TotalFramesWritten);
    }

    [TestMethod]
    public void Read_UnderflowFillsSilenceWithoutRepeatingStaleData()
    {
        var buffer = new RealtimeAudioBuffer(Format, true);
        var source = Enumerable.Repeat((byte)7, Format.BlockAlign).ToArray();
        buffer.Write(source, 0, source.Length);
        var destination = new byte[Format.BlockAlign * 2];

        Assert.AreEqual(destination.Length, buffer.Read(destination, 0, destination.Length));
        CollectionAssert.AreEqual(source, destination[..Format.BlockAlign]);
        CollectionAssert.AreEqual(new byte[Format.BlockAlign], destination[Format.BlockAlign..]);
        Assert.AreEqual(1, buffer.GetSnapshot().UnderflowBlocks);
    }

    [TestMethod]
    public void DrainProbe_DoesNotCountExpectedEmptyReadAsUnderflow()
    {
        var buffer = new RealtimeAudioBuffer(Format, readFully: false, countUnderflows: false);
        var destination = new byte[Format.BlockAlign];

        Assert.AreEqual(0, buffer.Read(destination, 0, destination.Length));
        Assert.AreEqual(0, buffer.GetSnapshot().UnderflowBlocks);
    }

    [TestMethod]
    public void Overflow_PreservesNewestCompleteFrames()
    {
        var buffer = new RealtimeAudioBuffer(Format, false, capacityMilliseconds: 10);
        var old = Enumerable.Repeat((byte)1, Format.AverageBytesPerSecond / 100).ToArray();
        var newest = Enumerable.Repeat((byte)2, old.Length).ToArray();
        buffer.Write(old, 0, old.Length);
        buffer.Write(newest, 0, newest.Length);
        var result = new byte[newest.Length];

        Assert.AreEqual(result.Length, buffer.Read(result, 0, result.Length));
        CollectionAssert.AreEqual(newest, result);
        Assert.AreEqual(1, buffer.GetSnapshot().OverflowEvents);
        Assert.IsTrue(buffer.GetSnapshot().DiscardedFrames > 0);
    }

    [TestMethod]
    public void ExcessOccupancy_CorrectsTowardTargetWithHysteresis()
    {
        var buffer = new RealtimeAudioBuffer(Format, false);
        var fortyMilliseconds = new byte[Format.AverageBytesPerSecond * 40 / 1000];
        buffer.Write(fortyMilliseconds, 0, fortyMilliseconds.Length);
        Assert.AreEqual(0, buffer.GetSnapshot().LatencyCorrections);

        buffer.Write(new byte[Format.AverageBytesPerSecond * 20 / 1000], 0,
            Format.AverageBytesPerSecond * 20 / 1000);
        buffer.Write(new byte[Format.BlockAlign], 0, Format.BlockAlign);
        Assert.AreEqual(1, buffer.GetSnapshot().LatencyCorrections);
        Assert.IsTrue(buffer.GetSnapshot().BufferedMilliseconds < 25);
    }

    [TestMethod]
    public void ReadWrite_WrapAroundPreservesFrameOrder()
    {
        var buffer = new RealtimeAudioBuffer(Format, false, capacityMilliseconds: 10);
        var first = Enumerable.Range(0, 10).SelectMany(Frame).ToArray();
        buffer.Write(first, 0, first.Length);
        var consumed = new byte[6 * Format.BlockAlign];
        buffer.Read(consumed, 0, consumed.Length);
        var second = Enumerable.Range(10, 6).SelectMany(Frame).ToArray();
        buffer.Write(second, 0, second.Length);
        var remaining = new byte[10 * Format.BlockAlign];
        buffer.Read(remaining, 0, remaining.Length);

        CollectionAssert.AreEqual(Enumerable.Range(6, 10).SelectMany(Frame).ToArray(), remaining);
    }

    [TestMethod]
    public async Task ProducerAndConsumer_KeepFramesIntact()
    {
        var buffer = new RealtimeAudioBuffer(Format, false, capacityMilliseconds: 120);
        var received = new List<int>();
        var producer = Task.Run(async () =>
        {
            for (var value = 0; value < 50; value++)
            {
                var frame = Frame(value);
                buffer.Write(frame, 0, frame.Length);
                await Task.Delay(1);
            }
        });
        var consumer = Task.Run(async () =>
        {
            var frame = new byte[Format.BlockAlign];
            while (!producer.IsCompleted || buffer.GetSnapshot().BufferedMilliseconds > 0)
            {
                if (buffer.Read(frame, 0, frame.Length) == frame.Length)
                    received.Add(BitConverter.ToInt32(frame, 0));
                else
                    await Task.Delay(1);
            }
        });

        await Task.WhenAll(producer, consumer);
        CollectionAssert.AreEqual(Enumerable.Range(0, 50).ToArray(), received);
    }

    private static byte[] Frame(int value)
    {
        var frame = new byte[Format.BlockAlign];
        BitConverter.GetBytes(value).CopyTo(frame, 0);
        return frame;
    }
}

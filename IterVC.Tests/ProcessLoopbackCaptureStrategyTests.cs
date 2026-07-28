using IterVC.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class ProcessLoopbackCaptureStrategyTests
{
    [TestMethod]
    public void DefaultStrategyPreservesCustomMultichannelDownmix()
    {
        Assert.AreEqual(ProcessLoopbackCaptureStrategy.CustomMultichannel,
            ProcessLoopbackCapture.DefaultStrategy);
    }

    [TestMethod]
    public void StandardStereoBypassesEndpointDetection()
    {
        var detectorCalled = false;
        var format = ProcessLoopbackCapture.SelectRequestedFormat(
            ProcessLoopbackCaptureStrategy.StandardStereo,
            () =>
            {
                detectorCalled = true;
                return new ProcessRenderFormat(48000, 8, "7.1");
            });

        Assert.IsFalse(detectorCalled);
        Assert.AreEqual(48000, format.SampleRate);
        Assert.AreEqual(2, format.Channels);
    }

    [TestMethod]
    public void CustomMultichannelUsesDetectedFormat()
    {
        var format = ProcessLoopbackCapture.SelectRequestedFormat(
            ProcessLoopbackCaptureStrategy.CustomMultichannel,
            () => new ProcessRenderFormat(48000, 8, "7.1"));
        Assert.AreEqual(8, format.Channels);
    }
}

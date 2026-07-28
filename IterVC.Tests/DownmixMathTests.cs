using IterVC.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class DownmixMathTests
{
    private const float Tolerance = 1e-5f;

    [TestMethod]
    public void Downmix5_1_MapsChannelsWithDocumentedHeadroom()
    {
        var frontLeft = DownmixMath.Downmix5_1([1, 0, 0, 0, 0, 0]);
        var center = DownmixMath.Downmix5_1([0, 0, 1, 0, 0, 0]);
        var surround = DownmixMath.Downmix5_1([0, 0, 0, 0, 1, 0]);
        var lfe = DownmixMath.Downmix5_1([0, 0, 0, 1, 0, 0]);

        Assert.AreEqual(1f, frontLeft.Left, Tolerance);
        Assert.AreEqual(0, frontLeft.Right, Tolerance);
        Assert.AreEqual(DownmixMath.InvSqrt2, center.Left, Tolerance);
        Assert.AreEqual(center.Left, center.Right, Tolerance);
        Assert.AreEqual(DownmixMath.InvSqrt2, surround.Left, Tolerance);
        Assert.AreEqual(0, surround.Right, Tolerance);
        Assert.AreEqual(DownmixMath.LfeGain, lfe.Left, Tolerance);
        Assert.AreEqual(lfe.Left, lfe.Right, Tolerance);
    }

    [TestMethod]
    public void Downmix5_1_AllCorrelatedChannelsRemainLinearForFinalProtection()
    {
        var result = DownmixMath.Downmix5_1([1, 1, 1, 1, 1, 1]);
        Assert.IsTrue(result.Left > 1);
        Assert.IsTrue(result.Right > 1);
    }

    [TestMethod]
    public void Downmix7_1_MapsBackAndSideChannelsToTheirOwnSide()
    {
        var result = DownmixMath.Downmix7_1([0, 0, 0, 0, 1, 0, 1, 0]);
        var expected = 0.5f + DownmixMath.InvSqrt2;
        Assert.AreEqual(expected, result.Left, Tolerance);
        Assert.AreEqual(0, result.Right, Tolerance);
    }

    [TestMethod]
    public void Downmix_DoesNotHardClampIntermediateFloatSamples()
    {
        var result = DownmixMath.Downmix5_1([2, 2, 2, 2, 2, 2]);
        Assert.IsTrue(result.Left > 1);
        Assert.IsTrue(result.Right > 1);
    }

    [TestMethod]
    public void Downmix_OppositePhaseFrontChannelsPreserveSides()
    {
        var result = DownmixMath.Downmix5_1([1, -1, 0, 0, 0, 0]);
        Assert.AreEqual(1f, result.Left, Tolerance);
        Assert.AreEqual(-1f, result.Right, Tolerance);
    }

    [TestMethod]
    public void DownmixGeneric_MonoDuplicatesAndStereoPreservesPlacement()
    {
        Assert.AreEqual((0.42f, 0.42f), DownmixMath.DownmixGeneric([0.42f]));
        var stereo = DownmixMath.DownmixGeneric([0.5f, -0.7f]);
        Assert.AreEqual(0.5f, stereo.Left, Tolerance);
        Assert.AreEqual(-0.7f, stereo.Right, Tolerance);
    }

    [TestMethod]
    public void DownmixForChannels_UsesExpectedMatrix()
    {
        float[] fiveOne = [1, 0, 0, 0, 0, 0];
        float[] sevenOne = [1, 0, 0, 0, 0, 0, 0, 0];
        Assert.AreEqual(DownmixMath.Downmix5_1(fiveOne), DownmixMath.DownmixForChannels(fiveOne));
        Assert.AreEqual(DownmixMath.Downmix7_1(sevenOne), DownmixMath.DownmixForChannels(sevenOne));
    }

    [DataTestMethod]
    [DataRow(0, 1f, 0f)]
    [DataRow(1, 0f, 1f)]
    [DataRow(2, 0.70710678f, 0.70710678f)]
    [DataRow(3, 0.5f, 0.5f)]
    [DataRow(4, 0.70710678f, 0f)]
    [DataRow(5, 0f, 0.70710678f)]
    public void Downmix5_1_EachChannelHasDeterministicPlacement(
        int channel, float expectedLeftWeight, float expectedRightWeight)
    {
        var frame = new float[6];
        frame[channel] = 1;
        var result = DownmixMath.Downmix5_1(frame);
        Assert.AreEqual(expectedLeftWeight, result.Left, Tolerance);
        Assert.AreEqual(expectedRightWeight, result.Right, Tolerance);
    }

    [DataTestMethod]
    [DataRow(0, 1f, 0f)]
    [DataRow(1, 0f, 1f)]
    [DataRow(2, 0.70710678f, 0.70710678f)]
    [DataRow(3, 0.5f, 0.5f)]
    [DataRow(4, 0.5f, 0f)]
    [DataRow(5, 0f, 0.5f)]
    [DataRow(6, 0.70710678f, 0f)]
    [DataRow(7, 0f, 0.70710678f)]
    public void Downmix7_1_EachChannelHasDeterministicPlacement(
        int channel, float expectedLeftWeight, float expectedRightWeight)
    {
        var frame = new float[8];
        frame[channel] = 1;
        var result = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(expectedLeftWeight, result.Left, Tolerance);
        Assert.AreEqual(expectedRightWeight, result.Right, Tolerance);
    }

    [TestMethod]
    public void DownmixSilenceRemainsSilence()
    {
        Assert.AreEqual((0f, 0f), DownmixMath.Downmix5_1(new float[6]));
        Assert.AreEqual((0f, 0f), DownmixMath.Downmix7_1(new float[8]));
    }
}

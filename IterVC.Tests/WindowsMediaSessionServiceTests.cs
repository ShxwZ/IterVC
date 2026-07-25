using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class WindowsMediaSessionServiceTests
{
    [TestMethod]
    public void FormatTitle_IncludesArtistOnlyWhenPresent()
    {
        Assert.AreEqual("Artist - Track", WindowsMediaSessionService.FormatTitle("Artist", "Track"));
        Assert.AreEqual("Track", WindowsMediaSessionService.FormatTitle(" ", "Track"));
    }

    [TestMethod]
    public void FormatTime_PreservesMinuteBasedDisplay()
    {
        Assert.AreEqual("61:02 / 90:03", WindowsMediaSessionService.FormatTime(
            TimeSpan.FromMinutes(61) + TimeSpan.FromSeconds(2),
            TimeSpan.FromMinutes(90) + TimeSpan.FromSeconds(3)));
    }

    [TestMethod]
    public void EstimatePosition_AddsElapsedTimeAndClampsToEnd()
    {
        var updated = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(TimeSpan.FromSeconds(15), WindowsMediaSessionService.EstimatePosition(
            TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), updated, updated.AddSeconds(5)));
        Assert.AreEqual(TimeSpan.FromMinutes(1), WindowsMediaSessionService.EstimatePosition(
            TimeSpan.FromSeconds(59), TimeSpan.FromMinutes(1), updated, updated.AddSeconds(5)));
    }
}

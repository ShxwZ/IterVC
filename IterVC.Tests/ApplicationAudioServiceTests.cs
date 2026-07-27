using IterVC.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class ApplicationAudioServiceTests
{
    [TestMethod]
    public void GetSessionDisplayName_WhenBlank_UsesProcessDisplayName()
    {
        Assert.AreEqual("Google Chrome",
            ApplicationAudioService.GetSessionDisplayName(" ", "chrome"));
    }

    [TestMethod]
    public void GetSessionDisplayName_WhenLiteral_PreservesReadableName()
    {
        Assert.AreEqual("Spotify",
            ApplicationAudioService.GetSessionDisplayName(" Spotify ", "spotify"));
    }

    [TestMethod]
    public void GetSessionDisplayName_WhenIndirect_UsesResolvedResource()
    {
        const string source = @"@%SystemRoot%\System32\AudioSrv.dll,-202";

        var result = ApplicationAudioService.GetSessionDisplayName(
            source,
            "svchost",
            value => value == source ? "System Sounds" : null);

        Assert.AreEqual("System Sounds", result);
    }

    [TestMethod]
    public void GetSessionDisplayName_WhenIndirectCannotResolve_HidesSession()
    {
        var result = ApplicationAudioService.GetSessionDisplayName(
            @"@%SystemRoot%\System32\AudioSrv.dll,-202",
            "svchost",
            _ => null);

        Assert.IsNull(result);
    }
}

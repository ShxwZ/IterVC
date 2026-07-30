using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class AppLaunchOptionsTests
{
    [TestMethod]
    public void Parse_RecognizesWindowsStartupFlagCaseInsensitively()
    {
        Assert.IsTrue(AppLaunchOptions.Parse(["--WINDOWS-STARTUP"]).IsWindowsStartup);
    }

    [DataTestMethod]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    public void ShouldStartHidden_RequiresStartupArgumentAndPreference(bool startupArgument, bool preference, bool expected)
    {
        Assert.AreEqual(expected, new AppLaunchOptions(startupArgument).ShouldStartHidden(preference));
    }

    [TestMethod]
    public void Parse_IgnoresUnrelatedArguments()
    {
        Assert.IsFalse(AppLaunchOptions.Parse(["--profile", "--windows-startup-extra"]).IsWindowsStartup);
    }
}

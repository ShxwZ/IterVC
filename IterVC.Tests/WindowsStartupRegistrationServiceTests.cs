using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class WindowsStartupRegistrationServiceTests
{
    [TestMethod]
    public void FormatCommand_QuotesPathAndUsesOnlyStartupArgument()
    {
        Assert.AreEqual("\"C:\\Program Files\\IterVC\\IterVC.exe\" --windows-startup",
            WindowsStartupRegistrationService.FormatCommand("C:\\Program Files\\IterVC\\IterVC.exe"));
    }

    [DataTestMethod]
    [DataRow("\"C:\\IterVC\\IterVC.exe\" --windows-startup", "C:\\IterVC\\IterVC.exe")]
    [DataRow("\"C:\\Path With Spaces\\IterVC.exe\" --windows-startup", "C:\\Path With Spaces\\IterVC.exe")]
    public void ParseCommand_AcceptsOnlyCanonicalQuotedStartupCommand(string command, string expectedPath)
    {
        Assert.AreEqual(expectedPath, WindowsStartupRegistrationService.ParseCommand(command));
    }

    [DataTestMethod]
    [DataRow("C:\\IterVC\\IterVC.exe --windows-startup")]
    [DataRow("\"C:\\IterVC\\IterVC.exe\" --other")]
    [DataRow("\"C:\\IterVC\\IterVC.exe\" --windows-startup & calc.exe")]
    public void ParseCommand_RejectsMalformedOrUnsafeValues(string command)
    {
        Assert.IsNull(WindowsStartupRegistrationService.ParseCommand(command));
    }
}

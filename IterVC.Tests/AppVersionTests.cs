using IterVC.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class AppVersionTests
{
    [DataTestMethod]
    [DataRow(null, AppVersion.LocalDevelopment)]
    [DataRow("", AppVersion.LocalDevelopment)]
    [DataRow("local-development", AppVersion.LocalDevelopment)]
    [DataRow("v1.2.3", "alpha-1.2.3")]
    [DataRow("1.2.3", "alpha-1.2.3")]
    [DataRow("alpha-1.2.3", "alpha-1.2.3")]
    [DataRow("alpha-v1.2.3", "alpha-1.2.3")]
    [DataRow("refs/tags/v2.0.0-beta.1", "alpha-2.0.0-beta.1")]
    public void Format_NormalizesBuildVersion(string? input, string expected)
    {
        Assert.AreEqual(expected, AppVersion.Format(input));
    }

    [DataTestMethod]
    [DataRow("1.2.4", "1.2.3", true)]
    [DataRow("v2.0.0", "1.9.9", true)]
    [DataRow("1.0.0", "1.0.0-beta.2", true)]
    [DataRow("1.0.0-beta.10", "1.0.0-beta.2", true)]
    [DataRow("1.0.0-beta.1", "1.0.0", false)]
    [DataRow("1.2.3", "1.2.4", false)]
    [DataRow("not-a-version", "1.0.0", false)]
    public void IsNewer_UsesSemanticVersionPrecedence(string candidate, string current, bool expected)
    {
        Assert.AreEqual(expected, AppVersion.IsNewer(candidate, current));
    }

    [DataTestMethod]
    [DataRow("refs/tags/v1.2.3", "1.2.3")]
    [DataRow("alpha-v1.2", "1.2.0")]
    [DataRow("v1.2.3-beta.1+build.7", "1.2.3-beta.1")]
    [DataRow(AppVersion.LocalDevelopment, null)]
    public void NormalizeSemantic_NormalizesSupportedReleaseTags(string input, string? expected)
    {
        Assert.AreEqual(expected, AppVersion.NormalizeSemantic(input));
    }

    [DataTestMethod]
    [DataRow("1.4.0", "1.3.0", "1.4.0", false)]
    [DataRow("v1.4.0", "1.3.0", "refs/tags/v1.4.0", false)]
    [DataRow("1.5.0", "1.3.0", "1.4.0", true)]
    [DataRow("1.3.0", "1.3.0", null, false)]
    public void ShouldNotify_RespectsExactDismissedRelease(
        string candidate, string current, string? dismissed, bool expected)
    {
        Assert.AreEqual(expected, AppVersion.ShouldNotify(candidate, current, dismissed));
    }

    [TestMethod]
    public void ShouldNotify_ManualCheckOverridesDismissal()
    {
        Assert.IsTrue(AppVersion.ShouldNotify("1.4.0", "1.3.0", "1.4.0", ignoreDismissal: true));
    }
}

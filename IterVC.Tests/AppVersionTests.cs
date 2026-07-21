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
}

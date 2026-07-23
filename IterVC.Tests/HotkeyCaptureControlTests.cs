using Avalonia.Input;
using IterVC.Desktop.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class HotkeyCaptureControlTests
{
    [TestMethod]
    public void SettingsPanel_Construction_DoesNotRunTabHandlerBeforeNamedControlsExist()
    {
        var panel = new SettingsPanel();
        Assert.IsNotNull(panel);
    }

    [DataTestMethod]
    [DataRow(Key.A, "A")]
    [DataRow(Key.D7, "7")]
    [DataRow(Key.F24, "F24")]
    [DataRow(Key.Back, "Backspace")]
    [DataRow(Key.PageDown, "PageDown")]
    public void FormatKey_SupportedKey_ReturnsCanonicalName(Key key, string expected) =>
        Assert.AreEqual(expected, HotkeyCaptureControl.FormatKey(key));

    [TestMethod]
    public void FormatKey_UnsupportedKey_ReturnsNull() =>
        Assert.IsNull(HotkeyCaptureControl.FormatKey(Key.OemTilde));

    [DataTestMethod]
    [DataRow(Key.LeftCtrl)]
    [DataRow(Key.RightAlt)]
    [DataRow(Key.LeftShift)]
    [DataRow(Key.RWin)]
    public void IsModifier_ModifierKey_ReturnsTrue(Key key) =>
        Assert.IsTrue(HotkeyCaptureControl.IsModifier(key));
}

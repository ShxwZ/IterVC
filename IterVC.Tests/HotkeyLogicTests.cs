using IterVC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IterVC.Tests;

[TestClass]
public sealed class HotkeyLogicTests
{
    [DataTestMethod]
    [DataRow("Ctrl+Shift+R", 5u, 0x52u)]
    [DataRow(" control + r ", 1u, 0x52u)]
    [DataRow("Windows+PageDown", 8u, 0x22u)]
    [DataRow("F24", 0u, 0x87u)]
    [DataRow("7", 0u, 0x37u)]
    public void TryParse_SupportedGesture_ReturnsSemanticValue(string gesture, uint modifiers, uint key)
    {
        Assert.IsTrue(HotkeyGestureParser.TryParse(gesture, out var parsed));
        Assert.AreEqual(modifiers, parsed.Modifiers);
        Assert.AreEqual(key, parsed.Key);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("Ctrl+")]
    [DataRow("Ctrl+Ctrl+R")]
    [DataRow("Hyper+R")]
    [DataRow("F25")]
    [DataRow("Ctrl+Shift")]
    public void TryParse_InvalidGesture_ReturnsFalse(string gesture) =>
        Assert.IsFalse(HotkeyGestureParser.TryParse(gesture, out _));

    [TestMethod]
    public void Build_SemanticAliasDuplicate_RejectsAndPreservesCurrentBindings()
    {
        var existing = new Dictionary<HotkeyAction, ParsedHotkeyGesture>
        {
            [HotkeyAction.ToggleRouting] = new(5, 0x52)
        };
        var candidate = new[]
        {
            new HotkeyBinding(HotkeyAction.StartRouting, true, "Ctrl+R"),
            new HotkeyBinding(HotkeyAction.StopRouting, true, "Control+R")
        };

        var result = HotkeyConfiguration.Build(candidate, existing);

        Assert.AreEqual(1, result.Errors.Count);
        Assert.IsTrue(result.Errors.ContainsKey(HotkeyAction.StopRouting));
        CollectionAssert.AreEquivalent(existing.Keys.ToArray(), result.Bindings.Keys.ToArray());
        Assert.AreEqual(existing[HotkeyAction.ToggleRouting], result.Bindings[HotkeyAction.ToggleRouting]);
    }

    [TestMethod]
    public void Build_InvalidGesture_RejectsTransactionWithoutPartialReplacement()
    {
        var existing = new Dictionary<HotkeyAction, ParsedHotkeyGesture>
        {
            [HotkeyAction.ToggleRouting] = new(5, 0x52)
        };
        var candidate = new[]
        {
            new HotkeyBinding(HotkeyAction.StartRouting, true, "Alt+S"),
            new HotkeyBinding(HotkeyAction.StopRouting, true, "not-a-key")
        };

        var result = HotkeyConfiguration.Build(candidate, existing);

        Assert.IsTrue(result.Errors.ContainsKey(HotkeyAction.StopRouting));
        Assert.IsTrue(result.Bindings.ContainsKey(HotkeyAction.ToggleRouting));
        Assert.IsFalse(result.Bindings.ContainsKey(HotkeyAction.StartRouting));
    }

    [TestMethod]
    public void Build_ValidCandidate_ReplacesBindingsAndOmitsDisabledActions()
    {
        var candidate = new[]
        {
            new HotkeyBinding(HotkeyAction.StartRouting, true, "Alt+S"),
            new HotkeyBinding(HotkeyAction.StopRouting, false, "Alt+X")
        };

        var result = HotkeyConfiguration.Build(candidate, new Dictionary<HotkeyAction, ParsedHotkeyGesture>());

        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(1, result.Bindings.Count);
        Assert.IsTrue(result.Bindings.ContainsKey(HotkeyAction.StartRouting));
    }

    [TestMethod]
    public void KeyState_CombinesModifiersAndSuppressesAutoRepeatUntilRelease()
    {
        var state = new HotkeyKeyState();
        var keyboard = new IntPtr(1);
        Assert.IsNull(state.Process(keyboard, 0xA2, 29, 0, false));
        Assert.AreEqual(1u, state.Process(keyboard, 0x52, 19, 0, false));
        Assert.IsNull(state.Process(keyboard, 0x52, 19, 0, false));
        Assert.IsNull(state.Process(keyboard, 0x52, 19, 0, true));
        Assert.AreEqual(1u, state.Process(keyboard, 0x52, 19, 0, false));
    }

    [TestMethod]
    public void KeyState_TracksPhysicalDevicesIndependently()
    {
        var state = new HotkeyKeyState();
        Assert.IsNull(state.Process(new IntPtr(1), 0xA2, 29, 0, false));
        Assert.IsNull(state.Process(new IntPtr(2), 0xA4, 56, 0, false));
        Assert.AreEqual(3u, state.Process(new IntPtr(1), 0x52, 19, 0, false));
    }

    [TestMethod]
    public void KeyState_ResetClearsModifiersAndPressedKeys()
    {
        var state = new HotkeyKeyState();
        var keyboard = new IntPtr(1);
        state.Process(keyboard, 0xA2, 29, 0, false);
        state.Process(keyboard, 0x52, 19, 0, false);
        state.Reset();
        Assert.AreEqual(0u, state.Process(keyboard, 0x52, 19, 0, false));
    }
}

using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class HotkeysViewModelTests
{
    [TestMethod]
    public void Constructor_CreatesExactlyOneRowPerAction()
    {
        var viewModel = CreateViewModel(new Mock<IGlobalHotkeyService>(), new Mock<ISettingsService>());
        CollectionAssert.AreEquivalent(Enum.GetValues<HotkeyAction>(), viewModel.Rows.Select(row => row.Action).ToArray());
        Assert.AreEqual(Enum.GetValues<HotkeyAction>().Length, viewModel.Rows.Count);
    }

    [TestMethod]
    public async Task CompleteCaptureAsync_SemanticConflictPreservesRowsAndSkipsInfrastructure()
    {
        var hotkeys = new Mock<IGlobalHotkeyService>();
        var settings = new Mock<ISettingsService>();
        var viewModel = CreateViewModel(hotkeys, settings);
        viewModel.Hydrate(new AppSettings
        {
            ToggleRoutingHotkeyEnabled = true,
            ToggleRoutingHotkeyGesture = "Control+R"
        });
        viewModel.BeginCapture(HotkeyAction.StartRouting);

        Assert.IsFalse(await viewModel.CompleteCaptureAsync("Ctrl+R"));
        Assert.IsFalse(viewModel.Rows.Single(row => row.Action == HotkeyAction.StartRouting).IsAssigned);
        hotkeys.Verify(service => service.Configure(It.IsAny<IReadOnlyList<HotkeyBinding>>()), Times.Never);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CompleteCaptureAsync_RegistrationFailureDoesNotMutateOrPersist()
    {
        var hotkeys = new Mock<IGlobalHotkeyService>();
        hotkeys.Setup(service => service.Configure(It.IsAny<IReadOnlyList<HotkeyBinding>>()))
            .Returns(new HotkeyRegistrationResult(new Dictionary<HotkeyAction, string>
            {
                [HotkeyAction.StartRouting] = "failed"
            }));
        var settings = new Mock<ISettingsService>();
        var viewModel = CreateViewModel(hotkeys, settings);
        viewModel.BeginCapture(HotkeyAction.StartRouting);

        Assert.IsFalse(await viewModel.CompleteCaptureAsync("Alt+S"));
        Assert.AreEqual(string.Empty, viewModel.Rows.Single(row => row.Action == HotkeyAction.StartRouting).Gesture);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ClearAsync_SuccessUpdatesRegistrationAndPersistsOnce()
    {
        var hotkeys = new Mock<IGlobalHotkeyService>();
        hotkeys.Setup(service => service.Configure(It.IsAny<IReadOnlyList<HotkeyBinding>>()))
            .Returns(new HotkeyRegistrationResult(new Dictionary<HotkeyAction, string>()));
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(hotkeys, settings);
        viewModel.Hydrate(new AppSettings { ToggleRoutingHotkeyEnabled = true, ToggleRoutingHotkeyGesture = "Ctrl+R" });

        Assert.IsTrue(await viewModel.ClearAsync(HotkeyAction.ToggleRouting));
        Assert.IsFalse(viewModel.Rows.Single(row => row.Action == HotkeyAction.ToggleRouting).IsAssigned);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static HotkeysViewModel CreateViewModel(Mock<IGlobalHotkeyService> hotkeys, Mock<ISettingsService> settings) =>
        new(hotkeys.Object, settings.Object, new TextsViewModel());
}

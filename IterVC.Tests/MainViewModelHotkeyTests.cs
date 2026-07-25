using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class MainViewModelHotkeyTests
{
    [TestMethod]
    public async Task CompleteShortcutCapture_RegistrationFailurePreservesPreviousBinding()
    {
        var hotkeys = new Mock<IGlobalHotkeyService>();
        hotkeys.Setup(service => service.Configure(It.IsAny<IReadOnlyList<HotkeyBinding>>()))
            .Returns(new HotkeyRegistrationResult(new Dictionary<HotkeyAction, string>
            {
                [HotkeyAction.StartRouting] = "registration failed"
            }));
        var viewModel = CreateViewModel(hotkeys);
        viewModel.Settings.Hotkeys.Hydrate(new AppSettings { StartRoutingHotkeyEnabled = true, StartRoutingHotkeyGesture = "Alt+S" });
        viewModel.Settings.Hotkeys.BeginCapture(HotkeyAction.StartRouting);

        var completed = await viewModel.Settings.Hotkeys.CompleteCaptureAsync("Ctrl+S");

        Assert.IsFalse(completed);
        Assert.AreEqual("Alt+S", viewModel.Settings.Hotkeys.Row(HotkeyAction.StartRouting).Gesture);
        Assert.AreEqual("registration failed", viewModel.Settings.Hotkeys.CaptureError);
        await viewModel.DisposeAsync();
    }

    [TestMethod]
    public async Task BeginShortcutCapture_UnknownActionIsRejectedWithoutRegistration()
    {
        var hotkeys = new Mock<IGlobalHotkeyService>();
        var viewModel = CreateViewModel(hotkeys);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            viewModel.Settings.Hotkeys.BeginCapture((HotkeyAction)999));
        hotkeys.Verify(service => service.Configure(It.IsAny<IReadOnlyList<HotkeyBinding>>()), Times.Never);
        await viewModel.DisposeAsync();
    }

    private static MainViewModel CreateViewModel(Mock<IGlobalHotkeyService> hotkeys)
    {
        var devices = new Mock<IDeviceService>();
        devices.Setup(service => service.GetOutputDevices()).Returns([]);
        devices.Setup(service => service.GetInputDevices()).Returns([]);
        var router = Mock.Of<IAudioRouterService>();
        var settings = Mock.Of<ISettingsService>();
        var texts = new TextsViewModel();
        var applications = new ApplicationsViewModel(Mock.Of<IApplicationAudioService>(), router, settings,
            NullLogger<ApplicationsViewModel>.Instance, texts);
        var microphone = new MicrophoneViewModel(router, Mock.Of<IMicrophoneService>(), devices.Object, settings,
            NullLogger<MicrophoneViewModel>.Instance);
        var noiseGate = new NoiseGateViewModel(router, settings, NullLogger<NoiseGateViewModel>.Instance);
        var audio = new AudioRoutingViewModel(router, devices.Object, settings, applications, microphone, noiseGate,
            NullLogger<AudioRoutingViewModel>.Instance);
        var osc = new OscChatboxViewModel(Mock.Of<IOscChatboxWorker>(), settings,
            NullLogger<OscChatboxViewModel>.Instance);
        var updates = new UpdateViewModel(Mock.Of<IUpdateService>(), settings,
            Mock.Of<IExternalUrlLauncher>(), texts, NullLogger<UpdateViewModel>.Instance);
        var hotkeysViewModel = new HotkeysViewModel(hotkeys.Object, settings, texts);
        var language = new LanguageViewModel(IterVC.Core.Localization.LocalizationService.Instance, settings,
            texts, hotkeysViewModel, applications, updates, NullLogger<LanguageViewModel>.Instance);
        var settingsViewModel = new SettingsViewModel(language, hotkeysViewModel, updates);
        return new MainViewModel(devices.Object, audio, settings, NullLogger<MainViewModel>.Instance,
            osc, settingsViewModel);
    }
}

using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Core.Settings;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class MicrophoneViewModelTests
{
    [TestMethod]
    public async Task HydrateAsync_LegacyDisabledDeviceMigratesWithoutStartingCapture()
    {
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var settings = CreateSettings();
        var viewModel = CreateViewModel(router, microphone, settings);

        await viewModel.HydrateAsync(new AppSettings
        {
            MicrophoneDeviceId = "none",
            MicrophoneEnabled = true,
            MonitorMicrophone = true,
            MicrophoneBoost = 1.5f
        });

        Assert.IsFalse(viewModel.IsEnabled);
        Assert.AreEqual("mic", viewModel.SelectedDevice?.Id);
        microphone.Verify(service => service.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        router.Verify(service => service.SetMonitorMicrophone(true), Times.Once);
        router.Verify(service => service.SetMicrophoneBoost(1.5f), Times.Once);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task ToggleAsync_SerializesRapidCaptureTransitions()
    {
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var settings = CreateSettings();
        var viewModel = CreateViewModel(router, microphone, settings);
        await viewModel.HydrateAsync(new AppSettings { MicrophoneDeviceId = "mic", MicrophoneEnabled = true });

        var disable = viewModel.ToggleAsync();
        var enable = viewModel.ToggleAsync();
        await Task.WhenAll(disable, enable);

        microphone.Verify(service => service.StopAsync(), Times.Once);
        microphone.Verify(service => service.StartAsync("mic", It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.IsTrue(viewModel.IsEnabled);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task DataAvailable_FeedsOnlyWhileEnabledAndSubscribed()
    {
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var viewModel = CreateViewModel(router, microphone, CreateSettings());
        await viewModel.HydrateAsync(new AppSettings { MicrophoneEnabled = false });
        var samples = new byte[8];

        microphone.Raise(service => service.DataAvailable += null, microphone.Object, samples);
        await viewModel.ToggleAsync();
        microphone.Raise(service => service.DataAvailable += null, microphone.Object, samples);
        await viewModel.StopAsync();
        microphone.Raise(service => service.DataAvailable += null, microphone.Object, samples);

        router.Verify(service => service.FeedMicrophoneSamples(samples, samples.Length), Times.Once);
    }

    private static MicrophoneViewModel CreateViewModel(Mock<IAudioRouterService> router,
        Mock<IMicrophoneService> microphone, Mock<ISettingsService> settings)
    {
        var devices = new Mock<IDeviceService>();
        devices.Setup(service => service.GetInputDevices()).Returns([
            new AudioDeviceInfo { Id = "mic", Name = "Microphone", Kind = AudioDeviceKind.Input, IsDefault = true }
        ]);
        return new MicrophoneViewModel(router.Object, microphone.Object, devices.Object, settings.Object,
            NullLogger<MicrophoneViewModel>.Instance);
    }

    private static Mock<ISettingsService> CreateSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return settings;
    }
}

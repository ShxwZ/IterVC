using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Core.Settings;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class AudioRoutingViewModelTests
{
    [TestMethod]
    public async Task HydrateAsync_AppliesPersistedDevicesVolumeAndStartsRouting()
    {
        var router = new Mock<IAudioRouterService>();
        var devices = CreateDevices();
        var applications = new Mock<IApplicationAudioService>();
        applications.Setup(service => service.GetRunningAudioApps()).Returns([]);
        var viewModel = CreateViewModel(router, devices, applications);

        await viewModel.HydrateAsync(new AppSettings
        {
            OutputDeviceId = "speakers",
            VbCableDeviceId = "cable",
            AppsVolume = 1.5f
        });

        Assert.AreEqual("speakers", viewModel.SelectedOutputDevice?.Id);
        Assert.AreEqual("cable", viewModel.SelectedVbCableDevice?.Id);
        Assert.IsTrue(viewModel.IsRouting);
        router.Verify(service => service.SetAppsVolume(1.5f), Times.Once);
        router.Verify(service => service.StartAsync("cable", It.IsAny<CancellationToken>()), Times.Once);
        applications.Verify(service => service.UseDevice("speakers"), Times.Once);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task RoutingCommands_AreIdempotentAndReflectRouterState()
    {
        var router = new Mock<IAudioRouterService>();
        var viewModel = CreateViewModel(router, CreateDevices(), new Mock<IApplicationAudioService>());
        viewModel.RefreshDevices();
        viewModel.SelectedVbCableDevice = viewModel.VbCableDevices.Single(device => device.Id == "cable");

        await viewModel.StartRoutingAsync();
        await viewModel.StartRoutingAsync();
        await viewModel.StopRoutingAsync();
        await viewModel.StopRoutingAsync();

        router.Verify(service => service.StartAsync("cable", It.IsAny<CancellationToken>()), Times.Once);
        router.Verify(service => service.StopAsync(), Times.Once);
        Assert.IsFalse(viewModel.IsRouting);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public void RefreshDevices_EnumeratesOutputsOnceAndPreservesSelections()
    {
        var router = new Mock<IAudioRouterService>();
        var devices = CreateDevices();
        var viewModel = CreateViewModel(router, devices, new Mock<IApplicationAudioService>());
        viewModel.RefreshDevices();
        viewModel.SelectedOutputDevice = viewModel.OutputDevices.Single(device => device.Id == "speakers");
        viewModel.SelectedVbCableDevice = viewModel.VbCableDevices.Single(device => device.Id == "cable");

        viewModel.RefreshDevices();

        Assert.AreEqual("speakers", viewModel.SelectedOutputDevice?.Id);
        Assert.AreEqual("cable", viewModel.SelectedVbCableDevice?.Id);
        devices.Verify(service => service.GetOutputDevices(), Times.Exactly(2));
    }

    private static AudioRoutingViewModel CreateViewModel(Mock<IAudioRouterService> router,
        Mock<IDeviceService> devices, Mock<IApplicationAudioService> applicationService)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var applications = new ApplicationsViewModel(applicationService.Object, router.Object, settings.Object,
            NullLogger<ApplicationsViewModel>.Instance, new TextsViewModel());
        var microphone = new MicrophoneViewModel(router.Object, Mock.Of<IMicrophoneService>(), devices.Object,
            settings.Object, NullLogger<MicrophoneViewModel>.Instance);
        var noiseGate = new NoiseGateViewModel(router.Object, settings.Object,
            NullLogger<NoiseGateViewModel>.Instance);
        return new AudioRoutingViewModel(router.Object, devices.Object, settings.Object, applications, microphone,
            noiseGate,
            NullLogger<AudioRoutingViewModel>.Instance);
    }

    private static Mock<IDeviceService> CreateDevices()
    {
        var outputs = new[]
        {
            new AudioDeviceInfo { Id = "speakers", Name = "Speakers", Kind = AudioDeviceKind.Output, IsDefault = true },
            new AudioDeviceInfo { Id = "cable", Name = "VB-Cable", Kind = AudioDeviceKind.Output }
        };
        var devices = new Mock<IDeviceService>();
        devices.Setup(service => service.GetOutputDevices()).Returns(outputs);
        devices.Setup(service => service.FindVbCableDevice()).Returns(outputs[1]);
        return devices;
    }
}

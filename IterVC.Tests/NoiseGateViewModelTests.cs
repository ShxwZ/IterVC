using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class NoiseGateViewModelTests
{
    [TestMethod]
    public async Task Hydrate_AppliesAllSettingsWithoutPersisting()
    {
        var router = new Mock<IAudioRouterService>();
        var settings = CreateSettings();
        var viewModel = CreateViewModel(router, settings);

        viewModel.Hydrate(new AppSettings
        {
            NoiseGateEnabled = true,
            NoiseGateThresholdDb = -35f,
            NoiseGateAttackMilliseconds = 20f,
            NoiseGateReleaseMilliseconds = 250f
        });

        router.Verify(service => service.ConfigureNoiseGate(true, -35f, 20f, 250f), Times.Once);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task SettingChange_AppliesAndPersistsCoherentSnapshot()
    {
        var router = new Mock<IAudioRouterService>();
        var persisted = new AppSettings();
        var settings = CreateSettings();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Action<AppSettings>, CancellationToken>((mutation, _) => mutation(persisted))
            .Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(router, settings);
        viewModel.Hydrate(new AppSettings());

        viewModel.IsEnabled = true;
        viewModel.ThresholdDb = -30f;
        viewModel.AttackMilliseconds = 25f;
        viewModel.ReleaseMilliseconds = 300f;
        await viewModel.StopAsync();

        Assert.IsTrue(persisted.NoiseGateEnabled);
        Assert.AreEqual(-30f, persisted.NoiseGateThresholdDb);
        Assert.AreEqual(25f, persisted.NoiseGateAttackMilliseconds);
        Assert.AreEqual(300f, persisted.NoiseGateReleaseMilliseconds);
        router.Verify(service => service.ConfigureNoiseGate(true, -30f, 25f, 300f), Times.Once);
    }

    [TestMethod]
    public async Task Calibrate_UsesHighestAmbientLevelAddsSixDbAndClamps()
    {
        var router = new Mock<IAudioRouterService>();
        router.SetupGet(service => service.MicrophoneInputLevelDb).Returns(-12f);
        var viewModel = new NoiseGateViewModel(router.Object, CreateSettings().Object,
            NullLogger<NoiseGateViewModel>.Instance, (_, _) => Task.CompletedTask);

        await viewModel.CalibrateCommand.ExecuteAsync(null);

        Assert.AreEqual(-10f, viewModel.ThresholdDb);
        Assert.IsFalse(viewModel.IsCalibrating);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task UpdateMeter_PreservesSmoothingClampAndOpenState()
    {
        var router = new Mock<IAudioRouterService>();
        router.SetupGet(service => service.MicrophoneOutputLevelDb).Returns(20f);
        router.SetupGet(service => service.IsNoiseGateOpen).Returns(true);
        var viewModel = CreateViewModel(router, CreateSettings());

        viewModel.UpdateMeter();

        Assert.AreEqual(-25f, viewModel.OutputLevelDb, 0.001f);
        Assert.IsTrue(viewModel.IsOpen);
        await viewModel.StopAsync();
    }

    private static NoiseGateViewModel CreateViewModel(Mock<IAudioRouterService> router,
        Mock<ISettingsService> settings) => new(router.Object, settings.Object,
        NullLogger<NoiseGateViewModel>.Instance);

    private static Mock<ISettingsService> CreateSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return settings;
    }
}

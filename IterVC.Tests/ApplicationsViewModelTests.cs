using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Core.Settings;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class ApplicationsViewModelTests
{
    [TestMethod]
    public async Task RefreshAsync_RestoresIncludedAppWithoutAddingDuplicateSources()
    {
        var applicationService = CreateApplicationService();
        var router = new Mock<IAudioRouterService>();
        var settings = CreateSettingsService();
        var viewModel = CreateViewModel(applicationService, router, settings);
        viewModel.HydrateIncludedProcessNames(["player"]);

        await viewModel.InitializeOutputDeviceAsync("device");
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.RunningApps.Single().IsIncludedInMix);
        router.Verify(service => service.AddAppSourceAsync(42, true), Times.Once);
    }

    [TestMethod]
    public async Task ToggleInclusion_WhenCaptureFails_PreservesSelectionAndSkipsPersistence()
    {
        var applicationService = CreateApplicationService();
        var router = new Mock<IAudioRouterService>();
        router.Setup(service => service.AddAppSourceAsync(42, true)).ThrowsAsync(new InvalidOperationException("capture"));
        var settings = CreateSettingsService();
        var viewModel = CreateViewModel(applicationService, router, settings);
        await viewModel.InitializeOutputDeviceAsync("device");
        var app = viewModel.RunningApps.Single();

        await app.ToggleInclusionCommand.ExecuteAsync(null);

        Assert.IsFalse(app.IsIncludedInMix);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(viewModel.StatusMessage, "42");
    }

    [TestMethod]
    public async Task ToggleInclusion_RemoveCompletesBeforePersistingResult()
    {
        var applicationService = CreateApplicationService();
        var router = new Mock<IAudioRouterService>();
        var settings = CreateSettingsService();
        var viewModel = CreateViewModel(applicationService, router, settings);
        viewModel.HydrateIncludedProcessNames(["player"]);
        await viewModel.InitializeOutputDeviceAsync("device");
        var app = viewModel.RunningApps.Single();

        await app.ToggleInclusionCommand.ExecuteAsync(null);

        Assert.IsFalse(app.IsIncludedInMix);
        router.Verify(service => service.RemoveAppSourceAsync(42), Times.Once);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IApplicationAudioService> CreateApplicationService()
    {
        var service = new Mock<IApplicationAudioService>();
        service.Setup(x => x.GetRunningAudioApps()).Returns([new AudioAppInfo
        {
            ProcessId = 42, ProcessName = "player", DisplayName = "Player"
        }]);
        return service;
    }

    private static Mock<ISettingsService> CreateSettingsService()
    {
        var service = new Mock<ISettingsService>();
        service.Setup(x => x.UpdateAsync(It.IsAny<Action<AppSettings>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return service;
    }

    private static ApplicationsViewModel CreateViewModel(Mock<IApplicationAudioService> applications,
        Mock<IAudioRouterService> router, Mock<ISettingsService> settings) =>
        new(applications.Object, router.Object, settings.Object, NullLogger<ApplicationsViewModel>.Instance,
            new TextsViewModel());
}

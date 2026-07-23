using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class UpdateViewModelTests
{
    [TestMethod]
    public async Task ConsentCommands_PersistExactlyOnce()
    {
        var updates = new Mock<IUpdateService>();
        updates.Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(UpdateCheckResult.Failed);
        var settings = CreateSettings(new AppSettings());
        var viewModel = CreateViewModel(updates, settings);
        await viewModel.HydrateAsync(new AppSettings { CheckForUpdates = null });

        await viewModel.AcceptCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsConsentVisible);
        Assert.IsTrue(viewModel.IsEnabled);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task ManualFailure_AlwaysClearsCheckingStateAndShowsFailure()
    {
        var updates = new Mock<IUpdateService>();
        updates.Setup(service => service.CheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(UpdateCheckResult.Failed);
        var viewModel = CreateViewModel(updates, CreateSettings(new AppSettings()));

        await viewModel.CheckNowCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsChecking);
        Assert.AreEqual(new TextsViewModel().UpdateCheckFailed, viewModel.Status);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task FreshCache_SkipsNetworkCheck()
    {
        var current = new AppSettings
        {
            CheckForUpdates = true,
            LastSuccessfulUpdateCheckUtc = DateTimeOffset.UtcNow,
            CachedLatestVersion = "99.0.0",
            CachedReleaseUrl = "https://github.com/ShxwZ/IterVC/releases/tag/v99.0.0"
        };
        var updates = new Mock<IUpdateService>();
        updates.Setup(service => service.TryValidateReleaseUrl(current.CachedReleaseUrl, out It.Ref<string>.IsAny))
            .Returns((string? value, out string url) => { url = value!; return true; });
        var viewModel = CreateViewModel(updates, CreateSettings(current));

        await viewModel.HydrateAsync(current);

        updates.Verify(service => service.CheckAsync(It.IsAny<CancellationToken>()), Times.Never);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task OpenPage_UsesOnlyValidatedUrl()
    {
        var updates = new Mock<IUpdateService>();
        const string url = "https://github.com/ShxwZ/IterVC/releases/tag/v2";
        updates.Setup(service => service.TryValidateReleaseUrl(url, out It.Ref<string>.IsAny))
            .Returns((string? value, out string validated) => { validated = value!; return true; });
        var launcher = new Mock<IExternalUrlLauncher>();
        var viewModel = CreateViewModel(updates, CreateSettings(new AppSettings()), launcher);
        viewModel.AvailableUrl = url;

        viewModel.OpenPageCommand.Execute(null);

        launcher.Verify(service => service.Open(url), Times.Once);
        await viewModel.StopAsync();
    }

    private static UpdateViewModel CreateViewModel(Mock<IUpdateService> updates,
        Mock<ISettingsService> settings, Mock<IExternalUrlLauncher>? launcher = null) =>
        new(updates.Object, settings.Object, (launcher ?? new Mock<IExternalUrlLauncher>()).Object,
            new TextsViewModel(), NullLogger<UpdateViewModel>.Instance);

    private static Mock<ISettingsService> CreateSettings(AppSettings current)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(current);
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return settings;
    }
}

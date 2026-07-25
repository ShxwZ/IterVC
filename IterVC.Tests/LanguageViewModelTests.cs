using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class LanguageViewModelTests
{
    [TestMethod]
    public async Task Hydrate_AppliesLanguageAndRefreshesTextsWithoutPersisting()
    {
        var original = LocalizationService.Instance.CurrentLanguage;
        var settings = CreateSettings();
        var viewModel = CreateViewModel(settings);
        try
        {
            viewModel.Hydrate(new AppSettings { Language = SupportedLanguages.English });

            Assert.AreEqual(SupportedLanguages.English, viewModel.SelectedLanguage);
            Assert.AreEqual(LocalizationService.Instance.Get(LocalizationService.Keys.AppTitle),
                viewModel.Texts.AppTitle);
            settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await viewModel.StopAsync();
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    [TestMethod]
    public async Task SelectionChange_PersistsLatestLanguage()
    {
        var original = LocalizationService.Instance.CurrentLanguage;
        var persisted = new AppSettings();
        var settings = CreateSettings();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Action<AppSettings>, CancellationToken>((mutation, _) => mutation(persisted))
            .Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(settings);
        try
        {
            viewModel.Hydrate(new AppSettings { Language = SupportedLanguages.Spanish });
            viewModel.SelectedLanguage = SupportedLanguages.English;
            await viewModel.StopAsync();

            Assert.AreEqual(SupportedLanguages.English, persisted.Language);
        }
        finally
        {
            await viewModel.StopAsync();
            LocalizationService.Instance.SetLanguage(original);
        }
    }

    private static LanguageViewModel CreateViewModel(Mock<ISettingsService> settings)
    {
        var texts = new TextsViewModel();
        var router = Mock.Of<IAudioRouterService>();
        var applicationService = new Mock<IApplicationAudioService>();
        applicationService.Setup(service => service.GetRunningAudioApps()).Returns([]);
        var applications = new ApplicationsViewModel(applicationService.Object, router, settings.Object,
            NullLogger<ApplicationsViewModel>.Instance, texts);
        var hotkeys = new HotkeysViewModel(Mock.Of<IGlobalHotkeyService>(), settings.Object, texts);
        var updates = new UpdateViewModel(Mock.Of<IUpdateService>(), settings.Object,
            Mock.Of<IExternalUrlLauncher>(), texts, NullLogger<UpdateViewModel>.Instance);
        return new LanguageViewModel(LocalizationService.Instance, settings.Object, texts, hotkeys,
            applications, updates, NullLogger<LanguageViewModel>.Instance);
    }

    private static Mock<ISettingsService> CreateSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return settings;
    }
}

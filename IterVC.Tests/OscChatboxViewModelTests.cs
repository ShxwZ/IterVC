using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class OscChatboxViewModelTests
{
    [TestMethod]
    public async Task HydrateAsync_ConfiguresAndStartsWithoutPersisting()
    {
        var worker = new Mock<IOscChatboxWorker>();
        var settings = CreateSettings();
        var viewModel = CreateViewModel(worker, settings);

        await viewModel.HydrateAsync(new AppSettings
        {
            EnableOscChatbox = true,
            OscTemplate = "{title} / {time}"
        });

        worker.Verify(service => service.Configure(true, "{title} / {time}"), Times.Once);
        worker.Verify(service => service.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        settings.Verify(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await viewModel.StopAsync();
    }

    [TestMethod]
    public async Task Changes_ReconfigureImmediatelyAndPersistInOrder()
    {
        var worker = new Mock<IOscChatboxWorker>();
        var persisted = new AppSettings();
        var settings = CreateSettings();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Action<AppSettings>, CancellationToken>((mutation, _) => mutation(persisted))
            .Returns(Task.CompletedTask);
        var viewModel = CreateViewModel(worker, settings);
        await viewModel.HydrateAsync(new AppSettings());

        viewModel.Template = "{status}";
        viewModel.IsEnabled = true;
        await viewModel.StopAsync();

        Assert.AreEqual("{status}", persisted.OscTemplate);
        Assert.IsTrue(persisted.EnableOscChatbox);
        worker.Verify(service => service.Configure(false, "{status}"), Times.Once);
        worker.Verify(service => service.Configure(true, "{status}"), Times.Once);
    }

    [TestMethod]
    public async Task StopAsync_IsIdempotent()
    {
        var worker = new Mock<IOscChatboxWorker>();
        var viewModel = CreateViewModel(worker, CreateSettings());

        await viewModel.StopAsync();
        await viewModel.StopAsync();

        worker.Verify(service => service.StopAsync(), Times.Once);
    }

    private static OscChatboxViewModel CreateViewModel(Mock<IOscChatboxWorker> worker,
        Mock<ISettingsService> settings) => new(worker.Object, settings.Object,
        NullLogger<OscChatboxViewModel>.Instance);

    private static Mock<ISettingsService> CreateSettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.UpdateAsync(It.IsAny<Action<AppSettings>>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return settings;
    }
}

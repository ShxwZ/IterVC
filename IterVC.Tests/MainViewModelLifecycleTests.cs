using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IterVC.Tests;

[TestClass]
public sealed class MainViewModelLifecycleTests
{
    [TestMethod]
    public async Task InitializeAsync_ConcurrentCallsShareOneInitialization()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings { MicrophoneEnabled = false });
        var worker = new Mock<IOscChatboxWorker>();
        var viewModel = CreateViewModel(new Mock<IAudioRouterService>(), new Mock<IMicrophoneService>(), worker, settings);

        var first = viewModel.InitializeAsync();
        var second = viewModel.InitializeAsync();
        await CompleteWithDispatcherAsync(Task.WhenAll(first, second));
        await viewModel.DisposeAsync();

        Assert.AreSame(first, second);
        settings.Verify(service => service.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
        worker.Verify(service => service.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task InitializeAsync_WhenWorkerFails_CleansStartedResources()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppSettings { MicrophoneEnabled = false });
        var worker = new Mock<IOscChatboxWorker>();
        worker.Setup(service => service.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("worker failed"));
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var viewModel = CreateViewModel(router, microphone, worker, settings);

        var initialization = viewModel.InitializeAsync();
        await CompleteWithDispatcherAsync(initialization, expectFailure: true);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => initialization);
        await viewModel.DisposeAsync();

        worker.Verify(service => service.StopAsync(), Times.Once);
        microphone.Verify(service => service.StopAsync(), Times.Once);
        router.Verify(service => service.StopAsync(), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_UnsubscribesMicrophoneAndStopsResourcesOnce()
    {
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var worker = new Mock<IOscChatboxWorker>();
        var viewModel = CreateViewModel(router, microphone, worker);
        var samples = new byte[16];
        await viewModel.Audio.Microphone.HydrateAsync(new AppSettings());

        microphone.Raise(service => service.DataAvailable += null, microphone.Object, samples);
        await viewModel.DisposeAsync();
        await viewModel.DisposeAsync();
        microphone.Raise(service => service.DataAvailable += null, microphone.Object, samples);

        router.Verify(service => service.FeedMicrophoneSamples(samples, samples.Length), Times.Once);
        worker.Verify(service => service.StopAsync(), Times.Once);
        microphone.Verify(service => service.StopAsync(), Times.Once);
        router.Verify(service => service.StopAsync(), Times.Once);
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsActiveNoiseGateCalibration()
    {
        var router = new Mock<IAudioRouterService>();
        var microphone = new Mock<IMicrophoneService>();
        var worker = new Mock<IOscChatboxWorker>();
        var viewModel = CreateViewModel(router, microphone, worker);
        var calibration = viewModel.Audio.NoiseGate.CalibrateCommand.ExecuteAsync(null);
        await Task.Delay(20);

        await viewModel.DisposeAsync();
        await calibration;

        Assert.IsFalse(viewModel.Audio.NoiseGate.IsCalibrating);
        Assert.AreEqual(-45f, viewModel.Audio.NoiseGate.ThresholdDb);
    }

    private static MainViewModel CreateViewModel(
        Mock<IAudioRouterService> router,
        Mock<IMicrophoneService> microphone,
        Mock<IOscChatboxWorker> worker,
        Mock<ISettingsService>? settings = null)
    {
        var devices = new Mock<IDeviceService>();
        devices.Setup(service => service.GetOutputDevices()).Returns([]);
        devices.Setup(service => service.GetInputDevices()).Returns([]);
        var resolvedSettings = (settings ?? new Mock<ISettingsService>()).Object;
        var texts = new TextsViewModel();
        var applications = new ApplicationsViewModel(Mock.Of<IApplicationAudioService>(), router.Object,
            resolvedSettings, NullLogger<ApplicationsViewModel>.Instance, texts);
        var microphoneViewModel = new MicrophoneViewModel(router.Object, microphone.Object, devices.Object,
            resolvedSettings, NullLogger<MicrophoneViewModel>.Instance);
        var noiseGate = new NoiseGateViewModel(router.Object, resolvedSettings,
            NullLogger<NoiseGateViewModel>.Instance);
        var audio = new AudioRoutingViewModel(router.Object, devices.Object, resolvedSettings, applications,
            microphoneViewModel, noiseGate,
            NullLogger<AudioRoutingViewModel>.Instance);
        var osc = new OscChatboxViewModel(worker.Object, resolvedSettings,
            NullLogger<OscChatboxViewModel>.Instance);
        var updates = new UpdateViewModel(Mock.Of<IUpdateService>(), resolvedSettings,
            Mock.Of<IExternalUrlLauncher>(), texts, NullLogger<UpdateViewModel>.Instance);
        var hotkeys = new HotkeysViewModel(Mock.Of<IGlobalHotkeyService>(), resolvedSettings, texts);
        var language = new LanguageViewModel(IterVC.Core.Localization.LocalizationService.Instance,
            resolvedSettings, texts, hotkeys, applications, updates, NullLogger<LanguageViewModel>.Instance);
        var settingsViewModel = new SettingsViewModel(language, hotkeys, updates);
        return new MainViewModel(devices.Object, audio, resolvedSettings,
            NullLogger<MainViewModel>.Instance, osc, settingsViewModel);
    }

    private static async Task CompleteWithDispatcherAsync(Task task, bool expectFailure = false)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(1);
        }
        Assert.IsTrue(task.IsCompleted, "Initialization did not complete before the timeout.");
        if (!expectFailure) await task;
    }
}

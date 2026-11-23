using Microsoft.Extensions.Logging;
using IterVC.Core.Interfaces;

namespace IterVC.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IDeviceService _deviceService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    public TextsViewModel Texts => Settings.Language.Texts;
    public AudioRoutingViewModel Audio { get; }
    public OscChatboxViewModel OscChatbox { get; }
    public SettingsViewModel Settings { get; }
    public ApplicationsViewModel Applications => Audio.Applications;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    private bool _deviceEventsSubscribed;
    private int _resourceCleanupStarted;
    private int _disposed;

    public MainViewModel(
        IDeviceService deviceService,
        AudioRoutingViewModel audio,
        ISettingsService settingsService,
        ILogger<MainViewModel> logger,
        OscChatboxViewModel oscChatbox,
        SettingsViewModel settings)
    {
        _deviceService = deviceService;
        _settingsService = settingsService;
        _logger = logger;
        Audio = audio;
        OscChatbox = oscChatbox;
        Settings = settings;

    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void OnDevicesChanged(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshDevicesAsync());

    public Task InitializeAsync()
    {
        lock (_initializationLock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _initializationTask ??= InitializeCoreAsync(_lifetimeCancellation.Token);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            _deviceService.DevicesChanged += OnDevicesChanged;
            _deviceEventsSubscribed = true;

            Settings.Language.Hydrate(settings);
            Settings.Hotkeys.Hydrate(settings);
            Settings.Tray?.Hydrate(settings);
            Settings.Startup?.Hydrate(settings);

            await Audio.HydrateAsync(settings, cancellationToken);
            await Audio.Microphone.HydrateAsync(settings, cancellationToken);
            Audio.NoiseGate.Hydrate(settings);
            await OscChatbox.HydrateAsync(settings, cancellationToken);
            Settings.Updates.HydrateForStartup(settings);

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException || !IsDisposed)
                _logger.LogError(ex, "Error durante la inicializaci\u00f3n");
            await CleanupStartedResourcesAsync();
            throw;
        }
    }

    private async Task RefreshDevicesAsync()
    {
        if (IsDisposed || Avalonia.Application.Current == null) return;

        try
        {
            Audio.RefreshDevices();
            Audio.Microphone.RefreshDevices();
            // Device enumeration alone is insufficient after a spatial-audio format
            // change: existing process-loopback clients retain their original format.
            await Applications.RestartCapturedSourcesAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not recover audio captures after a device change");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetimeCancellation.Cancel();
        await CleanupStartedResourcesAsync();
        _lifetimeCancellation.Dispose();
    }

    private async Task CleanupStartedResourcesAsync()
    {
        if (Interlocked.Exchange(ref _resourceCleanupStarted, 1) != 0) return;
        if (_deviceEventsSubscribed)
        {
            _deviceService.DevicesChanged -= OnDevicesChanged;
            _deviceEventsSubscribed = false;
        }
        await StopSafelyAsync(OscChatbox.StopAsync, "OSC chatbox worker");
        await StopSafelyAsync(Settings.StopAsync, "settings");
        await StopSafelyAsync(Audio.NoiseGate.StopAsync, "noise gate");
        await StopSafelyAsync(Audio.Microphone.StopAsync, "microphone capture");
        await StopSafelyAsync(Audio.StopAsync, "audio routing");
    }

    private async Task StopSafelyAsync(Func<Task> stop, string component)
    {
        try { await stop(); }
        catch (Exception exception) { _logger.LogError(exception, "Failed to stop {Component}", component); }
    }

}

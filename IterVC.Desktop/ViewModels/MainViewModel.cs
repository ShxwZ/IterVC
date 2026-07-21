using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Media.Control;
using Avalonia.Threading;

namespace IterVC.Desktop.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IAudioRouterService _audioRouter;
    private readonly IMicrophoneService _microphoneService;
    private readonly IDeviceService _deviceService;
    private readonly IApplicationAudioService _applicationAudioService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IOscMediaService _oscMediaService;

    public ObservableCollection<AppAudioItemViewModel> RunningApps { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> VbCableDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedVbCableDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophoneDevice;
    [ObservableProperty] private bool _microphoneEnabled = true;
    [ObservableProperty] private bool _monitorMicrophone;
    [ObservableProperty] private bool _isRouting;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private float _appsVolume = 1.0f;

    [ObservableProperty] private bool _enableOscChatbox;
    [ObservableProperty] private string _oscTemplate = "{title} - [{time}]";

    [ObservableProperty] private float _microphoneBoost = 1.0f;
    [ObservableProperty] private bool _noiseGateEnabled;
    [ObservableProperty] private float _noiseGateThresholdDb = -45f;
    [ObservableProperty] private float _noiseGateAttackMilliseconds = 10f;
    [ObservableProperty] private float _noiseGateReleaseMilliseconds = 150f;
    [ObservableProperty] private float _noiseGateOutputLevelDb = -80f;
    [ObservableProperty] private bool _isNoiseGateOpen;
    [ObservableProperty] private bool _isCalibratingNoiseGate;

    [ObservableProperty] private string _selectedLanguage = LocalizationService.Instance.CurrentLanguage;

    public IReadOnlyList<string> AvailableLanguages { get; } = SupportedLanguages.All;
    public TextsViewModel Texts { get; } = new();

    private bool _isRefreshing;
    private bool _initializing;
    private bool _statusShowsDetectedApps;
    private List<string>? _pendingIncludedProcessNames;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private readonly DispatcherTimer _noiseGateMeterTimer;
    private float _smoothedMicrophoneLevelDb = -80f;

    public MainViewModel(
        IAudioRouterService audioRouter,
        IMicrophoneService microphoneService,
        IDeviceService deviceService,
        IApplicationAudioService applicationAudioService,
        ISettingsService settingsService,
        IOscMediaService oscMediaService,
        ILogger<MainViewModel> logger)
    {
        _audioRouter = audioRouter;
        _microphoneService = microphoneService;
        _deviceService = deviceService;
        _applicationAudioService = applicationAudioService;
        _settingsService = settingsService;
        _oscMediaService = oscMediaService;
        _logger = logger;

        _microphoneService.DataAvailable += (_, data) =>
        {
            if (!MicrophoneEnabled) return;
            if (_audioRouter is AudioRouterService concreteRouter)
                concreteRouter.FeedMicrophoneSamples(data, data.Length);
        };

        _deviceService.DevicesChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => RefreshDevices());

        var thread = new Thread(() =>
        {
            OscLoop(CancellationToken.None).GetAwaiter().GetResult();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        LocalizationService.Instance.Changed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Texts.RaiseAll());

        _noiseGateMeterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _noiseGateMeterTimer.Tick += (_, _) =>
        {
            var targetLevelDb = _audioRouter.MicrophoneOutputLevelDb;
            var smoothing = targetLevelDb > _smoothedMicrophoneLevelDb ? 0.55f : 0.18f;
            _smoothedMicrophoneLevelDb += (targetLevelDb - _smoothedMicrophoneLevelDb) * smoothing;
            NoiseGateOutputLevelDb = Math.Clamp(_smoothedMicrophoneLevelDb, -80f, 0f);
            IsNoiseGateOpen = _audioRouter.IsNoiseGateOpen;
        };
        _noiseGateMeterTimer.Start();
    }

    public async Task InitializeAsync()
    {
        _initializing = true;
        try
        {
            var settings = await _settingsService.LoadAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshDevices);

            var usedLegacyDisabledMicrophone = settings.MicrophoneDeviceId == "none";
            MicrophoneEnabled = settings.MicrophoneEnabled && !usedLegacyDisabledMicrophone;
            MonitorMicrophone = settings.MonitorMicrophone;
            _pendingIncludedProcessNames = settings.IncludedProcessNames;
            AppsVolume = settings.AppsVolume;

            LocalizationService.Instance.SetLanguage(
                string.IsNullOrEmpty(settings.Language) ? SupportedLanguages.Spanish : settings.Language);
            SelectedLanguage = LocalizationService.Instance.CurrentLanguage;
            Texts.RaiseAll();

            OscTemplate = settings.OscTemplate;
            EnableOscChatbox = settings.EnableOscChatbox;

            MicrophoneBoost = settings.MicrophoneBoost;
            _audioRouter.SetMicrophoneBoost(MicrophoneBoost);

            NoiseGateEnabled = settings.NoiseGateEnabled;
            NoiseGateThresholdDb = settings.NoiseGateThresholdDb;
            NoiseGateAttackMilliseconds = settings.NoiseGateAttackMilliseconds;
            NoiseGateReleaseMilliseconds = settings.NoiseGateReleaseMilliseconds;
            ApplyNoiseGateSettings();

            _audioRouter.SetAppsVolume(AppsVolume);
            _audioRouter.SetMicrophoneVolume(settings.MicrophoneVolume);
            _audioRouter.SetMonitorMicrophone(settings.MonitorMicrophone);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedVbCableDevice = VbCableDevices.FirstOrDefault(d => d.Id == settings.VbCableDeviceId)
                    ?? _deviceService.FindVbCableDevice();
                SelectedMicrophoneDevice = InputDevices.FirstOrDefault(d => d.Id == settings.MicrophoneDeviceId)
                    ?? InputDevices.FirstOrDefault(d => d.IsDefault);
            });

            if (SelectedVbCableDevice is not null)
            {
                await _audioRouter.StartAsync(SelectedVbCableDevice.Id);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => IsRouting = true);
            }

            if (MicrophoneEnabled && SelectedMicrophoneDevice is not null)
                await _microphoneService.StartAsync(SelectedMicrophoneDevice.Id);

            if (usedLegacyDisabledMicrophone && SelectedMicrophoneDevice is not null)
            {
                await _settingsService.UpdateAsync(s =>
                {
                    s.MicrophoneEnabled = false;
                    s.MicrophoneDeviceId = SelectedMicrophoneDevice.Id;
                });
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == settings.OutputDeviceId)
                    ?? OutputDevices.FirstOrDefault(d => d.IsDefault);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la inicializaci\u00f3n");
            _initializing = false;
            throw;
        }

        _initializing = false;

        if (SelectedOutputDevice is not null)
        {
            _applicationAudioService.UseDevice(SelectedOutputDevice.Id);
            await RefreshRunningAppsAsync();
        }
    }

    private Task PersistIncludedAppsAsync()
    {
        var names = RunningApps.Where(a => a.IsIncludedInMix).Select(a => a.ProcessName).Distinct().ToList();
        return _settingsService.UpdateAsync(s => s.IncludedProcessNames = names);
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_initializing || _isRefreshing || value is null) return;
        _applicationAudioService.UseDevice(value.Id);
        _ = _settingsService.UpdateAsync(s => s.OutputDeviceId = value.Id);
        _ = RefreshRunningAppsAsync();
    }

    partial void OnSelectedVbCableDeviceChanged(AudioDeviceInfo? value)
    {
        if (_initializing || _isRefreshing || value is null) return;
        _ = _audioRouter.SetTargetDeviceAsync(value.Id);
        _ = _settingsService.UpdateAsync(s => s.VbCableDeviceId = value.Id);
    }

    partial void OnSelectedMicrophoneDeviceChanged(AudioDeviceInfo? value)
    {
        if (_initializing || _isRefreshing || value is null) return;
        if (MicrophoneEnabled)
            _ = ChangeMicrophoneDeviceAsync(value.Id);
        _ = _settingsService.UpdateAsync(s => s.MicrophoneDeviceId = value.Id);
    }

    partial void OnMicrophoneEnabledChanged(bool value)
    {
        if (_initializing) return;

        _ = SetMicrophoneCaptureEnabledAsync(value, SelectedMicrophoneDevice?.Id);

        _ = _settingsService.UpdateAsync(s => s.MicrophoneEnabled = value);
    }

    private async Task ChangeMicrophoneDeviceAsync(string deviceId)
    {
        try
        {
            await _microphoneService.SetDeviceAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not switch microphone capture to device '{DeviceId}'", deviceId);
        }
    }

    private async Task SetMicrophoneCaptureEnabledAsync(bool enabled, string? deviceId)
    {
        try
        {
            if (enabled && deviceId is not null)
                await _microphoneService.StartAsync(deviceId);
            else
                await _microphoneService.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not {Action} microphone capture", enabled ? "start" : "stop");
        }
    }

    partial void OnMonitorMicrophoneChanged(bool value)
    {
        if (_initializing) return;
        _audioRouter.SetMonitorMicrophone(value);
        _ = _settingsService.UpdateAsync(s => s.MonitorMicrophone = value);
    }

    partial void OnMicrophoneBoostChanged(float value)
    {
        if (_initializing) return;
        _audioRouter.SetMicrophoneBoost(value);
        _ = _settingsService.UpdateAsync(s => s.MicrophoneBoost = value);
    }

    partial void OnNoiseGateEnabledChanged(bool value) => UpdateNoiseGateSettings();
    partial void OnNoiseGateThresholdDbChanged(float value) => UpdateNoiseGateSettings();
    partial void OnNoiseGateAttackMillisecondsChanged(float value) => UpdateNoiseGateSettings();
    partial void OnNoiseGateReleaseMillisecondsChanged(float value) => UpdateNoiseGateSettings();

    private void UpdateNoiseGateSettings()
    {
        if (_initializing) return;
        ApplyNoiseGateSettings();
        _ = _settingsService.UpdateAsync(settings =>
        {
            settings.NoiseGateEnabled = NoiseGateEnabled;
            settings.NoiseGateThresholdDb = NoiseGateThresholdDb;
            settings.NoiseGateAttackMilliseconds = NoiseGateAttackMilliseconds;
            settings.NoiseGateReleaseMilliseconds = NoiseGateReleaseMilliseconds;
        });
    }

    private void ApplyNoiseGateSettings()
        => _audioRouter.ConfigureNoiseGate(
            NoiseGateEnabled,
            NoiseGateThresholdDb,
            NoiseGateAttackMilliseconds,
            NoiseGateReleaseMilliseconds);

    [RelayCommand]
    private async Task CalibrateNoiseGateAsync()
    {
        if (IsCalibratingNoiseGate) return;

        IsCalibratingNoiseGate = true;
        var highestAmbientLevel = -80f;
        try
        {
            for (var sample = 0; sample < 40; sample++)
            {
                highestAmbientLevel = Math.Max(highestAmbientLevel, _audioRouter.MicrophoneInputLevelDb);
                await Task.Delay(50);
            }

            NoiseGateThresholdDb = Math.Clamp(highestAmbientLevel + 6f, -75f, -10f);
        }
        finally
        {
            IsCalibratingNoiseGate = false;
        }
    }

    partial void OnEnableOscChatboxChanged(bool value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.EnableOscChatbox = value);
    }

    partial void OnOscTemplateChanged(string value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.OscTemplate = value);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        var lang = string.IsNullOrEmpty(value)
            ? LocalizationService.Instance.CurrentLanguage
            : value;
        LocalizationService.Instance.SetLanguage(lang);
        Texts.RaiseAll();
        if (_statusShowsDetectedApps)
            UpdateDetectedAppsStatus();
        if (!_initializing)
            _ = _settingsService.UpdateAsync(s => s.Language = lang);
    }

    partial void OnAppsVolumeChanged(float value)
    {
        if (_initializing) return;
        _audioRouter.SetAppsVolume(value);
        _ = _settingsService.UpdateAsync(s => s.AppsVolume = value);
    }

    private void RefreshDevices()
    {
        if (Avalonia.Application.Current == null) return;
        _isRefreshing = true;

        var currentOutputId = SelectedOutputDevice?.Id;
        var currentVbId = SelectedVbCableDevice?.Id;
        var currentMicId = SelectedMicrophoneDevice?.Id;

        SelectedOutputDevice = null;
        SelectedVbCableDevice = null;
        SelectedMicrophoneDevice = null;

        OutputDevices.Clear();
        foreach (var d in _deviceService.GetOutputDevices()) OutputDevices.Add(d);

        VbCableDevices.Clear();
        foreach (var d in _deviceService.GetOutputDevices()) VbCableDevices.Add(d);

        InputDevices.Clear();
        foreach (var d in _deviceService.GetInputDevices()) InputDevices.Add(d);

        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == currentOutputId)
                               ?? OutputDevices.FirstOrDefault(d => d.IsDefault);
        SelectedVbCableDevice = VbCableDevices.FirstOrDefault(d => d.Id == currentVbId)
                                ?? _deviceService.FindVbCableDevice();
        SelectedMicrophoneDevice = InputDevices.FirstOrDefault(d => d.Id == currentMicId)
                                   ?? InputDevices.FirstOrDefault(d => d.IsDefault);

        _isRefreshing = false;

        if (!_initializing && SelectedMicrophoneDevice is not null &&
            SelectedMicrophoneDevice.Id != currentMicId)
        {
            var selectedMicrophoneId = SelectedMicrophoneDevice.Id;
            _ = _settingsService.UpdateAsync(s => s.MicrophoneDeviceId = selectedMicrophoneId);
            if (MicrophoneEnabled)
                _ = ChangeMicrophoneDeviceAsync(selectedMicrophoneId);
        }
    }

    [RelayCommand]
    private async Task RefreshRunningAppsAsync()
    {
        var overrideNames = _pendingIncludedProcessNames;
        _pendingIncludedProcessNames = null;
        await RefreshRunningAppsAsync(overrideNames);
    }

    private async Task RefreshRunningAppsAsync(IReadOnlyCollection<string>? includedProcessNamesOverride)
    {
        var apps = _applicationAudioService.GetRunningAudioApps();
        var currentlyCapturedIds = RunningApps.Where(a => a.IsIncludedInMix).Select(a => a.ProcessId).ToHashSet();

        RunningApps.Clear();
        var anyCaptureFailed = false;

        foreach (var app in apps)
        {
            var included = includedProcessNamesOverride is not null
                ? includedProcessNamesOverride.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase)
                : currentlyCapturedIds.Contains(app.ProcessId);

            if (included && !await TryAddAppSourceAsync(app.ProcessId))
                anyCaptureFailed = true;

            RunningApps.Add(new AppAudioItemViewModel(
                app with { IsIncludedInMix = included },
                OnAppIncludedChanged));
        }

        if (!anyCaptureFailed)
        {
            _statusShowsDetectedApps = true;
            UpdateDetectedAppsStatus();
        }
    }

    private void UpdateDetectedAppsStatus()
        => StatusMessage = RunningApps.Count == 1
            ? Texts.AppsDetectedOne
            : string.Format(Texts.AppsDetectedMany, RunningApps.Count);

    private void OnAppIncludedChanged(AppAudioItemViewModel app, bool included)
        => _ = OnAppIncludedChangedAsync(app, included);

    private async Task OnAppIncludedChangedAsync(AppAudioItemViewModel app, bool included)
    {
        if (included)
            await TryAddAppSourceAsync(app.ProcessId);
        else
            await _audioRouter.RemoveAppSourceAsync(app.ProcessId);

        await PersistIncludedAppsAsync();
    }

    private async Task<bool> TryAddAppSourceAsync(int processId)
    {
        try
        {
            await _audioRouter.AddAppSourceAsync(processId);
            return true;
        }
        catch (Exception ex)
        {
            var fullError = $"No se pudo capturar el proceso {processId}: {ex.Message}";
            _logger.LogError(ex, "No se pudo capturar el proceso {ProcessId}", processId);
            _statusShowsDetectedApps = false;
            StatusMessage = fullError;
            return false;
        }
    }

    [RelayCommand]
    private async Task ToggleRoutingAsync()
    {
        if (IsRouting)
        {
            await _audioRouter.StopAsync();
            IsRouting = false;
        }
        else if (SelectedVbCableDevice is not null)
        {
            await _audioRouter.StartAsync(SelectedVbCableDevice.Id);
            IsRouting = true;
        }
    }

    private async Task OscLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (EnableOscChatbox)
            {
                var mediaInfo = await GetActiveMediaInfoAsync();
                if (mediaInfo != null && !string.IsNullOrEmpty(mediaInfo.Title))
                {
                    string templateWithTime = OscTemplate
                        .Replace("{title}", mediaInfo.Title)
                        .Replace("{status}", mediaInfo.Status)
                        .Replace("{time}", mediaInfo.TimeInfo);

                    _oscMediaService.SendMediaInfo(mediaInfo.Title, mediaInfo.Status, templateWithTime);
                }
                await Task.Delay(1000, ct);
            }
            else
            {
                await Task.Delay(1000, ct);
            }
        }
    }

    public async Task<MediaInfo?> GetActiveMediaInfoAsync()
    {
        try
        {
            _mediaSessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _mediaSessionManager.GetSessions();
            if (sessions == null || sessions.Count == 0) return null;

            var activeSession = sessions.FirstOrDefault(s =>
                s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
            if (activeSession == null) return null;

            var props = await activeSession.TryGetMediaPropertiesAsync();
            var timeline = activeSession.GetTimelineProperties();
            if (props == null) return null;

            string timeFormatted = "00:00 / 00:00";
            if (timeline != null)
            {
                var pos = timeline.Position;
                if (activeSession.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    var elapsedSinceUpdate = DateTime.UtcNow - timeline.LastUpdatedTime.UtcDateTime;
                    if (elapsedSinceUpdate > TimeSpan.Zero)
                    {
                        var estimatedPos = pos.Add(elapsedSinceUpdate);
                        if (estimatedPos > timeline.EndTime) estimatedPos = timeline.EndTime;
                        pos = estimatedPos;
                    }
                }
                var end = timeline.EndTime;
                timeFormatted = $"{((int)pos.TotalMinutes):D2}:{pos.Seconds:D2} / {((int)end.TotalMinutes):D2}:{end.Seconds:D2}";
            }

            string displayTitle = !string.IsNullOrWhiteSpace(props.Artist)
                ? $"{props.Artist} - {props.Title}"
                : props.Title;

            return new MediaInfo
            {
                Title = displayTitle,
                Status = LocalizationService.Instance.Get(LocalizationService.Keys.MediaPlayingStatus),
                TimeInfo = timeFormatted
            };
        }
        catch
        {
            return null;
        }
    }
}

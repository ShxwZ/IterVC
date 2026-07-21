using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Avalonia.Input;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Windows.Media.Control;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using IterVC.Desktop.Services;

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
    private readonly IColorExtractionService _colorExtractionService;

    public ObservableCollection<AppAudioItemViewModel> RunningApps { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> VbCableDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();

    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedVbCableDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophoneDevice;
    [ObservableProperty] private bool _monitorMicrophone;
    [ObservableProperty] private bool _isRouting;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private float _appsVolume = 1.0f;

    [ObservableProperty] private bool _enableOscChatbox;
    [ObservableProperty] private string _oscTemplate = "{title} - [{time}]";

    [ObservableProperty] private float _microphoneBoost = 1.0f;

    /// <summary>Nivel de pico del micrófono (0-1) para el medidor de la pestaña Mezcla.</summary>
    [ObservableProperty] private float _microphoneLevel;

    [ObservableProperty] private string _selectedLanguage = LocalizationService.Instance.CurrentLanguage;

    public IReadOnlyList<string> AvailableLanguages { get; } = SupportedLanguages.All;
    public TextsViewModel Texts { get; } = new();

    // --- Appearance ---
    [ObservableProperty] private string? _backgroundImagePath;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _backgroundImage;
    [ObservableProperty] private bool _hasBackgroundImage;
    [ObservableProperty] private string _accentColorHex = ThemeService.DefaultAccentHex;
    [ObservableProperty] private string _cardBackgroundHex = ThemeService.DefaultCardBackgroundHex;
    [ObservableProperty] private bool _autoExtractColors = true;
    [ObservableProperty] private string _toggleRoutingShortcut = "F9";
    [ObservableProperty] private bool _minimizeToTray = true;

    public KeyGesture? ToggleShortcutGesture
    {
        get
        {
            try { return KeyGesture.Parse(ToggleRoutingShortcut); }
            catch { return null; }
        }
    }

    partial void OnToggleRoutingShortcutChanged(string value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.ToggleRoutingShortcut = value);
        OnPropertyChanged(nameof(ToggleShortcutGesture));
    }

    partial void OnBackgroundImagePathChanged(string? value)
    {
        var old = BackgroundImage;
        if (string.IsNullOrEmpty(value) || !File.Exists(value))
        {
            BackgroundImage = null;
        }
        else
        {
            try
            {
                BackgroundImage = new Avalonia.Media.Imaging.Bitmap(value);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load background image.");
                BackgroundImage = null;
            }
        }
        old?.Dispose();
    }
    [ObservableProperty] private byte _accentR;
    [ObservableProperty] private byte _accentG;
    [ObservableProperty] private byte _accentB;
    [ObservableProperty] private byte _cardR;
    [ObservableProperty] private byte _cardG;
    [ObservableProperty] private byte _cardB;

    /// <summary>Folder where background images are stored.</summary>
    private static string AppDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IterVC");

    private bool _isRefreshing;
    private bool _initializing;
    private List<string>? _pendingIncludedProcessNames;
    private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private readonly Avalonia.Threading.DispatcherTimer _levelTimer;

    public MainViewModel(
        IAudioRouterService audioRouter,
        IMicrophoneService microphoneService,
        IDeviceService deviceService,
        IApplicationAudioService applicationAudioService,
        ISettingsService settingsService,
        IOscMediaService oscMediaService,
        IColorExtractionService colorExtractionService,
        ILogger<MainViewModel> logger)
    {
        _audioRouter = audioRouter;
        _microphoneService = microphoneService;
        _deviceService = deviceService;
        _applicationAudioService = applicationAudioService;
        _settingsService = settingsService;
        _oscMediaService = oscMediaService;
        _colorExtractionService = colorExtractionService;
        _logger = logger;

        _microphoneService.DataAvailable += (_, data) =>
        {
            if (SelectedMicrophoneDevice?.Id == "none") return;
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

        _levelTimer = new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(66),
            Avalonia.Threading.DispatcherPriority.Background,
            OnLevelTimerTick);
    }

    private void OnLevelTimerTick(object? sender, EventArgs e)
    {
        MicrophoneLevel = SelectedMicrophoneDevice?.Id == "none"
            ? 0f
            : _audioRouter.GetMicrophoneLevel();

        foreach (var app in RunningApps)
            app.Level = app.IsIncludedInMix ? _audioRouter.GetAppSourceLevel(app.ProcessId) : 0f;
    }

    public async Task InitializeAsync()
    {
        _initializing = true;
        try
        {
            var settings = await _settingsService.LoadAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RefreshDevices);

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

            ToggleRoutingShortcut = string.IsNullOrWhiteSpace(settings.ToggleRoutingShortcut) ? "F9" : settings.ToggleRoutingShortcut;
            OnPropertyChanged(nameof(ToggleShortcutGesture));

            MinimizeToTray = settings.MinimizeToTray;

            // Load appearance settings
            AutoExtractColors = settings.AutoExtractColors;
            if (!string.IsNullOrEmpty(settings.BackgroundImageFileName))
            {
                var bgPath = Path.Combine(AppDataFolder, settings.BackgroundImageFileName);
                if (File.Exists(bgPath))
                {
                    BackgroundImagePath = bgPath;
                    HasBackgroundImage = true;
                }
            }
            var accentHex = settings.AccentColor ?? ThemeService.DefaultAccentHex;
            var cardHex = settings.CardBackgroundColor ?? ThemeService.DefaultCardBackgroundHex;
            SetAccentFromHex(accentHex);
            SetCardFromHex(cardHex);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                ThemeService.Apply(AccentColorHex, CardBackgroundHex));

            _audioRouter.SetAppsVolume(AppsVolume);
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

            if (SelectedMicrophoneDevice is not null && SelectedMicrophoneDevice.Id != "none")
                await _microphoneService.StartAsync(SelectedMicrophoneDevice.Id);

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
        if (value.Id == "none") { try { _ = _microphoneService.StopAsync(); } catch { } }
        else _ = _microphoneService.SetDeviceAsync(value.Id);
        _ = _settingsService.UpdateAsync(s => s.MicrophoneDeviceId = value.Id);
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
        _settingsService.QueueUpdate(s => s.MicrophoneBoost = value);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.MinimizeToTray = value);
    }

    partial void OnEnableOscChatboxChanged(bool value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.EnableOscChatbox = value);
    }

    partial void OnOscTemplateChanged(string value)
    {
        if (_initializing) return;
        _settingsService.QueueUpdate(s => s.OscTemplate = value);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        var lang = string.IsNullOrEmpty(value)
            ? LocalizationService.Instance.CurrentLanguage
            : value;
        LocalizationService.Instance.SetLanguage(lang);
        Texts.RaiseAll();
        if (!_initializing)
            _ = _settingsService.UpdateAsync(s => s.Language = lang);
    }

    partial void OnAppsVolumeChanged(float value)
    {
        if (_initializing) return;
        _audioRouter.SetAppsVolume(value);
        _settingsService.QueueUpdate(s => s.AppsVolume = value);
    }

    // ----------------------------------------------------------------
    // Appearance: RGB slider handlers
    // ----------------------------------------------------------------

    private bool _suppressColorSlider;

    partial void OnAccentRChanged(byte value) => OnAccentSliderChanged();
    partial void OnAccentGChanged(byte value) => OnAccentSliderChanged();
    partial void OnAccentBChanged(byte value) => OnAccentSliderChanged();

    private void OnAccentSliderChanged()
    {
        if (_initializing || _suppressColorSlider) return;
        AccentColorHex = ThemeService.ToHex(AccentR, AccentG, AccentB);
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            ThemeService.Apply(AccentColorHex, CardBackgroundHex));
        _settingsService.QueueUpdate(s => s.AccentColor = AccentColorHex);
    }

    partial void OnCardRChanged(byte value) => OnCardSliderChanged();
    partial void OnCardGChanged(byte value) => OnCardSliderChanged();
    partial void OnCardBChanged(byte value) => OnCardSliderChanged();

    private void OnCardSliderChanged()
    {
        if (_initializing || _suppressColorSlider) return;
        CardBackgroundHex = ThemeService.ToHex(CardR, CardG, CardB);
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            ThemeService.Apply(AccentColorHex, CardBackgroundHex));
        _settingsService.QueueUpdate(s => s.CardBackgroundColor = CardBackgroundHex);
    }

    partial void OnAutoExtractColorsChanged(bool value)
    {
        if (_initializing) return;
        _ = _settingsService.UpdateAsync(s => s.AutoExtractColors = value);
        if (value && HasBackgroundImage && BackgroundImagePath is not null)
            _ = ExtractAndApplyColorsAsync(BackgroundImagePath);
    }

    private void SetAccentFromHex(string hex)
    {
        _suppressColorSlider = true;
        AccentColorHex = hex;
        var (r, g, b) = ThemeService.ParseHex(hex);
        AccentR = r; AccentG = g; AccentB = b;
        _suppressColorSlider = false;
    }

    private void SetCardFromHex(string hex)
    {
        _suppressColorSlider = true;
        CardBackgroundHex = hex;
        var (r, g, b) = ThemeService.ParseHex(hex);
        CardR = r; CardG = g; CardB = b;
        _suppressColorSlider = false;
    }

    private async Task ExtractAndApplyColorsAsync(string imagePath)
    {
        try
        {
            var (accentHex, cardHex) = await _colorExtractionService.ExtractThemeColorsAsync(imagePath);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetAccentFromHex(accentHex);
                SetCardFromHex(cardHex);
                ThemeService.Apply(AccentColorHex, CardBackgroundHex);
            });
            _ = _settingsService.UpdateAsync(s =>
            {
                s.AccentColor = accentHex;
                s.CardBackgroundColor = cardHex;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not extract colors from image");
        }
    }

    // ----------------------------------------------------------------
    // Appearance commands
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task PickBackgroundImageAsync()
    {
        var topLevel = TopLevel.GetTopLevel(
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Texts.AppearancePickImage,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" } }
            }
        });

        if (files.Count == 0) return;

        var sourceFile = files[0];
        var sourcePath = sourceFile.TryGetLocalPath();
        if (string.IsNullOrEmpty(sourcePath)) return;

        // Copy image to AppData
        Directory.CreateDirectory(AppDataFolder);
        var ext = Path.GetExtension(sourcePath);
        var destName = $"background{ext}";
        var destPath = Path.Combine(AppDataFolder, destName);
        File.Copy(sourcePath, destPath, overwrite: true);

        BackgroundImagePath = destPath;
        HasBackgroundImage = true;
        _ = _settingsService.UpdateAsync(s => s.BackgroundImageFileName = destName);

        if (AutoExtractColors)
            await ExtractAndApplyColorsAsync(destPath);
    }

    [RelayCommand]
    private void RemoveBackgroundImage()
    {
        BackgroundImagePath = null;
        HasBackgroundImage = false;
        _ = _settingsService.UpdateAsync(s => s.BackgroundImageFileName = null);
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        RemoveBackgroundImage();
        SetAccentFromHex(ThemeService.DefaultAccentHex);
        SetCardFromHex(ThemeService.DefaultCardBackgroundHex);
        AutoExtractColors = true;
        ThemeService.ResetToDefaults();
        _ = _settingsService.UpdateAsync(s =>
        {
            s.AccentColor = null;
            s.CardBackgroundColor = null;
            s.AutoExtractColors = true;
            s.BackgroundImageFileName = null;
        });
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
        InputDevices.Add(new AudioDeviceInfo { Id = "none", Name = Texts.MicrophoneNoneLabel });
        foreach (var d in _deviceService.GetInputDevices()) InputDevices.Add(d);

        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == currentOutputId)
                               ?? OutputDevices.FirstOrDefault(d => d.IsDefault);
        SelectedVbCableDevice = VbCableDevices.FirstOrDefault(d => d.Id == currentVbId)
                                ?? _deviceService.FindVbCableDevice();
        SelectedMicrophoneDevice = InputDevices.FirstOrDefault(d => d.Id == currentMicId)
                                   ?? InputDevices.FirstOrDefault(d => d.IsDefault);

        _isRefreshing = false;
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

            var initialVolume = _settingsService.Current.AppVolumes.TryGetValue(app.ProcessName, out var savedVolume)
                ? savedVolume
                : 1.0f;

            if (included)
            {
                if (await TryAddAppSourceAsync(app.ProcessId))
                    _audioRouter.SetAppSourceVolume(app.ProcessId, initialVolume);
                else
                    anyCaptureFailed = true;
            }

            RunningApps.Add(new AppAudioItemViewModel(
                app with { IsIncludedInMix = included },
                OnAppIncludedChanged,
                OnAppVolumeChanged,
                initialVolume));
        }

        if (!anyCaptureFailed)
            StatusMessage = $"{RunningApps.Count} aplicación(es) con audio detectada(s).";
    }

    private void OnAppIncludedChanged(AppAudioItemViewModel app, bool included)
        => _ = OnAppIncludedChangedAsync(app, included);

    private async Task OnAppIncludedChangedAsync(AppAudioItemViewModel app, bool included)
    {
        if (included)
        {
            if (await TryAddAppSourceAsync(app.ProcessId))
                _audioRouter.SetAppSourceVolume(app.ProcessId, app.Volume);
        }
        else
        {
            await _audioRouter.RemoveAppSourceAsync(app.ProcessId);
        }

        await PersistIncludedAppsAsync();
    }

    private void OnAppVolumeChanged(AppAudioItemViewModel app, float volume)
    {
        _audioRouter.SetAppSourceVolume(app.ProcessId, volume);
        _settingsService.QueueUpdate(s => s.AppVolumes[app.ProcessName] = volume);
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
        // VRChat descarta mensajes del chatbox que lleguen más rápido que ~1.5 s.
        const int intervalMs = 1500;
        var wasEnabled = false;

        while (!ct.IsCancellationRequested)
        {
            if (EnableOscChatbox)
            {
                wasEnabled = true;
                var mediaInfo = await GetActiveMediaInfoAsync();

                string title, status, time;
                if (mediaInfo != null && !string.IsNullOrEmpty(mediaInfo.Title))
                {
                    title = mediaInfo.Title;
                    status = mediaInfo.Status;
                    time = mediaInfo.TimeInfo;
                }
                else
                {
                    // Sin sesión de medios: el chatbox sigue visible con valores neutros.
                    title = LocalizationService.Instance.Get(LocalizationService.Keys.MediaNothingPlaying);
                    status = LocalizationService.Instance.Get(LocalizationService.Keys.MediaStoppedStatus);
                    time = DateTime.Now.ToString("HH:mm");
                }

                var message = OscTemplate
                    .Replace("{title}", title)
                    .Replace("{status}", status)
                    .Replace("{time}", time);

                if (!string.IsNullOrWhiteSpace(message))
                    _oscMediaService.SendMediaInfo(title, status, message);
            }
            else if (wasEnabled)
            {
                // Al desactivar el chatbox, borramos el último mensaje que quedó en VRChat.
                wasEnabled = false;
                _oscMediaService.ClearChatbox();
            }

            await Task.Delay(intervalMs, ct);
        }
    }

    public async Task<MediaInfo?> GetActiveMediaInfoAsync()
    {
        try
        {
            _mediaSessionManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _mediaSessionManager.GetSessions();
            if (sessions == null || sessions.Count == 0) return null;

            // Preferimos una sesión reproduciendo; si no hay, usamos la primera disponible
            // (pausada/detenida) para que el chatbox también muestre música en pausa.
            var activeSession = sessions.FirstOrDefault(s =>
                    s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? sessions.FirstOrDefault();
            if (activeSession == null) return null;

            var props = await activeSession.TryGetMediaPropertiesAsync();
            var timeline = activeSession.GetTimelineProperties();
            if (props == null) return null;

            var playbackStatus = activeSession.GetPlaybackInfo()?.PlaybackStatus;
            var isPlaying = playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string timeFormatted = "00:00 / 00:00";
            if (timeline != null)
            {
                var pos = timeline.Position;
                if (isPlaying)
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

            var statusKey = playbackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => LocalizationService.Keys.MediaPlayingStatus,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => LocalizationService.Keys.MediaPausedStatus,
                _ => LocalizationService.Keys.MediaStoppedStatus
            };

            return new MediaInfo
            {
                Title = displayTitle,
                Status = LocalizationService.Instance.Get(statusKey),
                TimeInfo = timeFormatted
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo obtener la información de medios del sistema");
            return null;
        }
    }
}

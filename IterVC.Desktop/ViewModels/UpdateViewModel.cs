using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.ViewModels;

public sealed partial class UpdateViewModel : ViewModelBase
{
    private readonly IUpdateService _updates;
    private readonly IPortableUpdateInstaller? _installer;
    private readonly ISettingsService _settings;
    private readonly IExternalUrlLauncher _urlLauncher;
    private readonly TextsViewModel _texts;
    private readonly ILogger<UpdateViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _persistenceLock = new();
    private Task _preferencePersistenceTask = Task.CompletedTask;
    private Task _automaticCheckTask = Task.CompletedTask;
    private bool _hydrating;
    private bool _suppressPreferencePersistence;
    private UpdateStatusKind _statusKind;
    private int _stopped;

    public UpdateViewModel(IUpdateService updates, ISettingsService settings,
        IExternalUrlLauncher urlLauncher, TextsViewModel texts, ILogger<UpdateViewModel> logger,
        IPortableUpdateInstaller? installer = null)
    {
        _updates = updates;
        _installer = installer;
        _settings = settings;
        _urlLauncher = urlLauncher;
        _texts = texts;
        _logger = logger;
    }

    [ObservableProperty] private bool _isConsentVisible;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _isInstalling;

    public event Action? UpdateReadyToRestart;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string? _availableVersion;
    [ObservableProperty] private string? _availableUrl;
    [ObservableProperty] private string? _status;

    public async Task HydrateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        HydrateState(settings);
        if (IsEnabled) await CheckAsync(isManual: false, cancellationToken);
    }

    internal void HydrateForStartup(AppSettings settings)
    {
        HydrateState(settings);
        if (IsEnabled)
            _automaticCheckTask = CheckAutomaticallyAsync(_lifetimeCancellation.Token);
    }

    private void HydrateState(AppSettings settings)
    {
        _hydrating = true;
        try
        {
            IsConsentVisible = settings.CheckForUpdates is null;
            IsEnabled = settings.CheckForUpdates == true;
        }
        finally { _hydrating = false; }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_hydrating || _suppressPreferencePersistence || IsConsentVisible) return;
        lock (_persistenceLock)
            _preferencePersistenceTask = PersistPreferenceAfterAsync(_preferencePersistenceTask, value);
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        IsConsentVisible = false;
        _suppressPreferencePersistence = true;
        try { IsEnabled = true; }
        finally { _suppressPreferencePersistence = false; }
        await _settings.UpdateAsync(settings => settings.CheckForUpdates = true, _lifetimeCancellation.Token);
        await CheckAsync(isManual: false, _lifetimeCancellation.Token);
    }

    [RelayCommand]
    private async Task DeclineAsync()
    {
        IsConsentVisible = false;
        _suppressPreferencePersistence = true;
        try { IsEnabled = false; }
        finally { _suppressPreferencePersistence = false; }
        await _settings.UpdateAsync(settings => settings.CheckForUpdates = false, _lifetimeCancellation.Token);
    }

    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync(isManual: true, _lifetimeCancellation.Token);

    [RelayCommand]
    private void OpenPage()
    {
        if (!_updates.TryValidateReleaseUrl(AvailableUrl, out var url)) return;
        try { _urlLauncher.Open(url); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open the update release page");
            SetStatus(UpdateStatusKind.OpenFailed);
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsInstalling || IsChecking || !IsAvailable || AvailableVersion is null || _installer is null)
            return;

        IsInstalling = true;
        SetStatus(UpdateStatusKind.Installing);
        try
        {
            if (!await _installer.DownloadAndLaunchAsync(AvailableVersion, _lifetimeCancellation.Token))
            {
                SetStatus(UpdateStatusKind.InstallFailed);
                return;
            }
            UpdateReadyToRestart?.Invoke(); // The helper is running; exit the old executable cleanly.
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to prepare portable update");
            SetStatus(UpdateStatusKind.InstallFailed);
        }
        finally { IsInstalling = false; }
    }

    [RelayCommand]
    private async Task DismissAsync()
    {
        var dismissedVersion = AvailableVersion;
        IsAvailable = false;
        AvailableVersion = null;
        AvailableUrl = null;
        if (dismissedVersion is not null)
            await _settings.UpdateAsync(settings => settings.DismissedUpdateVersion = dismissedVersion,
                _lifetimeCancellation.Token);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _lifetimeCancellation.Cancel();
        Task persistence;
        lock (_persistenceLock) persistence = _preferencePersistenceTask;
        try { await persistence; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish update preference persistence"); }
        try { await _automaticCheckTask; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish automatic update check"); }
        _lifetimeCancellation.Dispose();
    }

    private async Task CheckAutomaticallyAsync(CancellationToken cancellationToken)
    {
        try { await CheckAsync(isManual: false, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogWarning(exception, "Automatic update check failed"); }
    }

    private async Task CheckAsync(bool isManual, CancellationToken cancellationToken)
    {
        if (IsChecking || IsInstalling) return;
        IsChecking = true;
        if (isManual) SetStatus(UpdateStatusKind.Checking);
        try
        {
            var settings = _settings.Current;
            UpdateCheckResult result;
            var cacheIsFresh = !isManual && settings.LastSuccessfulUpdateCheckUtc is { } checkedAt
                && DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromHours(24)
                && AppVersion.NormalizeSemantic(settings.CachedLatestVersion) is not null
                && _updates.TryValidateReleaseUrl(settings.CachedReleaseUrl, out _);

            if (cacheIsFresh)
                result = new UpdateCheckResult(true, settings.CachedLatestVersion, settings.CachedReleaseUrl);
            else
            {
                result = await _updates.CheckAsync(cancellationToken);
                if (result.Success)
                {
                    await _settings.UpdateAsync(current =>
                    {
                        current.LastSuccessfulUpdateCheckUtc = DateTimeOffset.UtcNow;
                        current.CachedLatestVersion = result.Version;
                        current.CachedReleaseUrl = result.ReleaseUrl;
                    }, cancellationToken);
                }
            }

            if (!result.Success)
            {
                _logger.LogWarning("Update check failed");
                if (isManual) SetStatus(UpdateStatusKind.CheckFailed);
                return;
            }

            var currentVersion = AppVersion.CurrentSemanticVersion;
            var isNewer = currentVersion is not null && result.Version is not null
                && AppVersion.IsNewer(result.Version, currentVersion);
            IsAvailable = isNewer && result.Version is not null
                && AppVersion.ShouldNotify(result.Version, currentVersion!, settings.DismissedUpdateVersion, isManual);
            AvailableVersion = IsAvailable ? result.Version : null;
            AvailableUrl = IsAvailable ? result.ReleaseUrl : null;
            if (isManual) SetStatus(IsAvailable ? UpdateStatusKind.Available : UpdateStatusKind.Current);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { IsChecking = false; }
    }

    private async Task PersistPreferenceAfterAsync(Task previous, bool value)
    {
        try { await previous; }
        catch { }
        try
        {
            await _settings.UpdateAsync(settings => settings.CheckForUpdates = value,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not persist update preference"); }
    }

    public void RefreshLocalization()
    {
        if (_statusKind != UpdateStatusKind.None) SetStatus(_statusKind);
    }

    private void SetStatus(UpdateStatusKind kind)
    {
        _statusKind = kind;
        Status = kind switch
        {
            UpdateStatusKind.Checking => _texts.UpdateChecking,
            UpdateStatusKind.Current => _texts.UpdateCurrent,
            UpdateStatusKind.CheckFailed => _texts.UpdateCheckFailed,
            UpdateStatusKind.OpenFailed => _texts.UpdateOpenFailed,
            UpdateStatusKind.Available => _texts.UpdateAvailable,
            UpdateStatusKind.Installing => _texts.UpdateInstalling,
            UpdateStatusKind.InstallFailed => _texts.UpdateInstallFailed,
            _ => null
        };
    }

    private enum UpdateStatusKind { None, Checking, Current, CheckFailed, OpenFailed, Available, Installing, InstallFailed }
}

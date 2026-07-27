using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace IterVC.Desktop.ViewModels;

public sealed partial class ApplicationsViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutomaticRefreshDebounce = TimeSpan.FromMilliseconds(400);

    private readonly IApplicationAudioService _applications;
    private readonly IAudioRouterService _router;
    private readonly ISettingsService _settings;
    private readonly ILogger<ApplicationsViewModel> _logger;
    private readonly HashSet<int> _capturedProcessIds = [];
    private readonly HashSet<string> _includedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private IReadOnlyCollection<string>? _pendingIncludedProcessNames;
    private CancellationTokenSource? _automaticRefreshCancellation;
    private bool _statusShowsDetectedApps;
    private bool _disposed;

    public ApplicationsViewModel(IApplicationAudioService applications, IAudioRouterService router,
        ISettingsService settings, ILogger<ApplicationsViewModel> logger, TextsViewModel texts)
    {
        _applications = applications;
        _router = router;
        _settings = settings;
        _logger = logger;
        Texts = texts;

        _applications.ApplicationsChanged += OnApplicationsChanged;
    }

    public TextsViewModel Texts { get; }
    public ObservableCollection<AppAudioItemViewModel> RunningApps { get; } = [];
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _areApplicationsMuted;

    public bool HasSelectedApplications => RunningApps.Any(app => app.IsIncludedInMix);
    public string ApplicationsMuteButtonText => AreApplicationsMuted
        ? Texts.ApplicationAudioUnmute
        : Texts.ApplicationAudioMute;
    public string ApplicationsMuteStatusText => AreApplicationsMuted
        ? Texts.ApplicationAudioMuted
        : Texts.ApplicationAudioActive;

    partial void OnAreApplicationsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(ApplicationsMuteButtonText));
        OnPropertyChanged(nameof(ApplicationsMuteStatusText));
    }

    [RelayCommand]
    public void ToggleApplicationsMute()
    {
        _router.SetApplicationsMuted(!_router.AreApplicationsMuted);
        AreApplicationsMuted = _router.AreApplicationsMuted;
    }

    public void HydrateIncludedProcessNames(IReadOnlyCollection<string> processNames)
    {
        _pendingIncludedProcessNames = processNames;
        ReplaceIncludedProcessNames(processNames);
        AreApplicationsMuted = _router.AreApplicationsMuted;
    }

    public async Task SelectOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => await SetOutputDeviceAsync(deviceId, persist: true, cancellationToken);

    internal async Task InitializeOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => await SetOutputDeviceAsync(deviceId, persist: false, cancellationToken);

    private async Task SetOutputDeviceAsync(string deviceId, bool persist, CancellationToken cancellationToken)
    {
        _applications.UseDevice(deviceId);
        if (persist)
            await _settings.UpdateAsync(settings => settings.OutputDeviceId = deviceId, cancellationToken);

        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);

        try
        {
            var overrideNames = _pendingIncludedProcessNames;
            _pendingIncludedProcessNames = null;

            if (overrideNames is not null)
                ReplaceIncludedProcessNames(overrideNames);

            var apps = _applications.GetRunningAudioApps();
            var visibleProcessIds = apps.Select(app => app.ProcessId).ToHashSet();
            var anyCaptureFailed = false;

            // Si una aplicación se cerró o Chrome cambió su PID, liberamos primero la
            // captura anterior. El nombre incluido se conserva para reconectar el PID nuevo.
            var staleCapturedProcessIds = _capturedProcessIds
                .Where(processId => !visibleProcessIds.Contains(processId))
                .ToList();

            foreach (var processId in staleCapturedProcessIds)
            {
                await _router.RemoveAppSourceAsync(processId);
                _capturedProcessIds.Remove(processId);
            }

            RunningApps.Clear();

            foreach (var app in apps)
            {
                var included = _includedProcessNames.Contains(app.ProcessName);

                if (included && !_capturedProcessIds.Contains(app.ProcessId))
                {
                    included = await TryAddSourceAsync(app.ProcessId, cancellationToken);
                    anyCaptureFailed |= !included;
                }

                RunningApps.Add(new AppAudioItemViewModel(
                    app with { IsIncludedInMix = included },
                    this));
            }
            OnPropertyChanged(nameof(HasSelectedApplications));

            if (!anyCaptureFailed)
            {
                _statusShowsDetectedApps = true;
                UpdateDetectedAppsStatus();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal async Task SetIncludedAsync(AppAudioItemViewModel app, bool included,
        CancellationToken cancellationToken = default)
    {
        if (included == app.IsIncludedInMix) return;

        if (included)
        {
            if (!await TryAddSourceAsync(app.ProcessId, cancellationToken)) return;
            _includedProcessNames.Add(app.ProcessName);
        }
        else
        {
            await _router.RemoveAppSourceAsync(app.ProcessId);
            _capturedProcessIds.Remove(app.ProcessId);
            _includedProcessNames.Remove(app.ProcessName);
        }

        app.SetIncluded(included);
        OnPropertyChanged(nameof(HasSelectedApplications));
        await PersistIncludedAppsAsync(cancellationToken);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ApplicationsMuteButtonText));
        OnPropertyChanged(nameof(ApplicationsMuteStatusText));
        if (_statusShowsDetectedApps)
            UpdateDetectedAppsStatus();
    }

    private void OnApplicationsChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;

        var nextCancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(
            ref _automaticRefreshCancellation,
            nextCancellation);

        previousCancellation?.Cancel();
        _ = DebounceAutomaticRefreshAsync(nextCancellation);
    }

    private async Task DebounceAutomaticRefreshAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(AutomaticRefreshDebounce, cancellation.Token).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() => _ = RefreshAutomaticallyAsync());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Otra notificación reinició el debounce.
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _automaticRefreshCancellation,
                null,
                cancellation);

            cancellation.Dispose();
        }
    }

    private async Task RefreshAutomaticallyAsync()
    {
        if (_disposed) return;

        try
        {
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not refresh audio applications automatically");
        }
    }

    private async Task<bool> TryAddSourceAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _router.AddAppSourceAsync(processId);
            _capturedProcessIds.Add(processId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not capture process {ProcessId}", processId);
            _statusShowsDetectedApps = false;
            StatusMessage = string.Format(Texts.AppCaptureError, processId, exception.Message);
            return false;
        }
    }

    private void ReplaceIncludedProcessNames(IEnumerable<string> processNames)
    {
        _includedProcessNames.Clear();

        foreach (var processName in processNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            _includedProcessNames.Add(processName);
    }

    private Task PersistIncludedAppsAsync(CancellationToken cancellationToken) =>
        _settings.UpdateAsync(settings => settings.IncludedProcessNames = _includedProcessNames
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList(), cancellationToken);

    private void UpdateDetectedAppsStatus() => StatusMessage = RunningApps.Count == 1
        ? Texts.AppsDetectedOne
        : string.Format(Texts.AppsDetectedMany, RunningApps.Count);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _applications.ApplicationsChanged -= OnApplicationsChanged;
        Interlocked.Exchange(ref _automaticRefreshCancellation, null)?.Cancel();
    }
}

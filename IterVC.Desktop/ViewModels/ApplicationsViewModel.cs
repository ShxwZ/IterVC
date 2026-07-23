using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace IterVC.Desktop.ViewModels;

public sealed partial class ApplicationsViewModel : ViewModelBase
{
    private readonly IApplicationAudioService _applications;
    private readonly IAudioRouterService _router;
    private readonly ISettingsService _settings;
    private readonly ILogger<ApplicationsViewModel> _logger;
    private readonly HashSet<int> _capturedProcessIds = [];
    private IReadOnlyCollection<string>? _pendingIncludedProcessNames;
    private bool _statusShowsDetectedApps;

    public ApplicationsViewModel(IApplicationAudioService applications, IAudioRouterService router,
        ISettingsService settings, ILogger<ApplicationsViewModel> logger, TextsViewModel texts)
    {
        _applications = applications;
        _router = router;
        _settings = settings;
        _logger = logger;
        Texts = texts;
    }

    public TextsViewModel Texts { get; }
    public ObservableCollection<AppAudioItemViewModel> RunningApps { get; } = [];
    [ObservableProperty] private string? _statusMessage;

    public void HydrateIncludedProcessNames(IReadOnlyCollection<string> processNames) =>
        _pendingIncludedProcessNames = processNames;

    public async Task SelectOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => await SetOutputDeviceAsync(deviceId, persist: true, cancellationToken);

    internal async Task InitializeOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        => await SetOutputDeviceAsync(deviceId, persist: false, cancellationToken);

    private async Task SetOutputDeviceAsync(string deviceId, bool persist, CancellationToken cancellationToken)
    {
        _applications.UseDevice(deviceId);
        if (persist) await _settings.UpdateAsync(settings => settings.OutputDeviceId = deviceId, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var overrideNames = _pendingIncludedProcessNames;
        _pendingIncludedProcessNames = null;
        var apps = _applications.GetRunningAudioApps();
        var visibleIds = apps.Select(app => app.ProcessId).ToHashSet();
        var anyCaptureFailed = false;

        RunningApps.Clear();
        foreach (var app in apps)
        {
            var included = overrideNames is not null
                ? overrideNames.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase)
                : _capturedProcessIds.Contains(app.ProcessId);
            if (included && !_capturedProcessIds.Contains(app.ProcessId))
            {
                included = await TryAddSourceAsync(app.ProcessId, cancellationToken);
                anyCaptureFailed |= !included;
            }
            RunningApps.Add(new AppAudioItemViewModel(app with { IsIncludedInMix = included }, this));
        }

        _capturedProcessIds.RemoveWhere(processId => !visibleIds.Contains(processId));
        if (!anyCaptureFailed)
        {
            _statusShowsDetectedApps = true;
            UpdateDetectedAppsStatus();
        }
    }

    internal async Task SetIncludedAsync(AppAudioItemViewModel app, bool included,
        CancellationToken cancellationToken = default)
    {
        if (included == app.IsIncludedInMix) return;
        if (included)
        {
            if (!await TryAddSourceAsync(app.ProcessId, cancellationToken)) return;
        }
        else
        {
            await _router.RemoveAppSourceAsync(app.ProcessId);
            _capturedProcessIds.Remove(app.ProcessId);
        }

        app.SetIncluded(included);
        await PersistIncludedAppsAsync(cancellationToken);
    }

    public void RefreshLocalization()
    {
        if (_statusShowsDetectedApps) UpdateDetectedAppsStatus();
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not capture process {ProcessId}", processId);
            _statusShowsDetectedApps = false;
            StatusMessage = string.Format(Texts.AppCaptureError, processId, exception.Message);
            return false;
        }
    }

    private Task PersistIncludedAppsAsync(CancellationToken cancellationToken) =>
        _settings.UpdateAsync(settings => settings.IncludedProcessNames = RunningApps
            .Where(app => app.IsIncludedInMix).Select(app => app.ProcessName).Distinct().ToList(), cancellationToken);

    private void UpdateDetectedAppsStatus() => StatusMessage = RunningApps.Count == 1
        ? Texts.AppsDetectedOne
        : string.Format(Texts.AppsDetectedMany, RunningApps.Count);
}

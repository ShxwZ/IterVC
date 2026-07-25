using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Core.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace IterVC.Desktop.ViewModels;

public sealed partial class AudioRoutingViewModel : ViewModelBase
{
    private readonly IAudioRouterService _router;
    private readonly IDeviceService _devices;
    private readonly ISettingsService _settings;
    private readonly ILogger<AudioRoutingViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _hydrating;
    private bool _refreshing;
    private Task _outputSelectionTask = Task.CompletedTask;
    private Task _targetSelectionTask = Task.CompletedTask;
    private Task _volumePersistenceTask = Task.CompletedTask;

    public AudioRoutingViewModel(IAudioRouterService router, IDeviceService devices,
        ISettingsService settings, ApplicationsViewModel applications, MicrophoneViewModel microphone,
        NoiseGateViewModel noiseGate,
        ILogger<AudioRoutingViewModel> logger)
    {
        _router = router;
        _devices = devices;
        _settings = settings;
        _logger = logger;
        Applications = applications;
        Microphone = microphone;
        NoiseGate = noiseGate;
    }

    public ApplicationsViewModel Applications { get; }
    public MicrophoneViewModel Microphone { get; }
    public NoiseGateViewModel NoiseGate { get; }
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> VbCableDevices { get; } = [];

    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private AudioDeviceInfo? _selectedVbCableDevice;
    [ObservableProperty] private bool _isRouting;
    [ObservableProperty] private float _appsVolume = 1f;

    public async Task HydrateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _hydrating = true;
        try
        {
            RefreshDevices();
            Applications.HydrateIncludedProcessNames(settings.IncludedProcessNames);
            AppsVolume = settings.AppsVolume;
            _router.SetAppsVolume(AppsVolume);

            SelectedVbCableDevice = VbCableDevices.FirstOrDefault(device => device.Id == settings.VbCableDeviceId)
                ?? _devices.FindVbCableDevice();
            SelectedOutputDevice = OutputDevices.FirstOrDefault(device => device.Id == settings.OutputDeviceId)
                ?? OutputDevices.FirstOrDefault(device => device.IsDefault);

            if (SelectedVbCableDevice is not null)
            {
                await _router.StartAsync(SelectedVbCableDevice.Id, cancellationToken);
                IsRouting = true;
            }

            if (SelectedOutputDevice is not null)
                await Applications.InitializeOutputDeviceAsync(SelectedOutputDevice.Id, cancellationToken);
        }
        finally { _hydrating = false; }
    }

    public void RefreshDevices()
    {
        _refreshing = true;
        try
        {
            var currentOutputId = SelectedOutputDevice?.Id;
            var currentVbId = SelectedVbCableDevice?.Id;
            var outputs = _devices.GetOutputDevices();

            SelectedOutputDevice = null;
            SelectedVbCableDevice = null;
            OutputDevices.Clear();
            VbCableDevices.Clear();
            foreach (var device in outputs)
            {
                OutputDevices.Add(device);
                VbCableDevices.Add(device);
            }

            SelectedOutputDevice = OutputDevices.FirstOrDefault(device => device.Id == currentOutputId)
                ?? OutputDevices.FirstOrDefault(device => device.IsDefault);
            SelectedVbCableDevice = VbCableDevices.FirstOrDefault(device => device.Id == currentVbId)
                ?? _devices.FindVbCableDevice();
        }
        finally { _refreshing = false; }
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_hydrating || _refreshing || value is null) return;
        _outputSelectionTask = SelectOutputDeviceSafelyAsync(value.Id);
    }

    partial void OnSelectedVbCableDeviceChanged(AudioDeviceInfo? value)
    {
        if (_hydrating || _refreshing || value is null) return;
        _targetSelectionTask = SelectTargetDeviceSafelyAsync(value.Id);
    }

    partial void OnAppsVolumeChanged(float value)
    {
        if (_hydrating) return;
        _router.SetAppsVolume(value);
        _volumePersistenceTask = PersistVolumeSafelyAsync(value);
    }

    [RelayCommand]
    public async Task ToggleRoutingAsync()
    {
        if (IsRouting) await StopRoutingAsync();
        else await StartRoutingAsync();
    }

    public async Task StartRoutingAsync()
    {
        if (IsRouting || SelectedVbCableDevice is null) return;
        await _router.StartAsync(SelectedVbCableDevice.Id, _lifetimeCancellation.Token);
        IsRouting = true;
    }

    public async Task StopRoutingAsync()
    {
        if (!IsRouting) return;
        await _router.StopAsync();
        IsRouting = false;
    }

    public async Task StopAsync()
    {
        _lifetimeCancellation.Cancel();
        await AwaitSafelyAsync(_outputSelectionTask, "application output selection");
        await AwaitSafelyAsync(_targetSelectionTask, "routing target selection");
        await AwaitSafelyAsync(_volumePersistenceTask, "applications volume persistence");
        try
        {
            await _router.StopAsync();
            IsRouting = false;
        }
        finally { _lifetimeCancellation.Dispose(); }
    }

    private async Task SelectOutputDeviceSafelyAsync(string deviceId)
    {
        try { await Applications.SelectOutputDeviceAsync(deviceId, _lifetimeCancellation.Token); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not change application output device"); }
    }

    private async Task SelectTargetDeviceSafelyAsync(string deviceId)
    {
        try
        {
            await _router.SetTargetDeviceAsync(deviceId);
            await _settings.UpdateAsync(settings => settings.VbCableDeviceId = deviceId,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not change routing target device"); }
    }

    private async Task PersistVolumeSafelyAsync(float value)
    {
        try { await _settings.UpdateAsync(settings => settings.AppsVolume = value, _lifetimeCancellation.Token); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not persist applications volume"); }
    }

    private async Task AwaitSafelyAsync(Task task, string operation)
    {
        try { await task; }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish {Operation}", operation); }
    }
}

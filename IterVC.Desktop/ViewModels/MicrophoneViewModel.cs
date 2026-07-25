using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using IterVC.Core.Settings;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace IterVC.Desktop.ViewModels;

public sealed partial class MicrophoneViewModel : ViewModelBase
{
    private readonly IAudioRouterService _router;
    private readonly IMicrophoneService _microphone;
    private readonly IDeviceService _devices;
    private readonly ISettingsService _settings;
    private readonly ILogger<MicrophoneViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _taskLock = new();
    private Task _transitionTask = Task.CompletedTask;
    private Task _persistenceTask = Task.CompletedTask;
    private bool _hydrating;
    private bool _refreshing;
    private bool _subscribed;
    private int _stopped;

    public MicrophoneViewModel(IAudioRouterService router, IMicrophoneService microphone,
        IDeviceService devices, ISettingsService settings, ILogger<MicrophoneViewModel> logger)
    {
        _router = router;
        _microphone = microphone;
        _devices = devices;
        _settings = settings;
        _logger = logger;
    }

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = [];
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _monitor;
    [ObservableProperty] private float _boost = 1f;

    public async Task HydrateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AttachDataSubscription();
        _hydrating = true;
        try
        {
            RefreshDevices();
            var usedLegacyDisabledValue = settings.MicrophoneDeviceId == "none";
            IsEnabled = settings.MicrophoneEnabled && !usedLegacyDisabledValue;
            Monitor = settings.MonitorMicrophone;
            Boost = settings.MicrophoneBoost;
            _router.SetMicrophoneVolume(settings.MicrophoneVolume);
            _router.SetMonitorMicrophone(Monitor);
            _router.SetMicrophoneBoost(Boost);
            SelectedDevice = InputDevices.FirstOrDefault(device => device.Id == settings.MicrophoneDeviceId)
                ?? InputDevices.FirstOrDefault(device => device.IsDefault);

            if (IsEnabled && SelectedDevice is not null)
                await _microphone.StartAsync(SelectedDevice.Id, cancellationToken);

            if (usedLegacyDisabledValue && SelectedDevice is not null)
            {
                await _settings.UpdateAsync(current =>
                {
                    current.MicrophoneEnabled = false;
                    current.MicrophoneDeviceId = SelectedDevice.Id;
                }, cancellationToken);
            }
        }
        finally { _hydrating = false; }
    }

    public void RefreshDevices()
    {
        _refreshing = true;
        try
        {
            var currentId = SelectedDevice?.Id;
            SelectedDevice = null;
            InputDevices.Clear();
            foreach (var device in _devices.GetInputDevices()) InputDevices.Add(device);
            SelectedDevice = InputDevices.FirstOrDefault(device => device.Id == currentId)
                ?? InputDevices.FirstOrDefault(device => device.IsDefault);
        }
        finally { _refreshing = false; }
    }

    [RelayCommand]
    public void Toggle() => IsEnabled = !IsEnabled;

    public Task ToggleAsync()
    {
        Toggle();
        lock (_taskLock) return _transitionTask;
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_hydrating || _refreshing || value is null) return;
        QueueTransition(async () =>
        {
            if (IsEnabled) await _microphone.SetDeviceAsync(value.Id);
            await _settings.UpdateAsync(current => current.MicrophoneDeviceId = value.Id,
                _lifetimeCancellation.Token);
        }, "change microphone device");
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_hydrating) return;
        var deviceId = SelectedDevice?.Id;
        QueueTransition(async () =>
        {
            if (value && deviceId is not null)
                await _microphone.StartAsync(deviceId, _lifetimeCancellation.Token);
            else
                await _microphone.StopAsync();
            await _settings.UpdateAsync(current => current.MicrophoneEnabled = value,
                _lifetimeCancellation.Token);
        }, value ? "enable microphone" : "disable microphone");
    }

    partial void OnMonitorChanged(bool value)
    {
        if (_hydrating) return;
        _router.SetMonitorMicrophone(value);
        QueuePersistence(current => current.MonitorMicrophone = value, "persist microphone monitoring");
    }

    partial void OnBoostChanged(float value)
    {
        if (_hydrating) return;
        _router.SetMicrophoneBoost(value);
        QueuePersistence(current => current.MicrophoneBoost = value, "persist microphone boost");
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _lifetimeCancellation.Cancel();
        if (_subscribed)
        {
            _microphone.DataAvailable -= OnDataAvailable;
            _subscribed = false;
        }
        Task transition;
        Task persistence;
        lock (_taskLock)
        {
            transition = _transitionTask;
            persistence = _persistenceTask;
        }
        await ObserveAsync(transition, "microphone transition");
        await ObserveAsync(persistence, "microphone persistence");
        try { await _microphone.StopAsync(); }
        finally { _lifetimeCancellation.Dispose(); }
    }

    private void AttachDataSubscription()
    {
        if (_subscribed) return;
        _microphone.DataAvailable += OnDataAvailable;
        _subscribed = true;
    }

    private void OnDataAvailable(object? sender, byte[] data)
    {
        if (Volatile.Read(ref _stopped) == 0 && IsEnabled)
            _router.FeedMicrophoneSamples(data, data.Length);
    }

    private void QueueTransition(Func<Task> operation, string description)
    {
        lock (_taskLock)
            _transitionTask = RunAfterAsync(_transitionTask, operation, description);
    }

    private void QueuePersistence(Action<AppSettings> mutation, string description)
    {
        lock (_taskLock)
            _persistenceTask = RunAfterAsync(_persistenceTask,
                () => _settings.UpdateAsync(mutation, _lifetimeCancellation.Token), description);
    }

    private async Task RunAfterAsync(Task previous, Func<Task> operation, string description)
    {
        try { await previous; }
        catch { }
        try { await operation(); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not {Operation}", description); }
    }

    private async Task ObserveAsync(Task task, string description)
    {
        try { await task; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish {Operation}", description); }
    }
}

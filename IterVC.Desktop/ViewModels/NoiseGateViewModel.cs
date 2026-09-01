using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.ViewModels;

public sealed partial class NoiseGateViewModel : ViewModelBase
{
    private readonly IAudioRouterService _router;
    private readonly ISettingsService _settings;
    private readonly ILogger<NoiseGateViewModel> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _persistenceLock = new();
    private Task _persistenceTask = Task.CompletedTask;
    private CancellationTokenSource? _calibrationCancellation;
    private bool _hydrating;
    private int _stopped;

    public NoiseGateViewModel(IAudioRouterService router, ISettingsService settings,
        ILogger<NoiseGateViewModel> logger)
        : this(router, settings, logger, Task.Delay) { }

    internal NoiseGateViewModel(IAudioRouterService router, ISettingsService settings,
        ILogger<NoiseGateViewModel> logger, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _router = router;
        _settings = settings;
        _logger = logger;
        _delay = delay;
    }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private float _thresholdDb = -45f;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(AttackMs))] private float _attackMilliseconds = 10f;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ReleaseMs))] private float _releaseMilliseconds = 150f;
    [ObservableProperty] private float _outputLevelDb = -80f;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isCalibrating;

    public float AttackMs { get => AttackMilliseconds; set => AttackMilliseconds = value; }
    public float ReleaseMs { get => ReleaseMilliseconds; set => ReleaseMilliseconds = value; }

    public void Hydrate(AppSettings settings)
    {
        _hydrating = true;
        try
        {
            IsEnabled = settings.NoiseGateEnabled;
            ThresholdDb = settings.NoiseGateThresholdDb;
            AttackMilliseconds = settings.NoiseGateAttackMilliseconds;
            ReleaseMilliseconds = settings.NoiseGateReleaseMilliseconds;
            ApplySettings();
        }
        finally { _hydrating = false; }
    }

    partial void OnIsEnabledChanged(bool value) => ApplyAndPersist();
    partial void OnThresholdDbChanged(float value) => ApplyAndPersist();
    partial void OnAttackMillisecondsChanged(float value) => ApplyAndPersist();
    partial void OnReleaseMillisecondsChanged(float value) => ApplyAndPersist();

    private void ApplyAndPersist()
    {
        if (_hydrating || Volatile.Read(ref _stopped) != 0) return;
        ApplySettings();
        lock (_persistenceLock)
            _persistenceTask = PersistAfterAsync(_persistenceTask);
    }

    private void ApplySettings() => _router.ConfigureNoiseGate(
        IsEnabled, ThresholdDb, AttackMilliseconds, ReleaseMilliseconds);

    private async Task PersistAfterAsync(Task previous)
    {
        try { await previous; }
        catch { }
        try
        {
            await _settings.UpdateAsync(settings =>
            {
                settings.NoiseGateEnabled = IsEnabled;
                settings.NoiseGateThresholdDb = ThresholdDb;
                settings.NoiseGateAttackMilliseconds = AttackMilliseconds;
                settings.NoiseGateReleaseMilliseconds = ReleaseMilliseconds;
            }, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not persist noise gate settings"); }
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        if (Volatile.Read(ref _stopped) != 0 || IsCalibrating) return;
        IsCalibrating = true;
        _calibrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        var cancellationToken = _calibrationCancellation.Token;
        var highestAmbientLevel = -80f;
        try
        {
            for (var sample = 0; sample < 40; sample++)
            {
                highestAmbientLevel = Math.Max(highestAmbientLevel, _router.MicrophoneInputLevelDb);
                await _delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
            ThresholdDb = Math.Clamp(highestAmbientLevel + 6f, -75f, -10f);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            _calibrationCancellation.Dispose();
            _calibrationCancellation = null;
            IsCalibrating = false;
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _lifetimeCancellation.Cancel();
        _calibrationCancellation?.Cancel();
        Task persistence;
        lock (_persistenceLock) persistence = _persistenceTask;
        try { await persistence; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish noise gate persistence"); }
        _lifetimeCancellation.Dispose();
    }

    internal void UpdateMeter()
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        OutputLevelDb = Math.Clamp(_router.MicrophoneOutputLevelDb, -80f, 0f);
        IsOpen = _router.IsNoiseGateOpen;
    }
}

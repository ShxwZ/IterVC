using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;

namespace IterVC.Desktop.ViewModels;

public sealed partial class AudioMetersViewModel : ViewModelBase
{
    private readonly IAudioRouterService _router;
    private readonly DispatcherTimer _timer;
    private ApplicationsViewModel? _applications;

    public AudioMetersViewModel(IAudioRouterService router)
    {
        _router = router;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
    }

    public AudioMeterState ApplicationsOutput { get; } = new();
    public AudioMeterState MicrophoneOutput { get; } = new();
    public AudioMeterState RoutedOutput { get; } = new();
    [ObservableProperty] private bool _isNoiseGateOpen;

    internal void Attach(ApplicationsViewModel applications) => _applications = applications;

    public void Start()
    {
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        ApplicationsOutput.Reset();
        MicrophoneOutput.Reset();
        RoutedOutput.Reset();
        if (_applications is not null)
            foreach (var app in _applications.RunningApps)
                app.Meter.Reset();
    }

    internal void Update()
    {
        var now = Environment.TickCount64;
        ApplicationsOutput.Update(_router.ApplicationsOutputLevel, now);
        MicrophoneOutput.Update(_router.MicrophoneOutputLevel, now);
        RoutedOutput.Update(_router.RoutedOutputLevel, now);
        IsNoiseGateOpen = _router.IsNoiseGateOpen;

        if (_applications is null) return;
        foreach (var app in _applications.RunningApps)
        {
            if (app.IsIncludedInMix)
                app.Meter.Update(_router.GetApplicationOutputLevel(app.ProcessId), now);
            else
                app.Meter.Reset();
        }
    }

    private void OnTick(object? sender, EventArgs args) => Update();
}

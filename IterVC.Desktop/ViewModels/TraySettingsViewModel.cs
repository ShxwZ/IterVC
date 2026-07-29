using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;
using IterVC.Core.Settings;

namespace IterVC.Desktop.ViewModels;

public sealed partial class TraySettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private bool _hydrating;

    public TraySettingsViewModel(ISettingsService settings) => _settings = settings;

    [ObservableProperty] private CloseBehavior _closeBehavior = CloseBehavior.Ask;
    [ObservableProperty] private bool _minimizeToTrayWhenMinimized;

    public bool AskOnClose { get => CloseBehavior == CloseBehavior.Ask; set { if (value) CloseBehavior = CloseBehavior.Ask; } }
    public bool MinimizeOnClose { get => CloseBehavior == CloseBehavior.MinimizeToTray; set { if (value) CloseBehavior = CloseBehavior.MinimizeToTray; } }
    public bool ExitOnClose { get => CloseBehavior == CloseBehavior.Exit; set { if (value) CloseBehavior = CloseBehavior.Exit; } }

    public void Hydrate(AppSettings settings)
    {
        _hydrating = true;
        CloseBehavior = settings.CloseBehavior;
        MinimizeToTrayWhenMinimized = settings.MinimizeToTrayWhenMinimized;
        _hydrating = false;
    }

    public Task SetCloseBehaviorAsync(CloseBehavior behavior)
    {
        _hydrating = true;
        CloseBehavior = behavior;
        _hydrating = false;
        return _settings.UpdateAsync(settings => settings.CloseBehavior = behavior);
    }

    partial void OnCloseBehaviorChanged(CloseBehavior value)
    {
        OnPropertyChanged(nameof(AskOnClose));
        OnPropertyChanged(nameof(MinimizeOnClose));
        OnPropertyChanged(nameof(ExitOnClose));
        if (!_hydrating) _ = _settings.UpdateAsync(settings => settings.CloseBehavior = value);
    }

    partial void OnMinimizeToTrayWhenMinimizedChanged(bool value)
    {
        if (!_hydrating) _ = _settings.UpdateAsync(settings => settings.MinimizeToTrayWhenMinimized = value);
    }
}

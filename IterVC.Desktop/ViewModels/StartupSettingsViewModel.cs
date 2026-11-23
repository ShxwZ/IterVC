using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Settings;
using IterVC.Desktop.Services;

namespace IterVC.Desktop.ViewModels;

public sealed partial class StartupSettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly WindowsStartupRegistrationService _registration;
    private bool _hydrating;

    internal StartupSettingsViewModel(ISettingsService settings, WindowsStartupRegistrationService registration)
    {
        _settings = settings;
        _registration = registration;
    }

    [ObservableProperty] private bool _isRegistrationEnabled;
    [ObservableProperty] private bool _startHiddenOnWindowsStartup = true;
    [ObservableProperty] private string? _status;

    internal event EventHandler? RegistrationEnabledSuccessfully;

    public void Hydrate(AppSettings settings)
    {
        _hydrating = true;
        StartHiddenOnWindowsStartup = settings.StartHiddenOnWindowsStartup;
        RefreshRegistrationState();
        _hydrating = false;
    }

    public void RefreshRegistrationState()
    {
        ApplyRegistrationState(_registration.RepairIfStale());
        if (_registration.LastMutationFailed) Status = G(LocalizationService.Keys.StartupRegistrationError);
    }

    partial void OnIsRegistrationEnabledChanged(bool value)
    {
        if (_hydrating) return;
        var state = _registration.SetEnabled(value);
        ApplyRegistrationState(state);
        if (_registration.LastMutationFailed) Status = G(LocalizationService.Keys.StartupRegistrationError);
        else if (value && state == WindowsStartupRegistrationState.Enabled)
            RegistrationEnabledSuccessfully?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStartHiddenOnWindowsStartupChanged(bool value)
    {
        if (!_hydrating) _ = _settings.UpdateAsync(settings => settings.StartHiddenOnWindowsStartup = value);
    }

    private void ApplyRegistrationState(WindowsStartupRegistrationState state)
    {
        _hydrating = true;
        IsRegistrationEnabled = state == WindowsStartupRegistrationState.Enabled;
        _hydrating = false;
        Status = state switch
        {
            WindowsStartupRegistrationState.Stale => G(LocalizationService.Keys.StartupRegistrationStale),
            WindowsStartupRegistrationState.Invalid => G(LocalizationService.Keys.StartupRegistrationInvalid),
            WindowsStartupRegistrationState.Error => G(LocalizationService.Keys.StartupRegistrationError),
            _ => null
        };
    }

    private static string G(string key) => LocalizationService.Instance.Get(key);
}

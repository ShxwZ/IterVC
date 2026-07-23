using CommunityToolkit.Mvvm.ComponentModel;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Settings;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.ViewModels;

public sealed partial class LanguageViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private readonly HotkeysViewModel _hotkeys;
    private readonly ApplicationsViewModel _applications;
    private readonly UpdateViewModel _updates;
    private readonly ILogger<LanguageViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _persistenceLock = new();
    private Task _persistenceTask = Task.CompletedTask;
    private bool _hydrating;
    private bool _subscribed;
    private int _stopped;

    public LanguageViewModel(ILocalizationService localization, ISettingsService settings,
        TextsViewModel texts, HotkeysViewModel hotkeys, ApplicationsViewModel applications,
        UpdateViewModel updates, ILogger<LanguageViewModel> logger)
    {
        _localization = localization;
        _settings = settings;
        Texts = texts;
        _hotkeys = hotkeys;
        _applications = applications;
        _updates = updates;
        _logger = logger;
    }

    public TextsViewModel Texts { get; }
    public IReadOnlyList<string> AvailableLanguages { get; } = SupportedLanguages.All;
    [ObservableProperty] private string _selectedLanguage = SupportedLanguages.Spanish;

    public void Hydrate(AppSettings settings)
    {
        _hydrating = true;
        try
        {
            _localization.SetLanguage(string.IsNullOrEmpty(settings.Language)
                ? SupportedLanguages.Spanish
                : settings.Language);
            SelectedLanguage = _localization.CurrentLanguage;
            if (!_subscribed)
            {
                _localization.Changed += OnLocalizationChanged;
                _subscribed = true;
            }
            RefreshLocalizedState();
        }
        finally { _hydrating = false; }
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_hydrating) return;
        var language = string.IsNullOrEmpty(value) ? _localization.CurrentLanguage : value;
        _localization.SetLanguage(language);
        lock (_persistenceLock)
            _persistenceTask = PersistAfterAsync(_persistenceTask, language);
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        if (_subscribed)
        {
            _localization.Changed -= OnLocalizationChanged;
            _subscribed = false;
        }
        Task persistence;
        lock (_persistenceLock) persistence = _persistenceTask;
        try { await persistence; }
        catch (Exception exception) { _logger.LogError(exception, "Failed to finish language persistence"); }
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private void OnLocalizationChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopped) == 0) RefreshLocalizedState();
    }

    private void RefreshLocalizedState()
    {
        Texts.RaiseAll();
        _hotkeys.RefreshLocalization();
        _applications.RefreshLocalization();
        _updates.RefreshLocalization();
    }

    private async Task PersistAfterAsync(Task previous, string language)
    {
        try { await previous; }
        catch { }
        try
        {
            await _settings.UpdateAsync(settings => settings.Language = language,
                _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Could not persist selected language"); }
    }
}

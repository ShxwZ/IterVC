namespace IterVC.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys, UpdateViewModel updates)
        : this(language, hotkeys, updates, null)
    {
    }

    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys,
        UpdateViewModel updates, DiagnosticsViewModel? diagnostics)
    {
        Language = language;
        Hotkeys = hotkeys;
        Updates = updates;
        Diagnostics = diagnostics;
    }

    public LanguageViewModel Language { get; }
    public HotkeysViewModel Hotkeys { get; }
    public UpdateViewModel Updates { get; }
    public DiagnosticsViewModel? Diagnostics { get; }

    public async Task StopAsync()
    {
        await Updates.StopAsync();
        await Language.StopAsync();
    }
}

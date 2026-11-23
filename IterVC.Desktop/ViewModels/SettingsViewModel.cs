namespace IterVC.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys, UpdateViewModel updates)
        : this(language, hotkeys, updates, null!, null)
    {
    }

    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys,
        UpdateViewModel updates, DiagnosticsViewModel? diagnostics)
        : this(language, hotkeys, updates, null!, diagnostics)
    {
    }

    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys,
        UpdateViewModel updates, TraySettingsViewModel tray, DiagnosticsViewModel? diagnostics)
        : this(language, hotkeys, updates, tray, diagnostics, null)
    {
    }

    internal SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys,
        UpdateViewModel updates, TraySettingsViewModel tray, DiagnosticsViewModel? diagnostics, StartupSettingsViewModel? startup)
    {
        Language = language;
        Hotkeys = hotkeys;
        Updates = updates;
        Diagnostics = diagnostics;
        Tray = tray;
        Startup = startup;
    }

    public LanguageViewModel Language { get; }
    public HotkeysViewModel Hotkeys { get; }
    public UpdateViewModel Updates { get; }
    public DiagnosticsViewModel? Diagnostics { get; }
    public TraySettingsViewModel? Tray { get; }
    public StartupSettingsViewModel? Startup { get; }

    public async Task StopAsync()
    {
        await Updates.StopAsync();
        await Language.StopAsync();
    }
}

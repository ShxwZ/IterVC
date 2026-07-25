namespace IterVC.Desktop.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(LanguageViewModel language, HotkeysViewModel hotkeys, UpdateViewModel updates)
    {
        Language = language;
        Hotkeys = hotkeys;
        Updates = updates;
    }

    public LanguageViewModel Language { get; }
    public HotkeysViewModel Hotkeys { get; }
    public UpdateViewModel Updates { get; }

    public async Task StopAsync()
    {
        await Updates.StopAsync();
        await Language.StopAsync();
    }
}

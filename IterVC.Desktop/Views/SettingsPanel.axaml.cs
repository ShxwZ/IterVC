using Avalonia.Controls;

namespace IterVC.Desktop.Views;

public sealed partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        SettingsTabs.TabSelectionChanged += CancelCaptureOutsideSettingsTab;
    }

    private void CancelCaptureOutsideSettingsTab(object? sender, SelectionChangedEventArgs e)
    {
        if (!SettingsTabs.PreferencesSelected
            && ShortcutCapture?.Hotkeys is { RecordingAction: not null })
            ShortcutCapture.CancelCapture();
    }

}

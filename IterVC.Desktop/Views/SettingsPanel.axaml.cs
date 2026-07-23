using Avalonia.Controls;

namespace IterVC.Desktop.Views;

public sealed partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        SettingsTabs.SelectionChanged += CancelCaptureOutsideSettingsTab;
    }

    private void CancelCaptureOutsideSettingsTab(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: var selected }
            && !ReferenceEquals(selected, SettingsTab)
            && ShortcutCapture?.Hotkeys is { RecordingAction: not null })
            ShortcutCapture.CancelCapture();
    }

}

using Avalonia.Controls;
using Avalonia.Data;
using System.Linq;

namespace IterVC.Desktop.Views;

public sealed partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        SettingsTabs.SelectionChanged += CancelCaptureOutsideSettingsTab;
        AddNoiseSuppressionToggle();
    }

    private void AddNoiseSuppressionToggle()
    {
        // Keep the existing XAML intact and add the RNNoise switch to the
        // existing Noise Gate tab. The binding is relative to MainViewModel,
        // just like the controls already declared in SettingsPanel.axaml.
        var noiseGateTab = SettingsTabs.Items.OfType<TabItem>().ElementAtOrDefault(2);
        if (noiseGateTab?.Content is not ScrollViewer scrollViewer || scrollViewer.Content is not StackPanel panel)
            return;

        var toggle = new CheckBox
        {
            Content = "Supresión de ruido (RNNoise)",
            Margin = new Avalonia.Thickness(0, 0, 0, 2),
            [!CheckBox.IsCheckedProperty] = new Binding("Audio.NoiseGate.NoiseSuppressionEnabled", BindingMode.TwoWay)
        };

        panel.Children.Insert(1, toggle);
    }

    private void CancelCaptureOutsideSettingsTab(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: var selected }
            && !ReferenceEquals(selected, SettingsTab)
            && ShortcutCapture?.Hotkeys is { RecordingAction: not null })
            ShortcutCapture.CancelCapture();
    }
}

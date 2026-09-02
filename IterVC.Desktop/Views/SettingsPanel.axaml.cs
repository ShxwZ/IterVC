using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
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
        // Keep the existing XAML/layout intact. Add DeepFilterNet3 to the
        // existing third tab (Noise Gate) instead of replacing the settings UI.
        var noiseGateTab = SettingsTabs.Items.OfType<TabItem>().ElementAtOrDefault(2);
        if (noiseGateTab?.Content is not ScrollViewer { Content: StackPanel panel })
            return;

        if (panel.Children.OfType<Border>().Any(b => b.Tag as string == "noise-suppression-toggle"))
            return;

        var toggle = new CheckBox
        {
            Content = "Supresión de ruido (DeepFilterNet 3)",
            [!CheckBox.IsCheckedProperty] = new Binding("Audio.NoiseGate.NoiseSuppressionEnabled", BindingMode.TwoWay)
        };

        var description = new TextBlock
        {
            Text = "Mejora la voz y reduce ruido de fondo del micrófono antes de aplicar la puerta de ruido.",
            FontSize = 9.5,
            TextWrapping = TextWrapping.Wrap
        };

        var card = new Border
        {
            Tag = "noise-suppression-toggle",
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Child = new StackPanel
            {
                Spacing = 5,
                Children = { toggle, description }
            }
        };

        panel.Children.Insert(1, card);
    }

    private void CancelCaptureOutsideSettingsTab(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: var selected }
            && !ReferenceEquals(selected, SettingsTab)
            && ShortcutCapture?.Hotkeys is { RecordingAction: not null })
            ShortcutCapture.CancelCapture();
    }
}

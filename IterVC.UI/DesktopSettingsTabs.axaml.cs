using Avalonia;
using Avalonia.Controls;

namespace IterVC.UI;

/// <summary>
/// One common tabbed-settings visual used by Windows and Linux. The contents
/// are provided by platform-specific backends without duplicating tab layout.
/// </summary>
public sealed partial class DesktopSettingsTabs : UserControl
{
    public static readonly StyledProperty<string> DevicesTitleProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, string>(nameof(DevicesTitle), "Devices");
    public static readonly StyledProperty<string> MixTitleProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, string>(nameof(MixTitle), "Mix");
    public static readonly StyledProperty<string> GateTitleProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, string>(nameof(GateTitle), "Noise gate");
    public static readonly StyledProperty<string> OscTitleProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, string>(nameof(OscTitle), "OSC");
    public static readonly StyledProperty<string> PreferencesTitleProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, string>(nameof(PreferencesTitle), "Settings");

    public static readonly StyledProperty<Control?> DevicesProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, Control?>(nameof(Devices));
    public static readonly StyledProperty<Control?> MixProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, Control?>(nameof(Mix));
    public static readonly StyledProperty<Control?> GateProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, Control?>(nameof(Gate));
    public static readonly StyledProperty<Control?> OscProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, Control?>(nameof(Osc));
    public static readonly StyledProperty<Control?> PreferencesProperty =
        AvaloniaProperty.Register<DesktopSettingsTabs, Control?>(nameof(Preferences));

    public string DevicesTitle { get => GetValue(DevicesTitleProperty); set => SetValue(DevicesTitleProperty, value); }
    public string MixTitle { get => GetValue(MixTitleProperty); set => SetValue(MixTitleProperty, value); }
    public string GateTitle { get => GetValue(GateTitleProperty); set => SetValue(GateTitleProperty, value); }
    public string OscTitle { get => GetValue(OscTitleProperty); set => SetValue(OscTitleProperty, value); }
    public string PreferencesTitle { get => GetValue(PreferencesTitleProperty); set => SetValue(PreferencesTitleProperty, value); }

    public Control? Devices { get => GetValue(DevicesProperty); set => SetValue(DevicesProperty, value); }
    public Control? Mix { get => GetValue(MixProperty); set => SetValue(MixProperty, value); }
    public Control? Gate { get => GetValue(GateProperty); set => SetValue(GateProperty, value); }
    public Control? Osc { get => GetValue(OscProperty); set => SetValue(OscProperty, value); }
    public Control? Preferences { get => GetValue(PreferencesProperty); set => SetValue(PreferencesProperty, value); }

    public bool PreferencesSelected => ReferenceEquals(InnerTabs.SelectedItem, PreferencesTab);
    public event EventHandler<SelectionChangedEventArgs>? TabSelectionChanged;

    public DesktopSettingsTabs()
    {
        InitializeComponent();
        InnerTabs.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, InnerTabs))
                TabSelectionChanged?.Invoke(this, args);
        };
    }
}

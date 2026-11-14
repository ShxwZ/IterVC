using Avalonia;
using Avalonia.Controls;

namespace IterVC.UI;

/// <summary>
/// Shared application picker panel. Audio/app discovery belongs to the OS
/// adapter, but the header, list, scrolling and status layout are identical.
/// </summary>
public sealed partial class DesktopApplicationsPanel : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DesktopApplicationsPanel, string>(nameof(Title), "");
    public static readonly StyledProperty<string> HelpProperty =
        AvaloniaProperty.Register<DesktopApplicationsPanel, string>(nameof(Help), "");
    public static readonly StyledProperty<Control?> ActionsProperty =
        AvaloniaProperty.Register<DesktopApplicationsPanel, Control?>(nameof(Actions));
    public static readonly StyledProperty<Control?> ApplicationItemsProperty =
        AvaloniaProperty.Register<DesktopApplicationsPanel, Control?>(nameof(ApplicationItems));
    public static readonly StyledProperty<Control?> StatusProperty =
        AvaloniaProperty.Register<DesktopApplicationsPanel, Control?>(nameof(Status));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Help { get => GetValue(HelpProperty); set => SetValue(HelpProperty, value); }
    public Control? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public Control? ApplicationItems { get => GetValue(ApplicationItemsProperty); set => SetValue(ApplicationItemsProperty, value); }
    public Control? Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    public DesktopApplicationsPanel() => InitializeComponent();
}

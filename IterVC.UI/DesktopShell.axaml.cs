using Avalonia.Controls;

namespace IterVC.UI;

/// <summary>
/// Identical main-window grid and slots for both OS entry points.
/// Slots intentionally accept Controls: platform backends may provide different
/// device selectors while preserving the same actual shell/layout source.
/// </summary>
public sealed partial class DesktopShell : UserControl
{
    public DesktopShell() => InitializeComponent();

    public Control? HeaderLeft
    {
        get => HeaderLeftHost.Content as Control;
        set => HeaderLeftHost.Content = value;
    }

    public Control? HeaderRight
    {
        get => HeaderRightHost.Content as Control;
        set => HeaderRightHost.Content = value;
    }

    public Control? Notices
    {
        get => NoticesHost.Content as Control;
        set => NoticesHost.Content = value;
    }

    public Control? Applications
    {
        get => ApplicationsHost.Content as Control;
        set => ApplicationsHost.Content = value;
    }

    public Control? Settings
    {
        get => SettingsHost.Content as Control;
        set => SettingsHost.Content = value;
    }

    public Control? Footer
    {
        get => FooterHost.Content as Control;
        set => FooterHost.Content = value;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using IterVC.Desktop.Services;

namespace IterVC.Desktop.Views;

/// <summary>
/// Thin Avalonia adapter that forwards window close, minimize, and visibility events to
/// <see cref="DesktopLifecycleCoordinator"/> while keeping business and UI state out of ViewModels.
/// </summary>
public sealed partial class MainWindow : Window
{
    private DesktopLifecycleCoordinator? _lifecycle;
    private bool _terminalExit;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                _lifecycle?.TrackWindowState(WindowState);
                if (WindowState == WindowState.Minimized)
                    _ = _lifecycle?.RequestMinimizeAsync();
            }
        };
    }

    internal void AttachLifecycle(DesktopLifecycleCoordinator lifecycle) => _lifecycle = lifecycle;
    internal void AllowTerminalExit() => _terminalExit = true;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_terminalExit) return;
        e.Cancel = true;
        _ = _lifecycle?.RequestCloseAsync();
    }

    internal Task<CloseBehaviorChoice?> ShowCloseBehaviorDialogAsync() => CloseBehaviorDialog.ShowAsync(this);
    internal Task<bool> ShowTrayHideNotificationAsync() => TrayHideNotificationWindow.ShowAsync(this);
    internal Task<bool> ShowStartupRegistrationNoticeAsync() => StartupRegistrationNoticeWindow.ShowAsync(this);
}

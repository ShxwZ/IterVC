using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using IterVC.Core.Settings;

namespace IterVC.Linux;

/// <summary>
/// Reproduces the Windows desktop close-policy experience without relying on
/// Windows-only lifecycle services. Hiding is only offered when a system tray
/// was successfully registered on the running Linux desktop.
/// </summary>
internal sealed partial class MainWindow
{
    private bool _closePromptOpen;
    private bool _forceExit;
    private readonly RadioButton _closeAsk = new() { GroupName = "IterVCClosing" };
    private readonly RadioButton _closeTray = new() { GroupName = "IterVCClosing" };
    private readonly RadioButton _closeExit = new() { GroupName = "IterVCClosing" };

    private Control CloseSettingsControls()
    {
        _closeAsk.Content = L("Preguntar al cerrar", "Ask when closing");
        _closeTray.Content = L("Minimizar a la bandeja al cerrar",
            "Minimize to system tray on close");
        _closeExit.Content = L("Salir al cerrar", "Exit when closing");
        _closeAsk.IsChecked = _settings.CloseBehavior == CloseBehavior.Ask;
        _closeTray.IsChecked = _settings.CloseBehavior == CloseBehavior.MinimizeToTray;
        _closeExit.IsChecked = _settings.CloseBehavior == CloseBehavior.Exit;

        void Update(object? _, EventArgs __)
        {
            if (_loadingPreferences) return;
            if (_closeAsk.IsChecked == true) _settings.CloseBehavior = CloseBehavior.Ask;
            else if (_closeTray.IsChecked == true) _settings.CloseBehavior = CloseBehavior.MinimizeToTray;
            else _settings.CloseBehavior = CloseBehavior.Exit;
            SavePreferences();
        }
        _closeAsk.IsCheckedChanged += Update;
        _closeTray.IsCheckedChanged += Update;
        _closeExit.IsCheckedChanged += Update;

        return Vertical(_closeAsk, _closeTray, _closeExit,
            Label(L("La opción de bandeja solo funciona cuando el escritorio Linux dispone de ella.",
                "System-tray hiding works only when your Linux desktop provides a tray."),
                10, "TextSubtleBrush"));
    }

    internal void RequestTerminalExit()
    {
        _forceExit = true;
        Close();
    }

    private void HideInTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private async Task<CloseBehavior?> PromptCloseBehaviorAsync()
    {
        var dialog = new Window
        {
            Title = L("Cerrar IterVC", "Close IterVC"),
            Width = 460, Height = 205,
            MinWidth = 380, MinHeight = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = C("AppBackgroundBrush")
        };
        var question = Label(L("¿Qué quieres hacer al cerrar IterVC?",
            "What should happen when closing IterVC?"), 14, "TextStrongBrush", true);
        var options = new StackPanel { Orientation = Orientation.Horizontal,
            Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ActionButton(L("Cancelar", "Cancel"));
        var minimize = ActionButton(L("A la bandeja", "To tray"));
        var exit = ActionButton(L("Salir", "Exit"), primary: true);
        minimize.IsEnabled = TrayConfigured;
        cancel.Click += (_, _) => dialog.Close((CloseBehavior?)null);
        minimize.Click += (_, _) => dialog.Close((CloseBehavior?)CloseBehavior.MinimizeToTray);
        exit.Click += (_, _) => dialog.Close((CloseBehavior?)CloseBehavior.Exit);
        options.Children.Add(cancel);
        options.Children.Add(minimize);
        options.Children.Add(exit);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 24,
            Children =
            {
                question,
                Label(L("El enrutamiento seguirá activo si minimizas a la bandeja.",
                    "Routing remains active if you minimize to the tray."),
                    11, "TextSecondaryBrush"),
                options
            }
        };
        return await dialog.ShowDialog<CloseBehavior?>(this);
    }
}

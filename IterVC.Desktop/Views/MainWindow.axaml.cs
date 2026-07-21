using Avalonia.Controls;

namespace IterVC.Desktop.Views;

/// <summary>
/// Code-behind mínimo, exigido por Avalonia únicamente para cargar el XAML.
/// Toda la lógica reside en MainViewModel (MVVM estricto, sin lógica aquí).
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ShortcutTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key;
        if (key is Avalonia.Input.Key.LeftCtrl or Avalonia.Input.Key.RightCtrl or
            Avalonia.Input.Key.LeftShift or Avalonia.Input.Key.RightShift or
            Avalonia.Input.Key.LeftAlt or Avalonia.Input.Key.RightAlt or
            Avalonia.Input.Key.LWin or Avalonia.Input.Key.RWin or
            Avalonia.Input.Key.None)
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        var gesture = new Avalonia.Input.KeyGesture(key, modifiers);
        
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.ToggleRoutingShortcut = gesture.ToString();
        }
    }
}

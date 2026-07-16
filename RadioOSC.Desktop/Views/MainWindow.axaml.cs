using Avalonia.Controls;

namespace RadioOSC.Desktop.Views;

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
}

using Avalonia.Controls;
using Avalonia.Input;
using IterVC.Desktop.ViewModels;

namespace IterVC.Desktop.Views;

public sealed partial class ApplicationsPanel : UserControl
{
    public ApplicationsPanel()
    {
        InitializeComponent();
    }

    private void OnAppRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: AppAudioItemViewModel app })
        {
            app.IsIncludedInMix = !app.IsIncludedInMix;
            e.Handled = true;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using IterVC.Core.Localization;
using IterVC.Core.Settings;

namespace IterVC.Desktop.Views;

internal readonly record struct CloseBehaviorChoice(CloseBehavior Behavior, bool Remember);

internal sealed partial class CloseBehaviorDialog : Window
{
    private CloseBehaviorDialog()
    {
        InitializeComponent();

        Title = LocalizationService.Instance.Get(LocalizationService.Keys.TrayCloseDialogTitle);
        DialogTitle.Text = Title;
        DialogBody.Text = LocalizationService.Instance.Get(LocalizationService.Keys.TrayCloseDialogBody);
        RememberChoice.Content = LocalizationService.Instance.Get(LocalizationService.Keys.TrayRememberChoice);
        ExitButtonText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.TrayExit);
        MinimizeButtonText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.TrayMinimize);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
        Close(new CloseBehaviorChoice(CloseBehavior.MinimizeToTray, RememberChoice.IsChecked == true));

    private void OnExitClick(object? sender, RoutedEventArgs e) =>
        Close(new CloseBehaviorChoice(CloseBehavior.Exit, RememberChoice.IsChecked == true));

    public static Task<CloseBehaviorChoice?> ShowAsync(Window owner) =>
        new CloseBehaviorDialog().ShowDialog<CloseBehaviorChoice?>(owner);
}

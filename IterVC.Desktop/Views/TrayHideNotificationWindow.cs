using Avalonia.Controls;
using IterVC.Core.Localization;

namespace IterVC.Desktop.Views;

internal sealed partial class TrayHideNotificationWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();

    private TrayHideNotificationWindow()
    {
        InitializeComponent();

        Title = "IterVC";
        NoticeText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.TrayHideNotice);
        SuppressNotice.Content = LocalizationService.Instance.Get(LocalizationService.Keys.TrayDoNotShowAgain);
        DismissButtonText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.TrayDismiss);
        Closed += (_, _) => _result.TrySetResult(false);
    }

    private void OnDismissClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result.TrySetResult(SuppressNotice.IsChecked == true);
        Close();
    }

    public static async Task<bool> ShowAsync(Window _)
    {
        var window = new TrayHideNotificationWindow();
        // The main window is already hidden before this notification is shown.
        // Avalonia rejects a non-visible owner, so the notification must be modeless.
        window.Show();
        return await window._result.Task;
    }
}

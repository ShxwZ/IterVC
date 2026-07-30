using Avalonia.Controls;
using IterVC.Core.Localization;

namespace IterVC.Desktop.Views;

internal sealed partial class StartupRegistrationNoticeWindow : Window
{
    private readonly TaskCompletionSource<bool> _result = new();

    private StartupRegistrationNoticeWindow()
    {
        InitializeComponent();

        Title = "IterVC";
        NoticeText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.StartupRegistrationPortableNotice);
        SuppressNotice.Content = LocalizationService.Instance.Get(LocalizationService.Keys.TrayDoNotShowAgain);
        DismissButtonText.Text = LocalizationService.Instance.Get(LocalizationService.Keys.StartupRegistrationPortableNoticeDismiss);
        Closed += (_, _) => _result.TrySetResult(false);
    }

    private void OnDismissClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _result.TrySetResult(SuppressNotice.IsChecked == true);
        Close();
    }

    public static async Task<bool> ShowAsync(Window _)
    {
        var window = new StartupRegistrationNoticeWindow();
        // This notice must remain modeless so enabling startup never blocks Settings.
        window.Show();
        return await window._result.Task;
    }
}

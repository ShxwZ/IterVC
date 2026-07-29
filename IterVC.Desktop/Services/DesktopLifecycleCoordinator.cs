using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Core.Settings;
using IterVC.Desktop.ViewModels;
using IterVC.Desktop.Views;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

/// <summary>Owns the non-terminal window lifecycle and the single tray icon.</summary>
internal sealed class DesktopLifecycleCoordinator : IDisposable
{
    private readonly TraySettingsViewModel _settings;
    private readonly ISettingsService _persistedSettings;
    private readonly ILogger<DesktopLifecycleCoordinator> _logger;
    private readonly Func<Task> _exit;
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _openMenuItem;
    private readonly NativeMenuItem _exitMenuItem;
    private MainWindow? _window;
    private WindowState _lastVisibleState = WindowState.Normal;
    private Task<bool>? _trayHideNotificationTask;
    private bool _disposed;
    private int _decisionInProgress;

    public DesktopLifecycleCoordinator(TraySettingsViewModel settings, ISettingsService persistedSettings,
        ILogger<DesktopLifecycleCoordinator> logger, Func<Task> exit)
    {
        _settings = settings;
        _persistedSettings = persistedSettings;
        _logger = logger;
        _exit = exit;
        _openMenuItem = new NativeMenuItem();
        _openMenuItem.Click += (_, _) => Restore();
        _exitMenuItem = new NativeMenuItem();
        _exitMenuItem.Click += async (_, _) => await _exit();
        _trayIcon = new TrayIcon
        {
            ToolTipText = "IterVC",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://IterVC/Assets/icon.ico"))),
            Menu = new NativeMenu { Items = { _openMenuItem, new NativeMenuItemSeparator(), _exitMenuItem } }
        };
        RefreshLocalizedStrings();
        LocalizationService.Instance.Changed += OnLocalizationChanged;
        _trayIcon.Clicked += (_, _) => Restore();
        TrayIcon.SetIcons(Application.Current!, [_trayIcon]);
    }

    public void Attach(MainWindow window) => _window = window;

    private void OnLocalizationChanged(object? sender, EventArgs e) => RefreshLocalizedStrings();

    private void RefreshLocalizedStrings()
    {
        _openMenuItem.Header = LocalizationService.Instance.Get(LocalizationService.Keys.TrayOpen);
        _exitMenuItem.Header = LocalizationService.Instance.Get(LocalizationService.Keys.TrayExit);
    }

    public void TrackWindowState(WindowState state)
    {
        if (state != WindowState.Minimized) _lastVisibleState = state;
    }

    public async Task RequestCloseAsync()
    {
        try
        {
            if (Interlocked.Exchange(ref _decisionInProgress, 1) != 0) return;
            try
            {
                var behavior = _settings.CloseBehavior;
                if (behavior == CloseBehavior.Ask)
                {
                    var result = _window is null ? null : await _window.ShowCloseBehaviorDialogAsync();
                    if (result is null) return;
                    behavior = result.Value.Behavior;
                    if (result.Value.Remember)
                        await _settings.SetCloseBehaviorAsync(behavior);
                }

                if (behavior == CloseBehavior.Exit)
                    await _exit();
                else
                    await HideToTrayAsync();
            }
            finally { Interlocked.Exchange(ref _decisionInProgress, 0); }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process the window close request");
        }
    }

    public async Task RequestMinimizeAsync()
    {
        try
        {
            if (_settings.MinimizeToTrayWhenMinimized)
                await HideToTrayAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process the window minimize request");
        }
    }

    private Task HideToTrayAsync()
    {
        if (_window is null) return Task.CompletedTask;
        _window.Hide();
        if (!_persistedSettings.Current.TrayHideNotificationSuppressed && _trayHideNotificationTask is null)
        {
            var notification = _window.ShowTrayHideNotificationAsync();
            _trayHideNotificationTask = notification;
            _ = CompleteTrayHideNotificationAsync(notification);
        }

        return Task.CompletedTask;
    }

    private async Task CompleteTrayHideNotificationAsync(Task<bool> notification)
    {
        try
        {
            if (await notification)
                await _persistedSettings.UpdateAsync(settings => settings.TrayHideNotificationSuppressed = true);
        }
        catch
        {
            // The notification must never block or destabilize tray lifecycle actions.
        }
        finally
        {
            if (ReferenceEquals(notification, _trayHideNotificationTask))
                _trayHideNotificationTask = null;
        }
    }

    private void Restore()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window is null) return;
            _window.WindowState = _lastVisibleState;
            _window.Show();
            _window.Activate();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LocalizationService.Instance.Changed -= OnLocalizationChanged;
        _trayIcon.Dispose();
        TrayIcon.SetIcons(Application.Current!, null);
    }
}

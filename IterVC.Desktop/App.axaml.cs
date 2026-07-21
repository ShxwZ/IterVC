using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Desktop.ViewModels;
using IterVC.Desktop.Views;
using System.Diagnostics;

namespace IterVC.Desktop;

public sealed class App : Application
{
    private TrayIcon? _trayIcon;
    private bool _exitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Debug.WriteLine("4 - OnFrameworkInitializationCompleted");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Debug.WriteLine("5 - Creando ventana");
            var mainViewModel = Program.AppHost.Services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = mainViewModel };

            mainWindow.Opened += async (_, _) =>
            {
                Debug.WriteLine("8 - Ventana abierta, iniciando inicialización");
                try
                {
                    await Program.AppHost.StartAsync().ConfigureAwait(false);
                    Debug.WriteLine("9 - AppHost iniciado");
                    await mainViewModel.InitializeAsync();
                    Debug.WriteLine("10 - ViewModel inicializado");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ERROR: {ex}");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        mainViewModel.StatusMessage = $"Error:\n{ex.Message}\n\n{ex.StackTrace}");
                }
            };

            // Cerrar con la X oculta a la bandeja (si la opción está activa) en vez de salir;
            // el audio sigue enrutándose en segundo plano.
            mainWindow.Closing += (_, e) =>
            {
                if (_exitRequested || !mainViewModel.MinimizeToTray) return;
                e.Cancel = true;
                mainWindow.Hide();
            };

            SetupTrayIcon(desktop, mainWindow, mainViewModel);

            desktop.MainWindow = mainWindow;
            mainWindow.Show();

            desktop.ShutdownRequested += (_, _) =>
            {
                _trayIcon?.Dispose();
                // Task.Run evita capturar el contexto de UI: bloquear aquí garantiza que el
                // guardado pendiente del debounce llegue a disco antes de morir el proceso.
                var settings = Program.AppHost.Services.GetRequiredService<ISettingsService>();
                Task.Run(() => settings.FlushAsync()).GetAwaiter().GetResult();
                Task.Run(() => Program.AppHost.StopAsync()).GetAwaiter().GetResult();
            };
        }

        Debug.WriteLine("6 - base.OnFrameworkInitializationCompleted");
        base.OnFrameworkInitializationCompleted();
        Debug.WriteLine("7 - Completado");
    }

    private void SetupTrayIcon(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainViewModel mainViewModel)
    {
        var showItem = new NativeMenuItem();
        showItem.Click += (_, _) => ShowMainWindow(mainWindow);

        var exitItem = new NativeMenuItem();
        exitItem.Click += (_, _) =>
        {
            _exitRequested = true;
            desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://IterVC/Assets/icon.ico"))),
            Menu = menu
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow(mainWindow);
        _trayIcon = trayIcon;

        void ApplyTexts()
        {
            trayIcon.ToolTipText = mainViewModel.Texts.AppTitle;
            showItem.Header = mainViewModel.Texts.TrayShowWindow;
            exitItem.Header = mainViewModel.Texts.TrayExit;
        }

        ApplyTexts();
        LocalizationService.Instance.Changed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyTexts);

        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using IterVC.Desktop.ViewModels;
using IterVC.Desktop.Views;
using IterVC.Desktop.Services;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using IterVC.Core.Interfaces;

namespace IterVC.Desktop;

public sealed class App : Application
{
    private int _exitStarted;
    private static SingleInstanceCoordinator? _primaryInstance;
    internal static AppLaunchOptions LaunchOptions { get; set; }

    internal static void ConfigurePrimaryInstance(SingleInstanceCoordinator instance) => _primaryInstance = instance;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, args) =>
            Program.TryLogCritical(args.Exception, "Unhandled Avalonia dispatcher exception");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logger = Program.AppHost.Services.GetRequiredService<ILogger<App>>();
            var mainViewModel = Program.AppHost.Services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            DesktopLifecycleCoordinator? lifecycle = null;
            var globalHotkey = Program.AppHost.Services.GetRequiredService<IGlobalHotkeyService>();
            var hotkeyActionGate = new SemaphoreSlim(1, 1);
            var hotkeyActionsStopping = new CancellationTokenSource();

            void ConfigureHotkeys()
            {
                var errors = globalHotkey.Configure(mainViewModel.Settings.Hotkeys.BuildBindings()).Errors;
                string ActionLabel(HotkeyAction action) => action switch
                {
                    HotkeyAction.ToggleRouting => mainViewModel.Texts.HotkeyToggleRouting,
                    HotkeyAction.StartRouting => mainViewModel.Texts.HotkeyStartRouting,
                    HotkeyAction.StopRouting => mainViewModel.Texts.HotkeyStopRouting,
                    HotkeyAction.ToggleApplicationsMute => mainViewModel.Texts.HotkeyToggleApplicationsMute,
                    _ => mainViewModel.Texts.HotkeyToggleMicrophone
                };
                mainViewModel.Settings.Hotkeys.RegistrationStatus = errors.Count == 0
                    ? null
                    : string.Format(mainViewModel.Texts.HotkeyRegistrationFailed,
                        string.Join(" | ", errors.Select(x => $"{ActionLabel(x.Key)}: "
                            + x.Value)));
            }

            globalHotkey.Pressed += (_, action) => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await hotkeyActionGate.WaitAsync(hotkeyActionsStopping.Token);
                    try
                    {
                        if (hotkeyActionsStopping.IsCancellationRequested) return;
                        switch (action)
                        {
                            case HotkeyAction.ToggleRouting: await mainViewModel.Audio.ToggleRoutingAsync(); break;
                            case HotkeyAction.StartRouting: await mainViewModel.Audio.StartRoutingAsync(); break;
                            case HotkeyAction.StopRouting: await mainViewModel.Audio.StopRoutingAsync(); break;
                            case HotkeyAction.ToggleApplicationsMute:
                                mainViewModel.Audio.Applications.ToggleApplicationsMute();
                                break;
                            case HotkeyAction.ToggleMicrophone: await mainViewModel.Audio.Microphone.ToggleAsync(); break;
                        }
                    }
                    finally { hotkeyActionGate.Release(); }
                }
                catch (OperationCanceledException) { }
            });
            globalHotkey.Failed += (_, error) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                mainViewModel.Settings.Hotkeys.RegistrationStatus =
                    string.Format(mainViewModel.Texts.HotkeyRegistrationFailed, error));
            async Task InitializeAsync()
            {
                try
                {
                    await Program.AppHost.StartAsync().ConfigureAwait(false);
                    await mainViewModel.InitializeAsync();
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ConfigureHotkeys();
                        if (!LaunchOptions.IsWindowsStartup || !Program.AppHost.Services
                                .GetRequiredService<ISettingsService>().Current.StartHiddenOnWindowsStartup)
                            mainWindow.Show();
                    });
                    logger.LogInformation("Application initialization completed");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Application initialization failed");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mainViewModel.StatusMessage = $"Error: {ex.Message}";
                        mainWindow.Show();
                    });
                }
            }

            async Task ExitAsync()
            {
                if (Interlocked.Exchange(ref _exitStarted, 1) != 0) return;
                mainWindow.AllowTerminalExit();
                try
                {
                    lifecycle?.Dispose();
                    hotkeyActionsStopping.Cancel();
                    globalHotkey.Dispose();
                    await mainViewModel.DisposeAsync();
                    await Program.AppHost.StopAsync();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Application cleanup failed; continuing terminal shutdown");
                }
                finally
                {
                    desktop.Shutdown();
                }
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            lifecycle = new DesktopLifecycleCoordinator(
                mainViewModel.Settings.Tray!,
                mainViewModel.Settings.Startup!,
                Program.AppHost.Services.GetRequiredService<ISettingsService>(),
                Program.AppHost.Services.GetRequiredService<ILogger<DesktopLifecycleCoordinator>>(),
                ExitAsync);
            lifecycle.Attach(mainWindow);
            _primaryInstance?.StartListener(lifecycle.Restore);
            _ = InitializeAsync();
            mainWindow.AttachLifecycle(lifecycle);

            desktop.ShutdownRequested += (_, args) =>
            {
                if (Volatile.Read(ref _exitStarted) == 0)
                {
                    args.Cancel = true;
                    _ = ExitAsync();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }


}

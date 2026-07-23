using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using IterVC.Desktop.ViewModels;
using IterVC.Desktop.Views;
using System.Diagnostics;
using IterVC.Desktop.Services;

namespace IterVC.Desktop;

public sealed class App : Application
{
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
            mainWindow.Opened += async (_, _) =>
            {
                Debug.WriteLine("8 - Ventana abierta, iniciando inicialización");
                try
                {
                    await Program.AppHost.StartAsync().ConfigureAwait(false);
                    Debug.WriteLine("9 - AppHost iniciado");
                    await mainViewModel.InitializeAsync();
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ConfigureHotkeys);
                    Debug.WriteLine("10 - ViewModel inicializado");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ERROR: {ex}");
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        mainViewModel.StatusMessage = $"Error:\n{ex.Message}\n\n{ex.StackTrace}");
                }
            };

            desktop.MainWindow = mainWindow;
            mainWindow.Show(); 

            desktop.ShutdownRequested += async (_, _) =>
            {
                hotkeyActionsStopping.Cancel();
                globalHotkey.Dispose();
                await mainViewModel.DisposeAsync();
                await Program.AppHost.StopAsync();
            };
        }

        Debug.WriteLine("6 - base.OnFrameworkInitializationCompleted");
        base.OnFrameworkInitializationCompleted();
        Debug.WriteLine("7 - Completado");
    }


}

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
            var globalHotkey = new GlobalHotkeyService();
            var hotkeyActionGate = new SemaphoreSlim(1, 1);
            var hotkeyActionsStopping = new CancellationTokenSource();

            void ConfigureHotkeys()
            {
                var bindings = new[]
                {
                    new HotkeyBinding(HotkeyAction.ToggleRouting, mainViewModel.ToggleRoutingHotkeyEnabled, mainViewModel.ToggleRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.StartRouting, mainViewModel.StartRoutingHotkeyEnabled, mainViewModel.StartRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.StopRouting, mainViewModel.StopRoutingHotkeyEnabled, mainViewModel.StopRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.ToggleMicrophone, mainViewModel.ToggleMicrophoneHotkeyEnabled, mainViewModel.ToggleMicrophoneHotkeyGesture)
                };
                var errors = globalHotkey.Configure(bindings);
                string ActionLabel(HotkeyAction action) => action switch
                {
                    HotkeyAction.ToggleRouting => mainViewModel.Texts.HotkeyToggleRouting,
                    HotkeyAction.StartRouting => mainViewModel.Texts.HotkeyStartRouting,
                    HotkeyAction.StopRouting => mainViewModel.Texts.HotkeyStopRouting,
                    _ => mainViewModel.Texts.HotkeyToggleMicrophone
                };
                mainViewModel.SetGlobalHotkeyStatus(errors.Count == 0
                    ? null
                    : string.Format(mainViewModel.Texts.HotkeyRegistrationFailed,
                        string.Join(" | ", errors.Select(x => $"{ActionLabel(x.Key)}: "
                            + x.Value))));
            }

            mainViewModel.TryConfigureCapturedHotkey = (action, gesture) =>
            {
                var bindings = new[]
                {
                    new HotkeyBinding(HotkeyAction.ToggleRouting, action == "ToggleRouting" || mainViewModel.ToggleRoutingHotkeyEnabled, action == "ToggleRouting" ? gesture : mainViewModel.ToggleRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.StartRouting, action == "StartRouting" || mainViewModel.StartRoutingHotkeyEnabled, action == "StartRouting" ? gesture : mainViewModel.StartRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.StopRouting, action == "StopRouting" || mainViewModel.StopRoutingHotkeyEnabled, action == "StopRouting" ? gesture : mainViewModel.StopRoutingHotkeyGesture),
                    new HotkeyBinding(HotkeyAction.ToggleMicrophone, action == "ToggleMicrophone" || mainViewModel.ToggleMicrophoneHotkeyEnabled, action == "ToggleMicrophone" ? gesture : mainViewModel.ToggleMicrophoneHotkeyGesture)
                };
                var errors = globalHotkey.Configure(bindings);
                var target = Enum.Parse<HotkeyAction>(action);
                if (errors.Count == 0) return null;
                ConfigureHotkeys(); // Restore the previously persisted registration set.
                var error = errors.TryGetValue(target, out var targetError) ? targetError : errors.First().Value;
                return error == GlobalHotkeyService.WorkerUnavailableError
                        ? string.Format(mainViewModel.Texts.HotkeyRegistrationFailed, error)
                    : error;
            };

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
                            case HotkeyAction.ToggleRouting: await mainViewModel.ToggleRoutingFromHotkeyAsync(); break;
                            case HotkeyAction.StartRouting: await mainViewModel.StartRoutingFromHotkeyAsync(); break;
                            case HotkeyAction.StopRouting: await mainViewModel.StopRoutingFromHotkeyAsync(); break;
                            case HotkeyAction.ToggleMicrophone: await mainViewModel.ToggleMicrophoneFromHotkeyAsync(); break;
                        }
                    }
                    finally { hotkeyActionGate.Release(); }
                }
                catch (OperationCanceledException) { }
            });
            globalHotkey.Failed += (_, error) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                mainViewModel.SetGlobalHotkeyStatus(
                    string.Format(mainViewModel.Texts.HotkeyRegistrationFailed, error)));
            mainViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName?.EndsWith("HotkeyEnabled", StringComparison.Ordinal) == true ||
                    args.PropertyName?.EndsWith("HotkeyGesture", StringComparison.Ordinal) == true)
                    ConfigureHotkeys();
            };

            mainWindow.Opened += async (_, _) =>
            {
                Debug.WriteLine("8 - Ventana abierta, iniciando inicializaciÃ³n");
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
                await Program.AppHost.StopAsync();
            };
        }

        Debug.WriteLine("6 - base.OnFrameworkInitializationCompleted");
        base.OnFrameworkInitializationCompleted();
        Debug.WriteLine("7 - Completado");
    }


}

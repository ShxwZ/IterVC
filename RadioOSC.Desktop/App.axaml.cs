using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RadioOSC.Desktop.ViewModels;
using RadioOSC.Desktop.Views;
using System.Diagnostics;

namespace RadioOSC.Desktop;

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

            desktop.MainWindow = mainWindow;
            mainWindow.Show(); 

            desktop.ShutdownRequested += async (_, _) =>
            {
                await Program.AppHost.StopAsync();
            };
        }

        Debug.WriteLine("6 - base.OnFrameworkInitializationCompleted");
        base.OnFrameworkInitializationCompleted();
        Debug.WriteLine("7 - Completado");
    }


}
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Desktop.ViewModels;
using System.Diagnostics;

namespace IterVC.Desktop;

internal static class Program
{
    /// <summary>Host de DI accesible desde App.axaml.cs para resolver el MainViewModel.</summary>
    public static IHost AppHost { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        Debug.WriteLine("1 - Antes de Host");
        AppHost = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            })
            .ConfigureServices(ConfigureServices)
            .Build();
        Debug.WriteLine("2 - Host construido");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        Debug.WriteLine("3 - Avalonia iniciado");
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        Debug.WriteLine("ConfigureServices llamado");

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IOscMediaService, OscMediaService>();
        services.AddSingleton<IApplicationAudioService, ApplicationAudioService>();
        services.AddSingleton<IMicrophoneService, MicrophoneService>();
        services.AddSingleton<AudioRouterService>();
        services.AddSingleton<IAudioRouterService>(sp => sp.GetRequiredService<AudioRouterService>());
        services.AddSingleton<MainViewModel>();

        Debug.WriteLine("ConfigureServices completado");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
}

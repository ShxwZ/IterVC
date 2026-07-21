using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Desktop.ViewModels;
using IterVC.Desktop.Services;
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
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        services.AddSingleton(sp => new GitHubUpdateService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IAudioRouterService>(),
            sp.GetRequiredService<IMicrophoneService>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<IApplicationAudioService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IOscMediaService>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            sp.GetRequiredService<GitHubUpdateService>()));

        Debug.WriteLine("ConfigureServices completado");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
}

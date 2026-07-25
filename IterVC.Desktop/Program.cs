using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
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
        services.AddSingleton<ILocalizationService>(LocalizationService.Instance);
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IOscMediaService, OscMediaService>();
        services.AddSingleton<IApplicationAudioService, ApplicationAudioService>();
        services.AddSingleton<TextsViewModel>();
        services.AddSingleton(sp => new ApplicationsViewModel(
            sp.GetRequiredService<IApplicationAudioService>(),
            sp.GetRequiredService<IAudioRouterService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<ApplicationsViewModel>>(),
            sp.GetRequiredService<TextsViewModel>()));
        services.AddSingleton(sp => new MicrophoneViewModel(
            sp.GetRequiredService<IAudioRouterService>(),
            sp.GetRequiredService<IMicrophoneService>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<MicrophoneViewModel>>()));
        services.AddSingleton(sp => new NoiseGateViewModel(
            sp.GetRequiredService<IAudioRouterService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<NoiseGateViewModel>>()));
        services.AddSingleton(sp => new AudioRoutingViewModel(
            sp.GetRequiredService<IAudioRouterService>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ApplicationsViewModel>(),
            sp.GetRequiredService<MicrophoneViewModel>(),
            sp.GetRequiredService<NoiseGateViewModel>(),
            sp.GetRequiredService<ILogger<AudioRoutingViewModel>>()));
        services.AddSingleton<IMediaSessionService, WindowsMediaSessionService>();
        services.AddSingleton<IOscChatboxWorker, OscChatboxWorker>();
        services.AddSingleton(sp => new OscChatboxViewModel(
            sp.GetRequiredService<IOscChatboxWorker>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<OscChatboxViewModel>>()));
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton(sp => new HotkeysViewModel(
            sp.GetRequiredService<IGlobalHotkeyService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<TextsViewModel>()));
        services.AddSingleton<IMicrophoneService, MicrophoneService>();
        services.AddSingleton<AudioRouterService>();
        services.AddSingleton<IAudioRouterService>(sp => sp.GetRequiredService<AudioRouterService>());
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        services.AddSingleton<IUpdateService>(sp => new GitHubUpdateService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IExternalUrlLauncher, ShellUrlLauncher>();
        services.AddSingleton(sp => new UpdateViewModel(
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IExternalUrlLauncher>(),
            sp.GetRequiredService<TextsViewModel>(),
            sp.GetRequiredService<ILogger<UpdateViewModel>>()));
        services.AddSingleton(sp => new LanguageViewModel(
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<TextsViewModel>(),
            sp.GetRequiredService<HotkeysViewModel>(),
            sp.GetRequiredService<ApplicationsViewModel>(),
            sp.GetRequiredService<UpdateViewModel>(),
            sp.GetRequiredService<ILogger<LanguageViewModel>>()));
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<LanguageViewModel>(),
            sp.GetRequiredService<HotkeysViewModel>(),
            sp.GetRequiredService<UpdateViewModel>()));
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<AudioRoutingViewModel>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            sp.GetRequiredService<OscChatboxViewModel>(),
            sp.GetRequiredService<SettingsViewModel>()));

        Debug.WriteLine("ConfigureServices completado");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
}

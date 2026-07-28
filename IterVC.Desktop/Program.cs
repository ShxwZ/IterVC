using Avalonia;
using IterVC.Audio;
using IterVC.Core.Interfaces;
using IterVC.Core.Localization;
using IterVC.Desktop.Services;
using IterVC.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop;

internal static class Program
{
    private static RollingFileLoggerProvider? _fileLogger;

    public static IHost AppHost { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IterVC");
        var logsDirectory = Path.Combine(appDataDirectory, "Logs");
        _fileLogger = new RollingFileLoggerProvider(logsDirectory);

        try
        {
            AppHost = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(_fileLogger);
#if DEBUG
                    logging.AddConsole();
#endif
                })
                .ConfigureServices((context, services) =>
                    ConfigureServices(context, services, logsDirectory))
                .Build();

            RegisterGlobalExceptionHandlers();
            _ = AppHost.Services.GetRequiredService<MicrophoneRoutingCoordinator>();
            var logger = AppHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IterVC");
            logger.LogInformation("IterVC {Version} started", AppVersion.Display);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            logger.LogInformation("IterVC shut down cleanly");
        }
        catch (Exception exception)
        {
            TryLogCritical(exception, "IterVC terminated unexpectedly");
            throw;
        }
        finally
        {
            _fileLogger.Flush(TimeSpan.FromSeconds(1));
            AppHost?.Dispose();
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                            ?? new InvalidOperationException(
                                $"Unhandled object: {args.ExceptionObject?.GetType().FullName}");
            TryLogCritical(exception, "Unhandled application exception. Terminating: {IsTerminating}",
                args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            TryLogCritical(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    internal static void TryLogCritical(Exception exception, string message, params object?[] args)
    {
        try
        {
            AppHost?.Services.GetService<ILoggerFactory>()
                ?.CreateLogger("IterVC.GlobalExceptions")
                .LogCritical(exception, message, args);
            _fileLogger?.Flush(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Exception reporting must not hide the original failure.
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services,
        string logsDirectory)
    {
        services.AddSingleton(new DiagnosticsService(logsDirectory));
        services.AddSingleton(sp => new DiagnosticsViewModel(
            sp.GetRequiredService<DiagnosticsService>(),
            sp.GetRequiredService<ILogger<DiagnosticsViewModel>>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILocalizationService>(LocalizationService.Instance);
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IOscMediaService, OscMediaService>();
        services.AddSingleton<IApplicationAudioService, ApplicationAudioService>();
        services.AddSingleton<TextsViewModel>();
        services.AddSingleton<AudioMetersViewModel>();
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
            sp.GetRequiredService<AudioMetersViewModel>(),
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
        services.AddSingleton<IAudioRouterService, AudioRouterService>();
        services.AddSingleton<MicrophoneRoutingCoordinator>();
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
            sp.GetRequiredService<UpdateViewModel>(),
            sp.GetRequiredService<DiagnosticsViewModel>()));
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<IDeviceService>(),
            sp.GetRequiredService<AudioRoutingViewModel>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILogger<MainViewModel>>(),
            sp.GetRequiredService<OscChatboxViewModel>(),
            sp.GetRequiredService<SettingsViewModel>()));
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .WithInterFont();
}

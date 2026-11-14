using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace IterVC.Linux;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This experimental desktop build supports Linux only.");
            return 1;
        }

        if (args.Contains("--self-test"))
        {
            try
            {
                using var backend = new LinuxAudioBackend();
                var help = backend.QueryAsync("--help").GetAwaiter().GetResult();
                if (!help.Contains("IterVC experimental Linux PipeWire prototype", StringComparison.Ordinal))
                    throw new InvalidOperationException("Embedded Rust CLI failed its smoke test.");
                Console.WriteLine("Linux GUI and embedded Rust audio backend: OK");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DesktopApp>().UsePlatformDetect().LogToTrace().WithInterFont();
}

internal sealed class DesktopApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

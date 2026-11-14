using Avalonia;
using System.Diagnostics;
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

        var shortcut = args.FirstOrDefault(arg => arg is "--toggle-routing" or "--toggle-monitor");
        if (shortcut is not null)
        {
            try
            {
                ShortcutControlServer.SendAsync(shortcut[2..]).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Could not send Linux shortcut: " + error.Message);
                return 1;
            }
        }

        if (args.Contains("--self-test"))
        {
            try
            {
                using var backend = new LinuxAudioBackend();
                var help = backend.QueryAsync("--help").GetAwaiter().GetResult();
                if (!help.Contains("IterVC experimental Linux PipeWire prototype", StringComparison.Ordinal))
                    throw new InvalidOperationException("Embedded Rust CLI failed its smoke test.");
                var path = Path.Combine(Path.GetTempPath(),
                    $"itervc-shortcut-selftest-{Guid.NewGuid():N}", "shortcuts.sock");
                var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (var shortcuts = new ShortcutControlServer(command => received.TrySetResult(command), path))
                {
                    using var client = new Process
                    {
                        StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
                        {
                            UseShellExecute = false,
                            RedirectStandardError = true
                        }
                    };
                    client.StartInfo.ArgumentList.Add("--toggle-routing");
                    client.StartInfo.Environment["ITERVC_CONTROL_SOCKET"] = path;
                    if (!client.Start())
                        throw new InvalidOperationException("Could not invoke the shortcut command.");
                    client.WaitForExit(3000);
                    if (!client.HasExited || client.ExitCode != 0)
                        throw new InvalidOperationException("Shortcut command process failed.");
                    var actual = received.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                    if (actual != "toggle-routing")
                        throw new InvalidOperationException("Linux shortcut IPC self-test failed.");
                }
                Console.WriteLine("Linux GUI, Rust backend and user-local shortcut transport: OK");
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

using Avalonia;
using System.Diagnostics;
using System.Text;
using IterVC.Core.Settings;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        var shortcut = args.FirstOrDefault(arg => arg is
            "--toggle-routing" or "--start-routing" or "--stop-routing"
            or "--toggle-monitor" or "--toggle-microphone" or "--toggle-applications-mute"
            or "--media-play-pause" or "--media-next" or "--media-previous"
            or "--show-window" or "--exit");
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
                    var permissions = File.GetUnixFileMode(path);
                    if ((permissions & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
                        throw new InvalidOperationException("Linux shortcut socket permissions are too broad.");
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
                var settingsDirectory = Path.Combine(Path.GetTempPath(),
                    $"itervc-linux-settings-selftest-{Guid.NewGuid():N}");
                try
                {
                    var store = new LinuxSettingsStore(Path.Combine(settingsDirectory, "settings.json"));
                    store.Save(new AppSettings {
                        MicrophoneDeviceId = "alsa_input.test",
                        OutputDeviceId = "alsa_output.test",
                        MonitorMicrophone = true
                    });
                    var loaded = store.Load();
                    if (loaded.MicrophoneDeviceId != "alsa_input.test" ||
                        loaded.OutputDeviceId != "alsa_output.test" || !loaded.MonitorMicrophone)
                        throw new InvalidOperationException("Linux settings round-trip test failed.");
                    File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), "{invalid");
                    if (store.Load().MicrophoneDeviceId is not null)
                        throw new InvalidOperationException("Corrupted settings did not recover safely.");
                }
                finally
                {
                    if (Directory.Exists(settingsDirectory))
                        Directory.Delete(settingsDirectory, recursive: true);
                }
                // PulseAudio sink-input indices can legitimately start at 0.
                // The catalogue must retain that stream but hide IterVC's own
                // playback to avoid selecting a feedback loop.
                var appsJson = "[{\"index\":0,\"properties\":{\"application.name\":\"Music Player\"}},"
                    + "{\"index\":42,\"properties\":{\"application.name\":\"Voice Chat\"}},"
                    + "{\"index\":43,\"properties\":{\"application.name\":\"IterVC Rust Microphone\"}}]";
                var parsedApps = ApplicationStreamCatalog.Parse(appsJson);
                if (parsedApps.Length != 2 || parsedApps[0].Id != 0 ||
                    parsedApps[1].Name != "Voice Chat")
                    throw new InvalidOperationException("Linux application stream parsing failed.");
                // A stale saved stream must not be forwarded to Rust at startup.
                // Resolve by name and ignore apps that have stopped playing.
                var activeSelection = ApplicationStreamCatalog.SelectActive(appsJson,
                    new[] { "voice chat", "PreviouslyClosedPlayer" });
                if (activeSelection.Length != 1 || activeSelection[0].Id != 42)
                    throw new InvalidOperationException("Stale application selection was not filtered.");
                // Theme contract: the Linux preview renders the original
                // desktop semantic palette instead of inventing new colors.
                LinuxTheme.Validate();
                var packet = LinuxOscChatboxService.EncodeChatbox("IterVC test");
                var text = Encoding.UTF8.GetString(packet);
                if (packet.Length % 4 != 0 || !text.Contains("/chatbox/input") ||
                    !text.Contains(",sTF") || !text.Contains("IterVC test"))
                    throw new InvalidOperationException("VRChat OSC message encoding failed.");
                Console.WriteLine("Linux GUI, Rust audio, shortcuts, OSC and shared settings: OK");
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
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://IterVC.Linux/Assets/icon.ico"));
                var tray = new TrayIcon {
                    Icon = new WindowIcon(new Bitmap(stream)),
                    ToolTipText = "IterVC Linux"
                };
                var show = new NativeMenuItem("Mostrar IterVC / Show IterVC");
                show.Click += (_, _) => window.RestoreFromTray();
                var exit = new NativeMenuItem("Salir / Exit");
                exit.Click += (_, _) => window.Close();
                tray.Clicked += (_, _) => window.RestoreFromTray();
                tray.Menu = new NativeMenu { show, new NativeMenuItemSeparator(), exit };
                TrayIcon.SetIcons(this, new TrayIcons { tray });
                window.TrayConfigured = true;
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Tray icon unavailable: " + error.Message);
                window.TrayConfigured = false;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

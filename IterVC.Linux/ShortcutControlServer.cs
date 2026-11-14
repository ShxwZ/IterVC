using System.Net.Sockets;
using System.Text;

namespace IterVC.Linux;

/// <summary>
/// User-local Unix socket for shortcuts configured through desktop settings.
/// Works with both X11 and Wayland compositors; no keyboard interception.
/// </summary>
internal sealed class ShortcutControlServer : IDisposable
{
    private readonly string _socketPath;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _loop;

    public static string DefaultSocketPath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("ITERVC_CONTROL_SOCKET");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath;
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            var baseDirectory = !string.IsNullOrWhiteSpace(runtime) && Directory.Exists(runtime)
                ? runtime
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDirectory, "IterVC", "shortcuts.sock");
        }
    }

    public ShortcutControlServer(Action<string> commandReceived, string? socketPath = null)
    {
        _socketPath = socketPath ?? DefaultSocketPath;
        var directory = Path.GetDirectoryName(_socketPath)
            ?? throw new InvalidOperationException("Invalid control socket path.");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (File.Exists(_socketPath))
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                probe.Connect(new UnixDomainSocketEndPoint(_socketPath));
                throw new InvalidOperationException(
                    "Another IterVC Linux desktop window is already running.");
            }
            catch (SocketException)
            {
                // Previous instance was interrupted before cleaning its socket.
                File.Delete(_socketPath);
            }
        }

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(4);
        _loop = Task.Run(async () =>
        {
            while (!_cancel.IsCancellationRequested)
            {
                Socket client;
                try { client = await _listener.AcceptAsync(_cancel.Token); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                using (client)
                using (var stream = new NetworkStream(client, ownsSocket: false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    using var messageTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        var command = await reader.ReadLineAsync(messageTimeout.Token);
                        if (command is "toggle-routing" or "start-routing" or "stop-routing"
                            or "toggle-monitor" or "toggle-microphone" or "toggle-applications-mute"
                            or "media-play-pause" or "media-next" or "media-previous" or "show-window")
                            commandReceived(command);
                    }
                    catch (OperationCanceledException) { /* Unresponsive shortcut client */ }
                }
            }
        });
    }

    public static async Task SendAsync(string command, string? socketPath = null)
    {
        if (command is not ("toggle-routing" or "start-routing" or "stop-routing"
            or "toggle-monitor" or "toggle-microphone" or "toggle-applications-mute"
            or "media-play-pause" or "media-next" or "media-previous" or "show-window"))
            throw new ArgumentException("Unknown shortcut command.", nameof(command));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath ?? DefaultSocketPath),
            timeout.Token);
        using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), timeout.Token);
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _listener.Dispose();
        try { _loop.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);
        _cancel.Dispose();
    }
}

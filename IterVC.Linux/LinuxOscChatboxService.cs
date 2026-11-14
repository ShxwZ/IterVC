using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IterVC.Linux;

/// <summary>
/// Linux VRChat OSC chatbox integration. The OSC encoding is portable and
/// sends UDP packets to the local VRChat client; MPRIS media metadata is
/// obtained using playerctl when present. No Windows media APIs are needed.
/// </summary>
internal sealed class LinuxOscChatboxService : IDisposable
{
    private readonly UdpClient _client = new();
    private readonly IPEndPoint _target = new(IPAddress.Loopback, 9000);
    private readonly Action<string>? _log;
    private CancellationTokenSource? _stop;
    private Task? _task;
    private string _lastMessage = string.Empty;
    private int _disposed;

    public LinuxOscChatboxService(Action<string>? log = null) => _log = log;

    private static void AppendString(List<byte> output, string text)
    {
        output.AddRange(Encoding.UTF8.GetBytes(text));
        output.Add(0);
        while (output.Count % 4 != 0)
            output.Add(0);
    }

    /// <summary>
    /// OSC /chatbox/input: string message, bool send=true, notify=false.
    /// The booleans are encoded by typetags, without payload bytes.
    /// </summary>
    internal static byte[] EncodeChatbox(string message)
    {
        var payload = new List<byte>(message.Length + 64);
        AppendString(payload, "/chatbox/input");
        AppendString(payload, ",sTF");
        AppendString(payload, message);
        return payload.ToArray();
    }

    internal async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var packet = EncodeChatbox(message);
        await _client.SendAsync(packet, _target, cancellationToken);
    }

    public void SetEnabled(bool enabled, Func<string> template)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        if (!enabled)
        {
            Stop();
            return;
        }
        if (_task is { IsCompleted: false })
            return;
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _task = Task.Run(() => RunAsync(template, token));
    }

    private static async Task<string?> RunPlayerctlAsync(
        CancellationToken token, params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("playerctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }};
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start())
            return null;
        var result = process.StandardOutput.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                return null;
            return (await result).Trim();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return null;
        }
    }

    private async Task RunAsync(Func<string> template, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                string? title;
                string? status;
                try
                {
                    title = await RunPlayerctlAsync(token, "metadata", "title");
                    status = await RunPlayerctlAsync(token, "status");
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    _log?.Invoke("playerctl is not installed. Media OSC is unavailable.");
                    break;
                }
                if (!string.IsNullOrWhiteSpace(title))
                {
                    var message = template()
                        .Replace("{title}", title, StringComparison.Ordinal)
                        .Replace("{status}", status ?? "Unknown", StringComparison.Ordinal);
                    if (message.Length > 144)
                        message = message[..144];
                    if (message != _lastMessage)
                    {
                        await SendAsync(message, token);
                        _lastMessage = message;
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _log?.Invoke("Media OSC stopped: " + error.Message);
        }
    }

    public void Stop()
    {
        _stop?.Cancel();
        var task = _task;
        _task = null;
        if (task is not null)
        {
            try { task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        }
        _stop?.Dispose();
        _stop = null;
        _lastMessage = string.Empty;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Stop();
        _client.Dispose();
    }
}

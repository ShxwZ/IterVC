using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace IterVC.Linux;

/// <summary>
/// Isolated Linux backend runner. Extracts the Rust executable from a single-file
/// desktop publish and terminates it gracefully to allow PipeWire module cleanup.
/// </summary>
internal sealed class LinuxAudioBackend : IDisposable
{
    private readonly string _directory;
    private readonly string _executable;
    private Process? _session;
    private readonly SemaphoreSlim _gainWrite = new(1, 1);
    private bool _disposed;

    public LinuxAudioBackend()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"itervc-linux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _executable = Path.Combine(_directory, "itervc-linux-pipewire");
        using var input = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IterVC.Linux.NativeAudioCli")
            ?? throw new InvalidOperationException("The Rust audio backend is not embedded.");
        using (var output = File.Create(_executable))
            input.CopyTo(output);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
            File.SetUnixFileMode(_executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    public event Action<double>? LevelChanged;
    public event Action<string>? MessageReceived;
    public event Action? SessionExited;

    public async Task<string> QueryAsync(string option, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var process = new Process { StartInfo = MakeStartInfo(option) };
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        if (!process.Start())
            throw new InvalidOperationException("Could not start embedded Rust backend.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        var text = await stdout;
        if (process.ExitCode != 0)
            throw new InvalidOperationException((await stderr).Trim());
        return text;
    }

    public void Start(string microphone, string? monitoringOutput,
        IReadOnlyCollection<uint>? applicationStreamIds = null, double appVolume = 1.0,
        double micVolume = 1.0, bool gateEnabled = false, double gateThresholdDb = -45,
        bool includeApplicationCapture = false)
    {
        ThrowIfDisposed();
        if (_session is { HasExited: false })
            throw new InvalidOperationException("An audio session is already running.");
        _session?.Dispose();
        var process = new Process { StartInfo = MakeStartInfo("--input", microphone,
            "--seconds", "0") };
        if (!string.IsNullOrWhiteSpace(monitoringOutput))
        {
            process.StartInfo.ArgumentList.Add("--monitor-output");
            process.StartInfo.ArgumentList.Add(monitoringOutput);
        }
        if (includeApplicationCapture || applicationStreamIds is { Count: > 0 })
        {
            process.StartInfo.ArgumentList.Add("--include-apps");
            if (!double.IsFinite(appVolume) || appVolume < 0 || appVolume > 2)
                throw new ArgumentOutOfRangeException(nameof(appVolume));
            foreach (var streamId in applicationStreamIds?.Distinct() ?? Enumerable.Empty<uint>())
            {
                process.StartInfo.ArgumentList.Add("--app-stream");
                process.StartInfo.ArgumentList.Add(streamId.ToString(CultureInfo.InvariantCulture));
            }
            process.StartInfo.ArgumentList.Add("--app-volume");
            process.StartInfo.ArgumentList.Add(appVolume.ToString("0.00", CultureInfo.InvariantCulture));
        }
        if (!double.IsFinite(micVolume) || micVolume < 0 || micVolume > 3)
            throw new ArgumentOutOfRangeException(nameof(micVolume));
        if (!double.IsFinite(gateThresholdDb) || gateThresholdDb < -100 || gateThresholdDb > 0)
            throw new ArgumentOutOfRangeException(nameof(gateThresholdDb));
        if (micVolume != 1.0)
        {
            process.StartInfo.ArgumentList.Add("--mic-volume");
            process.StartInfo.ArgumentList.Add(micVolume.ToString("0.00", CultureInfo.InvariantCulture));
        }
        if (gateEnabled)
        {
            process.StartInfo.ArgumentList.Add("--noise-gate");
            process.StartInfo.ArgumentList.Add("--gate-threshold");
            process.StartInfo.ArgumentList.Add(gateThresholdDb.ToString("0.0", CultureInfo.InvariantCulture));
        }
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;
        process.EnableRaisingEvents = true;
        process.ErrorDataReceived += (_, args) =>
        {
            var line = args.Data;
            if (line is null || !ReferenceEquals(Volatile.Read(ref _session), process))
                return;
            if (line.StartsWith("ITERVC_LEVEL:", StringComparison.Ordinal) &&
                double.TryParse(line.AsSpan("ITERVC_LEVEL:".Length),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var rms))
                LevelChanged?.Invoke(rms);
            else
                MessageReceived?.Invoke(line);
        };
        process.Exited += (_, _) =>
        {
            // A previous worker may finish after a rapid stop/start. Never
            // report its exit as the termination of the new audio session.
            if (ReferenceEquals(Volatile.Read(ref _session), process))
                SessionExited?.Invoke();
        };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start Linux microphone process.");
            _session = process;
            process.BeginErrorReadLine();
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public Task SetMicrophoneVolumeAsync(double gain) =>
        SetLiveVolumeAsync("mic-volume", gain, 3);

    public Task SetApplicationVolumeAsync(double gain) =>
        SetLiveVolumeAsync("app-volume", gain, 2);

    public async Task AddApplicationStreamAsync(uint streamId)
    {
        ThrowIfDisposed();
        await _gainWrite.WaitAsync().ConfigureAwait(false);
        try
        {
            var process = _session;
            if (process is null || process.HasExited)
                throw new InvalidOperationException("The Linux microphone session is no longer running.");
            await process.StandardInput.WriteLineAsync(
                "add-app " + streamId.ToString(CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally { _gainWrite.Release(); }
    }

    private async Task SetLiveVolumeAsync(string target, double gain, double maximum)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(gain) || gain < 0 || gain > maximum)
            throw new ArgumentOutOfRangeException(nameof(gain));
        await _gainWrite.WaitAsync().ConfigureAwait(false);
        try
        {
            var process = _session;
            if (process is null || process.HasExited)
                throw new InvalidOperationException("The Linux microphone session is no longer running.");
            await process.StandardInput.WriteLineAsync(
                target + " " + gain.ToString("0.00000", CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally { _gainWrite.Release(); }
    }

    public async Task StopAsync()
    {
        var process = _session;
        if (process is null || process.HasExited)
            return;

        // SIGTERM allows the Rust process to remove its temporary PipeWire
        // source and sink; Process.Kill() alone would bypass its cleanup.
        using var term = new Process { StartInfo = new ProcessStartInfo("/bin/kill") {
            UseShellExecute = false, CreateNoWindow = true
        }};
        term.StartInfo.ArgumentList.Add("-TERM");
        term.StartInfo.ArgumentList.Add(process.Id.ToString(CultureInfo.InvariantCulture));
        try
        {
            if (!term.Start())
                throw new InvalidOperationException("Failed to request graceful Linux audio shutdown.");
            await term.WaitForExitAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception error) when (error is OperationCanceledException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A hung or unexpectedly stopped worker must not block app exit.
            // Always reap the child after a forced shutdown; otherwise a quick
            // restart can race against stale processes and temporary modules.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            MessageReceived?.Invoke("PipeWire shutdown required a forced cleanup: " + error.Message);
        }
    }

    private ProcessStartInfo MakeStartInfo(params string[] args)
    {
        var info = new ProcessStartInfo(_executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _directory
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync().GetAwaiter().GetResult();
        _session?.Dispose();
        _gainWrite.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* Cleanup may be deferred by the OS. */ }
        _disposed = true;
    }
}

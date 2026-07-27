using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    internal const long MaximumFileSizeBytes = 5 * 1024 * 1024;
    internal const int RetainedFileCount = 10;

    private readonly string _logsDirectory;
    private readonly Channel<string> _entries;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _writerTask;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new();
    private int _disposed;

    public RollingFileLoggerProvider(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
        _entries = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(WriteEntriesAsync);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, category => new RollingFileLogger(this, category));

    internal bool IsEnabled(LogLevel level) =>
        level != LogLevel.None && level >= LogLevel.Information;

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsEnabled(level)) return;

        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var eventSuffix = eventId.Id == 0 ? string.Empty : $" [{eventId.Id}]";
        var entry = $"{timestamp} [{level}] {category}{eventSuffix}: {message}{Environment.NewLine}";
        if (exception is not null) entry += exception + Environment.NewLine;
        _entries.Writer.TryWrite(entry);
    }

    private async Task WriteEntriesAsync()
    {
        StreamWriter? writer = null;
        string? activePath = null;
        try
        {
            Directory.CreateDirectory(_logsDirectory);
            DeleteExpiredFiles();

            await foreach (var entry in _entries.Reader.ReadAllAsync(_stopping.Token))
            {
                try
                {
                    var targetPath = GetTargetPath();
                    if (!string.Equals(activePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (writer is not null) await writer.DisposeAsync();
                        writer = new StreamWriter(new FileStream(
                            targetPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        {
                            AutoFlush = true
                        };
                        activePath = targetPath;
                        DeleteExpiredFiles();
                    }

                    await writer!.WriteAsync(entry);
                }
                catch
                {
                    if (writer is not null)
                    {
                        try { await writer.DisposeAsync(); } catch { }
                        writer = null;
                        activePath = null;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch
        {
            // Logging must never interrupt application startup or shutdown.
        }
        finally
        {
            if (writer is not null)
                try { await writer.DisposeAsync(); } catch { }
        }
    }

    private string GetTargetPath()
    {
        var baseName = $"itervc-{DateTime.Now:yyyy-MM-dd}";
        var path = Path.Combine(_logsDirectory, baseName + ".log");
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileSizeBytes) return path;

        for (var index = 1; ; index++)
        {
            path = Path.Combine(_logsDirectory, $"{baseName}-{index:000}.log");
            if (!File.Exists(path) || new FileInfo(path).Length < MaximumFileSizeBytes) return path;
        }
    }

    private void DeleteExpiredFiles()
    {
        try
        {
            foreach (var file in new DirectoryInfo(_logsDirectory)
                         .EnumerateFiles("itervc-*.log")
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(RetainedFileCount))
                try { file.Delete(); } catch { }
        }
        catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _entries.Writer.TryComplete();
        try
        {
            if (!_writerTask.Wait(TimeSpan.FromSeconds(2)))
                _stopping.Cancel();
        }
        catch { }
        _stopping.Dispose();
    }

    public void Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_entries.Reader.Count > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
    }

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try { provider.Write(logLevel, category, eventId, formatter(state, exception), exception); }
            catch { }
        }
    }
}

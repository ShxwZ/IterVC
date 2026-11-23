using System.IO.Pipes;
using System.Text;

namespace IterVC.Desktop.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\IterVC.Desktop";
    private const string PipeName = "IterVC.Desktop.Activation";
    private const string OpenMainWindow = "OpenMainWindow";
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listener;

    private SingleInstanceCoordinator(Mutex mutex, bool ownsMutex) { _mutex = mutex; _ownsMutex = ownsMutex; }
    internal bool IsPrimary => _ownsMutex;
    internal static SingleInstanceCoordinator Acquire() { var mutex = new Mutex(true, MutexName, out var created); return new SingleInstanceCoordinator(mutex, created); }
    internal async Task NotifyPrimaryAsync(CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        await pipe.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
        var payload = Encoding.UTF8.GetBytes(OpenMainWindow);
        await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }
    internal void StartListener(Action activate)
    {
        if (!IsPrimary || _listener is not null) return;
        _listenerCancellation = new CancellationTokenSource();
        _listener = ListenAsync(activate, _listenerCancellation.Token);
    }
    private static async Task ListenAsync(Action activate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[OpenMainWindow.Length + 1];
                var count = await pipe.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == OpenMainWindow.Length && Encoding.UTF8.GetString(buffer, 0, count) == OpenMainWindow) activate();
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { }
        }
    }
    public void Dispose() { _listenerCancellation?.Cancel(); if (_ownsMutex) _mutex.ReleaseMutex(); _mutex.Dispose(); _listenerCancellation?.Dispose(); }
}

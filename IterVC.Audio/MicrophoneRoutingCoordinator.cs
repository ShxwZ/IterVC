using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Audio;

public sealed class MicrophoneRoutingCoordinator : IDisposable
{
    private readonly IMicrophoneService _microphone;
    private readonly IAudioRouterService _router;
    private int _disposed;

    public MicrophoneRoutingCoordinator(IMicrophoneService microphone, IAudioRouterService router)
    {
        _microphone = microphone;
        _router = router;
        _microphone.DataAvailable += OnDataAvailable;
    }

    private void OnDataAvailable(object? sender, AudioDataEventArgs data) =>
        _router.FeedMicrophoneSamples(data.Buffer, data.Count);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _microphone.DataAvailable -= OnDataAvailable;
    }
}

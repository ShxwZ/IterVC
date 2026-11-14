using IterVC.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

internal sealed class OscChatboxWorker : IOscChatboxWorker
{
    private const string WavePattern = "▁▂▄▆█▆▄▂▁▂▄▆█▆▄";

    private readonly IMediaSessionService _mediaSessions;
    private readonly IOscMediaService _oscMedia;
    private readonly ILogger<OscChatboxWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _stopping;
    private Task? _runTask;
    private volatile bool _enabled;
    private string _template = "{title} - [{time}]";
    private int _waveFrameIndex;

    public OscChatboxWorker(IMediaSessionService mediaSessions, IOscMediaService oscMedia,
        ILogger<OscChatboxWorker> logger) : this(mediaSessions, oscMedia, logger, TimeSpan.FromSeconds(1)) { }

    internal OscChatboxWorker(IMediaSessionService mediaSessions, IOscMediaService oscMedia,
        ILogger<OscChatboxWorker> logger, TimeSpan interval)
    {
        _mediaSessions = mediaSessions;
        _oscMedia = oscMedia;
        _logger = logger;
        _interval = interval;
    }

    public void Configure(bool enabled, string template)
    {
        _template = template;
        _enabled = enabled;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_runTask is { IsCompleted: false }) return Task.CompletedTask;
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(_stopping.Token);
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? runTask;
        lock (_gate) { _stopping?.Cancel(); runTask = _runTask; }
        if (runTask is not null) await runTask.ConfigureAwait(false);
        lock (_gate)
        {
            _stopping?.Dispose();
            _stopping = null;
            _runTask = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_enabled)
                {
                    var media = await _mediaSessions.GetActiveMediaInfoAsync(cancellationToken).ConfigureAwait(false);
                    if (media is { Title.Length: > 0 })
                    {
                        var message = _template
                            .Replace("{title}", media.Title)
                            .Replace("{time}", media.TimeInfo)
                            .Replace("{wave}", NextWaveFrame());
                        _oscMedia.SendMediaInfo(message);
                    }
                    else
                    {
                        var message = _template
                            .Replace("{title}", "⏹️")
                            .Replace("{time}", "00:00 / 00:00")
                            .Replace("{wave}", NextWaveFrame());
                        _oscMedia.SendMediaInfo(message);
                    }
                }
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "OSC chatbox worker stopped unexpectedly"); }
    }

    private string NextWaveFrame()
    {
        var frame = WavePattern[_waveFrameIndex..] + WavePattern[.._waveFrameIndex];
        _waveFrameIndex = (_waveFrameIndex + 1) % WavePattern.Length;
        return frame;
    }
}

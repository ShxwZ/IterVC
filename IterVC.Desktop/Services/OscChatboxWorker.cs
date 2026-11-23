using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using Microsoft.Extensions.Logging;

namespace IterVC.Desktop.Services;

internal sealed class OscChatboxWorker : IOscChatboxWorker
{
    private const string WavePattern = "▁▂▄▆█▆▄▂▁▂▄▆█▆▄";
    private const string StoppedTitle = "⏹️";
    private const string StoppedTime = "00:00 / 00:00";

    private sealed record ChatboxOptions(bool Enabled, string Template, bool KeepVisibleWhenPaused);

    private readonly IMediaSessionService _mediaSessions;
    private readonly IOscMediaService _oscMedia;
    private readonly ILogger<OscChatboxWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _stopping;
    private Task? _runTask;
    private ChatboxOptions _options = new(false, "{title}", false);
    private bool _messageVisible;
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

    public void Configure(bool enabled, string template, bool keepVisibleWhenPaused)
    {
        Volatile.Write(ref _options, new ChatboxOptions(enabled, template, keepVisibleWhenPaused));
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
                var options = Volatile.Read(ref _options);
                if (options.Enabled)
                {
                    var media = await _mediaSessions.GetActiveMediaInfoAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);

                    if (hasMedia && media!.IsPaused && !options.KeepVisibleWhenPaused)
                        ClearIfVisible();
                    else
                    {
                        _oscMedia.SendMessage(FormatMessage(media, options.Template));
                        _messageVisible = true;
                    }
                }
                else
                    ClearIfVisible();

                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "OSC chatbox worker stopped unexpectedly"); }
    }

    private string FormatMessage(MediaInfo? media, string template)
    {
        var hasMedia = media is not null && !string.IsNullOrWhiteSpace(media.Title);
        var title = !hasMedia ? StoppedTitle :
            media!.IsPaused ? media.Title + " ⏸️" : media.Title;

        return template
            .Replace("{title}", title)
            .Replace("{time}", hasMedia ? media!.TimeInfo : StoppedTime)
            .Replace("{status}", string.Empty) // Backwards compatibility for imported templates.
            .Replace("{wave}", NextWaveFrame());
    }

    private void ClearIfVisible()
    {
        if (!_messageVisible) return;
        _oscMedia.ClearChatbox();
        _messageVisible = false;
    }

    private string NextWaveFrame()
    {
        var frame = WavePattern[_waveFrameIndex..] + WavePattern[.._waveFrameIndex];
        _waveFrameIndex = (_waveFrameIndex + 1) % WavePattern.Length;
        return frame;
    }
}

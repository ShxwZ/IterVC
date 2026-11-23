using IterVC.Core.Interfaces;
using IterVC.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.Media.Control;

namespace IterVC.Desktop.Services;

internal sealed class WindowsMediaSessionService(ILogger<WindowsMediaSessionService> logger) : IMediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var sessions = _manager.GetSessions();
            var activeSession = sessions.FirstOrDefault(session =>
                session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                ?? sessions.FirstOrDefault(session =>
                    session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            if (activeSession is null) return null;
            var isPaused = activeSession.GetPlaybackInfo()?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;

            var properties = await activeSession.TryGetMediaPropertiesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (properties is null) return null;

            var timeline = activeSession.GetTimelineProperties();
            var position = timeline is null
                ? TimeSpan.Zero
                : GetPlaybackPosition(timeline.Position, timeline.EndTime,
                    timeline.LastUpdatedTime, DateTimeOffset.UtcNow, isPaused);

            return new MediaInfo
            {
                Title = FormatTitle(properties.Artist, properties.Title),
                TimeInfo = timeline is null ? "00:00 / 00:00" : FormatTime(position, timeline.EndTime),
                IsPaused = isPaused
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to read the active Windows media session");
            return null;
        }
    }

    internal static TimeSpan GetPlaybackPosition(
        TimeSpan position, TimeSpan end, DateTimeOffset lastUpdated, DateTimeOffset now, bool isPaused) =>
        isPaused ? position : EstimatePosition(position, end, lastUpdated, now);

    internal static TimeSpan EstimatePosition(
        TimeSpan position,
        TimeSpan end,
        DateTimeOffset lastUpdated,
        DateTimeOffset now)
    {
        var elapsed = now - lastUpdated;
        if (elapsed > TimeSpan.Zero)
            position += elapsed;
        return position > end ? end : position;
    }

    internal static string FormatTitle(string? artist, string? title) =>
        !string.IsNullOrWhiteSpace(artist) ? $"{artist} - {title}" : title ?? string.Empty;

    internal static string FormatTime(TimeSpan position, TimeSpan end) =>
        $"{(int)position.TotalMinutes:D2}:{position.Seconds:D2} / {(int)end.TotalMinutes:D2}:{end.Seconds:D2}";
}

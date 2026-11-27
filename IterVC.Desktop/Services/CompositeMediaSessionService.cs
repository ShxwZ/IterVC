using IterVC.Core.Interfaces;
using IterVC.Core.Models;

namespace IterVC.Desktop.Services;

internal sealed class CompositeMediaSessionService(IMediaSessionService windows, IMediaSessionService aimp)
    : IMediaSessionService
{
    public async Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default)
    {
        var windowsMedia = await windows.GetActiveMediaInfoAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Preserve the existing Windows provider when it has a valid playing track.
        if (HasTitle(windowsMedia) && !windowsMedia!.IsPaused) return windowsMedia;

        var aimpMedia = await aimp.GetActiveMediaInfoAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (HasTitle(aimpMedia) && !aimpMedia!.IsPaused) return aimpMedia;

        // A paused Windows session must not hide a playing AIMP session.
        return HasTitle(windowsMedia) ? windowsMedia : HasTitle(aimpMedia) ? aimpMedia : null;
    }

    private static bool HasTitle(MediaInfo? media) => !string.IsNullOrWhiteSpace(media?.Title);
}

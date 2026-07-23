using IterVC.Core.Models;

namespace IterVC.Core.Interfaces;

public interface IMediaSessionService
{
    Task<MediaInfo?> GetActiveMediaInfoAsync(CancellationToken cancellationToken = default);
}

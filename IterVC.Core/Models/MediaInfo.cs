namespace IterVC.Core.Models;


public sealed record MediaInfo
{
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required string TimeInfo { get; init; }

}

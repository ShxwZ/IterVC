namespace IterVC.Desktop.Services;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    bool TryValidateReleaseUrl(string? value, out string url);
}

public readonly record struct UpdateCheckResult(bool Success, string? Version, string? ReleaseUrl)
{
    public static UpdateCheckResult Failed => new(false, null, null);
}

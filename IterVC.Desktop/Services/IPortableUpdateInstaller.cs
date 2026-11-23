namespace IterVC.Desktop.Services;

/// <summary>Installs a verified, explicit user-requested portable update.</summary>
public interface IPortableUpdateInstaller
{
    Task<bool> DownloadAndLaunchAsync(string version, CancellationToken cancellationToken = default);
}

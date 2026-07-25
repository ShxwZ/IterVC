using System.Diagnostics;

namespace IterVC.Desktop.Services;

internal sealed class ShellUrlLauncher : IExternalUrlLauncher
{
    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

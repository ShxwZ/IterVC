using System.Diagnostics;

namespace IterVC.Desktop.Services;

internal sealed class DiagnosticsService
{
    public DiagnosticsService(string logsDirectory) => LogsDirectory = logsDirectory;

    public string LogsDirectory { get; }

    public void OpenLogFolder()
    {
        Directory.CreateDirectory(LogsDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = LogsDirectory,
            UseShellExecute = true
        });
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace IterVC.Desktop.Services;

/// <summary>
/// An updater mode executed by the verified NEW portable binary from a temporary directory.
/// It starts before the old process exits and waits before replacing its executable.
/// </summary>
internal static class PortableUpdateBootstrap
{
    internal const string Command = "--apply-portable-update";

    internal static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != Command) return false;
        string? originalExecutable = args.Length > 1 ? args[1] : null;
        try
        {
            if (args.Length != 5) throw new ArgumentException("Invalid update arguments");
            Apply(args[1], args[2], args[3], args[4]);
        }
        catch (Exception error)
        {
            ReportFailure(error);
            if (originalExecutable is not null && File.Exists(originalExecutable))
            {
                try { Process.Start(new ProcessStartInfo(originalExecutable) { UseShellExecute = false }); }
                catch { /* The user can still launch the existing portable app manually. */ }
            }
        }
        return true;
    }

    private static void Apply(string originalPath, string stagedPath, string processId, string expectedHash)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var original = Path.GetFullPath(originalPath);
        var staged = Path.GetFullPath(stagedPath);
        if (!string.Equals(Path.GetFileName(original), "IterVC.exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(original), Path.GetDirectoryName(staged), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(original, staged, StringComparison.OrdinalIgnoreCase) ||
            expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit) ||
            !int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
            throw new ArgumentException("Invalid update target");

        if (!WaitForProcessExit(pid, TimeSpan.FromSeconds(90)))
            throw new TimeoutException("IterVC did not close in time");

        using (var file = File.OpenRead(staged))
        {
            var actual = Convert.ToHexString(SHA256.HashData(file));
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged executable was changed");
        }

        // File.Replace is atomic on supported Windows filesystems. The backup remains for recovery.
        var backup = Path.Combine(Path.GetDirectoryName(original)!,
            "IterVC.previous-" + Guid.NewGuid().ToString("N") + ".exe");
        var replaced = false;
        try
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Replace(staged, original, backup);
                    replaced = true;
                    break;
                }
                catch (IOException) when (attempt != 19)
                {
                    Thread.Sleep(500);
                }
            }
            if (!replaced) throw new IOException("Could not replace the portable executable");
            using var restarted = Process.Start(new ProcessStartInfo(original)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(original)!
            });
            if (restarted is null) throw new InvalidOperationException("Restart failed");
        }
        catch
        {
            if (replaced)
            {
                // Restore the previous version if launching the replacement failed.
                try { File.Replace(backup, original, null); } catch { }
            }
            throw;
        }
    }

    private static bool WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException) { return true; }
    }

    private static void ReportFailure(Exception error)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IterVC");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-update-error.txt"),
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine + error);
        }
        catch { /* Diagnostics must not mask the update error. */ }
    }
}

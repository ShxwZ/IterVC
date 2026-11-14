using System.Text;

namespace IterVC.Linux;

/// <summary>
/// Freedesktop user-session autostart, never sudo/system-wide registration.
/// Kept separate from Windows' startup registration service.
/// </summary>
internal static class LinuxAutoStart
{
    internal static string DesktopFilePath(string? configHome = null)
    {
        var root = configHome ??
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, "autostart", "IterVC.desktop");
    }

    internal static bool IsRegistered(string? configHome = null) =>
        File.Exists(DesktopFilePath(configHome));

    internal static void SetRegistered(bool enabled, string? binaryPath = null,
        string? configHome = null)
    {
        var path = DesktopFilePath(configHome);
        if (!enabled)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var executable = Path.GetFullPath(binaryPath ??
            Environment.ProcessPath ?? throw new InvalidOperationException(
                "Cannot determine the executable path for autostart."));
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new FileNotFoundException("The IterVC executable does not exist.", executable);
        if (executable.Any(char.IsControl))
            throw new ArgumentException("Executable path contains control characters.", nameof(binaryPath));
        // Freedesktop Exec field quoting: escape backslash, double quote,
        // backtick and dollar. A literal '%' requires doubling.
        var escaped = executable.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("`", "\\`")
            .Replace("$", "\\$")
            .Replace("%", "%%");
        var text = "[Desktop Entry]\nType=Application\nName=IterVC\n"
            + "Comment=IterVC PipeWire audio mixer\n"
            + $"Exec=\"{escaped}\"\n"
            + "Terminal=false\n"
            + "X-GNOME-Autostart-enabled=true\n";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

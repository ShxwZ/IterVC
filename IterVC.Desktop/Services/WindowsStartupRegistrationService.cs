using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace IterVC.Desktop.Services;

internal enum WindowsStartupRegistrationState { Disabled, Enabled, Stale, Invalid, Error }

internal sealed class WindowsStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IterVC";
    private readonly ILogger<WindowsStartupRegistrationService> _logger;

    public WindowsStartupRegistrationService(ILogger<WindowsStartupRegistrationService> logger) => _logger = logger;

    internal bool LastMutationFailed { get; private set; }
    internal string ExpectedCommand => FormatCommand(Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable."));
    internal static string FormatCommand(string executablePath) => $"\"{executablePath}\" --windows-startup";

    internal WindowsStartupRegistrationState Inspect()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (value is null) return WindowsStartupRegistrationState.Disabled;
            var path = ParseCommand(value);
            if (path is null) return WindowsStartupRegistrationState.Invalid;
            if (!File.Exists(path) || !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                return WindowsStartupRegistrationState.Stale;
            return value == ExpectedCommand ? WindowsStartupRegistrationState.Enabled : WindowsStartupRegistrationState.Invalid;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Unable to inspect Windows startup registration");
            return WindowsStartupRegistrationState.Error;
        }
    }

    internal WindowsStartupRegistrationState RepairIfStale()
    {
        var state = Inspect();
        return state == WindowsStartupRegistrationState.Stale ? SetEnabled(true) : state;
    }

    internal WindowsStartupRegistrationState SetEnabled(bool enabled)
    {
        LastMutationFailed = false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled) key.SetValue(ValueName, ExpectedCommand);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            LastMutationFailed = true;
            _logger.LogWarning(ex, "Unable to change Windows startup registration");
        }

        return Inspect();
    }

    internal static string? ParseCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 32767) return null;
        var text = command.Trim();
        if (!text.StartsWith('"')) return null;
        var end = text.IndexOf('"', 1);
        if (end <= 1 || text[(end + 1)..].Trim() != "--windows-startup") return null;
        return text[1..end];
    }
}

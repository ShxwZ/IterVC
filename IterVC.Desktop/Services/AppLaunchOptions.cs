namespace IterVC.Desktop.Services;

internal readonly record struct AppLaunchOptions(bool IsWindowsStartup)
{
    internal static AppLaunchOptions Parse(IEnumerable<string>? args) =>
        new(args?.Any(argument => string.Equals(argument, "--windows-startup", StringComparison.OrdinalIgnoreCase)) == true);

    internal bool ShouldStartHidden(bool hiddenStartupPreference) => IsWindowsStartup && hiddenStartupPreference;
}

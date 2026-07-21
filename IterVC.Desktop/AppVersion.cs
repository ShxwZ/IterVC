using System.Reflection;

namespace IterVC.Desktop;

internal static class AppVersion
{
    internal const string LocalDevelopment = "alpha-local-development";

    internal static string Display => Format(
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    internal static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion) ||
            informationalVersion.Equals("local-development", StringComparison.OrdinalIgnoreCase))
        {
            return LocalDevelopment;
        }

        var version = informationalVersion.Trim();
        const string tagPrefix = "refs/tags/";
        if (version.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase))
            version = version[tagPrefix.Length..];

        const string alphaPrefix = "alpha-";
        if (version.StartsWith(alphaPrefix, StringComparison.OrdinalIgnoreCase))
            version = version[alphaPrefix.Length..];

        if (version.Length > 1 && (version[0] is 'v' or 'V') && char.IsDigit(version[1]))
            version = version[1..];

        return string.IsNullOrWhiteSpace(version)
            ? LocalDevelopment
            : $"{alphaPrefix}{version}";
    }
}

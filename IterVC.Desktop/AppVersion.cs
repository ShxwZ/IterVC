using System.Reflection;

namespace IterVC.Desktop;

internal static class AppVersion
{
    internal const string LocalDevelopment = "alpha-local-development";

    internal static string Display => Format(
        Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    internal static string? CurrentSemanticVersion => NormalizeSemantic(
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

    internal static bool IsNewer(string candidate, string current) =>
        SemanticVersion.TryParse(candidate, out var candidateVersion) &&
        SemanticVersion.TryParse(current, out var currentVersion) &&
        candidateVersion.CompareTo(currentVersion) > 0;

    internal static bool ShouldNotify(
        string candidate, string current, string? dismissedVersion, bool ignoreDismissal = false) =>
        IsNewer(candidate, current) &&
        (ignoreDismissal ||
         !string.Equals(NormalizeSemantic(candidate), NormalizeSemantic(dismissedVersion), StringComparison.OrdinalIgnoreCase));

    internal static string? NormalizeSemantic(string? value) =>
        SemanticVersion.TryParse(value, out var version) ? version.ToString() : null;

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? Prerelease)
        : IComparable<SemanticVersion>
    {
        internal static bool TryParse(string? input, out SemanticVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(input) ||
                input.Equals("local-development", StringComparison.OrdinalIgnoreCase) ||
                input.Equals(LocalDevelopment, StringComparison.OrdinalIgnoreCase))
                return false;

            var value = input.Trim();
            if (value.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase)) value = value[10..];
            if (value.StartsWith("alpha-", StringComparison.OrdinalIgnoreCase)) value = value[6..];
            if (value.Length > 1 && (value[0] is 'v' or 'V')) value = value[1..];
            var buildIndex = value.IndexOf('+');
            if (buildIndex >= 0) value = value[..buildIndex];
            var prereleaseIndex = value.IndexOf('-');
            var prerelease = prereleaseIndex >= 0 ? value[(prereleaseIndex + 1)..] : null;
            var core = prereleaseIndex >= 0 ? value[..prereleaseIndex] : value;
            var parts = core.Split('.');
            var minor = 0;
            var patch = 0;
            if (parts.Length is < 1 or > 3 ||
                !int.TryParse(parts[0], out var major) ||
                (parts.Length > 1 && !int.TryParse(parts[1], out minor)) ||
                (parts.Length > 2 && !int.TryParse(parts[2], out patch)) ||
                string.IsNullOrEmpty(prerelease) && prereleaseIndex >= 0)
                return false;

            version = new SemanticVersion(major, parts.Length > 1 ? minor : 0, parts.Length > 2 ? patch : 0, prerelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            var core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
            if (other.Prerelease is null) return -1;

            var left = Prerelease.Split('.');
            var right = other.Prerelease.Split('.');
            for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                if (i >= left.Length) return -1;
                if (i >= right.Length) return 1;
                var leftNumber = int.TryParse(left[i], out var ln);
                var rightNumber = int.TryParse(right[i], out var rn);
                var comparison = leftNumber && rightNumber
                    ? ln.CompareTo(rn)
                    : leftNumber ? -1
                    : rightNumber ? 1
                    : string.Compare(left[i], right[i], StringComparison.OrdinalIgnoreCase);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        public override string ToString() => $"{Major}.{Minor}.{Patch}{(Prerelease is null ? "" : $"-{Prerelease}")}";
    }
}

using System.Reflection;
using System.Xml.Linq;
using Avalonia.Media;

namespace IterVC.Linux;

/// <summary>
/// Reuses the *actual* semantic palette from the Windows Avalonia App.axaml.
/// The desktop theme is embedded as data, not compiled as a second Application,
/// so Windows remains untouched and Linux tracks upstream color changes.
/// </summary>
internal static class LinuxTheme
{
    private static readonly Lazy<IReadOnlyDictionary<string, IBrush>> Colors = new(ReadColors);

    public static IBrush Brush(string name) =>
        Colors.Value.TryGetValue(name, out var color)
            ? color
            : throw new InvalidOperationException("Missing IterVC theme resource: " + name);

    public static void Validate() {
        foreach (var name in new[] { "AppBackgroundBrush", "SurfaceBaseBrush",
            "SurfaceRaisedBrush", "AccentBrush", "TextPrimaryBrush", "SuccessBrush" })
            _ = Brush(name);
    }

    private static IReadOnlyDictionary<string, IBrush> ReadColors()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("IterVC.Linux.WindowsThemePalette")
            ?? throw new InvalidOperationException("Shared desktop palette was not bundled.");
        var document = XDocument.Load(stream);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = new Dictionary<string, IBrush>(StringComparer.Ordinal);
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = (string?)element.Attribute(x + "Key");
            var value = (string?)element.Attribute("Color");
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                colors[key] = new SolidColorBrush(Color.Parse(value));
        }
        return colors;
    }
}

using Avalonia;
using Avalonia.Media;

namespace IterVC.Desktop.Services;

/// <summary>
/// Manages dynamic theme resources at runtime. Updates Avalonia's application-level
/// resources so that all controls bound via {DynamicResource} update live.
/// </summary>
public static class ThemeService
{
    // Default dark theme colors
    public const string DefaultAccentHex = "#5B5BF0";
    public const string DefaultCardBackgroundHex = "#111116";

    // Resource keys used in XAML via {DynamicResource ...}
    public const string AccentColorKey = "ThemeAccentColor";
    public const string AccentHoverColorKey = "ThemeAccentHoverColor";
    public const string CardBackgroundKey = "ThemeCardBackground";
    public const string CardBorderKey = "ThemeCardBorder";

    /// <summary>
    /// Applies the given accent and card background colors to the app's dynamic resources.
    /// Pass null to use the default value.
    /// </summary>
    public static void Apply(string? accentHex, string? cardBackgroundHex)
    {
        var app = Application.Current;
        if (app is null) return;

        accentHex ??= DefaultAccentHex;
        cardBackgroundHex ??= DefaultCardBackgroundHex;

        var accent = Color.Parse(accentHex);
        var accentHover = Lighten(accent, 0.15);
        var cardBg = Color.Parse(cardBackgroundHex);
        // Inject transparency (alpha 180 = ~70% opacity) for glassmorphism
        cardBg = new Color(180, cardBg.R, cardBg.G, cardBg.B);

        // Tint the border with the accent color
        var baseBorder = Lighten(cardBg, 0.08);
        var cardBorder = Blend(accent, baseBorder, 0.25); // 25% accent
        cardBorder = new Color(200, cardBorder.R, cardBorder.G, cardBorder.B);

        // Tint texts with the accent color for better cohesion
        var textPrimary = Blend(accent, Color.Parse("#D1D1DB"), 0.15);
        var textSecondary = Blend(accent, Color.Parse("#5F5F7A"), 0.20);
        var textMuted = Blend(accent, Color.Parse("#3F3F54"), 0.25);

        app.Resources[AccentColorKey] = new SolidColorBrush(accent);
        app.Resources[AccentHoverColorKey] = new SolidColorBrush(accentHover);
        app.Resources[CardBackgroundKey] = new SolidColorBrush(cardBg);
        app.Resources[CardBorderKey] = new SolidColorBrush(cardBorder);

        app.Resources["ThemeTextPrimary"] = new SolidColorBrush(textPrimary);
        app.Resources["ThemeTextSecondary"] = new SolidColorBrush(textSecondary);
        app.Resources["ThemeTextMuted"] = new SolidColorBrush(textMuted);
    }

    /// <summary>
    /// Resets all theme resources to defaults.
    /// </summary>
    public static void ResetToDefaults() => Apply(null, null);

    /// <summary>
    /// Parses a hex string like "#5B5BF0" into R, G, B bytes.
    /// </summary>
    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var color = Color.Parse(hex);
        return (color.R, color.G, color.B);
    }

    /// <summary>
    /// Converts R, G, B bytes into a hex string like "#5B5BF0".
    /// </summary>
    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    private static Color Lighten(Color color, double amount)
    {
        byte r = (byte)Math.Min(255, color.R + (255 - color.R) * amount);
        byte g = (byte)Math.Min(255, color.G + (255 - color.G) * amount);
        byte b = (byte)Math.Min(255, color.B + (255 - color.B) * amount);
        return Color.FromRgb(r, g, b);
    }

    private static Color Blend(Color c1, Color c2, double amountC1)
    {
        byte r = (byte)(c1.R * amountC1 + c2.R * (1 - amountC1));
        byte g = (byte)(c1.G * amountC1 + c2.G * (1 - amountC1));
        byte b = (byte)(c1.B * amountC1 + c2.B * (1 - amountC1));
        return Color.FromRgb(r, g, b);
    }
}

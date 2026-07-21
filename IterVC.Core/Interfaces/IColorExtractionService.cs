namespace IterVC.Core.Interfaces;

/// <summary>
/// Extracts a vibrant accent color and a matching dark card background color from an image.
/// Returns a tuple of (AccentHex, CardBackgroundHex).
/// </summary>
public interface IColorExtractionService
{
    /// <summary>
    /// Extracts a vibrant accent color and a matching dark card background color from an image.
    /// Returns a tuple of (AccentHex, CardBackgroundHex).
    /// </summary>
    Task<(string AccentHex, string CardBackgroundHex)> ExtractThemeColorsAsync(string imagePath, CancellationToken cancellationToken = default);
}

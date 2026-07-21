using IterVC.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace IterVC.Desktop.Services;

/// <summary>
/// Extracts a dominant vibrant color from an image using SixLabors.ImageSharp.
/// Downscales to a small thumbnail, then finds the most frequent saturated hue bucket.
/// </summary>
public sealed class ColorExtractionService : IColorExtractionService
{
    private const int ThumbnailSize = 50;
    private const int HueBuckets = 36; // 10° per bucket
    private const float MinSaturation = 0.25f;
    private const float MinLightness = 0.15f;
    private const float MaxLightness = 0.85f;

    public async Task<(string AccentHex, string CardBackgroundHex)> ExtractThemeColorsAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(imagePath, cancellationToken);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(ThumbnailSize, ThumbnailSize),
            Mode = ResizeMode.Max
        }));

        var hueCounts = new int[HueBuckets];
        var hueR = new long[HueBuckets];
        var hueG = new long[HueBuckets];
        var hueB = new long[HueBuckets];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A < 128) continue;

                    RgbToHsl(pixel.R, pixel.G, pixel.B, out float h, out float s, out float l);

                    // Skip desaturated or very dark/light pixels to only count vibrant colors
                    if (s < MinSaturation || l < MinLightness || l > MaxLightness) continue;

                    int bucket = (int)(h / 360f * HueBuckets);
                    if (bucket >= HueBuckets) bucket = HueBuckets - 1;

                    hueCounts[bucket]++;
                    hueR[bucket] += pixel.R;
                    hueG[bucket] += pixel.G;
                    hueB[bucket] += pixel.B;
                }
            }
        });

        // Find the bucket with the most pixels
        int bestBucket = 0;
        int bestCount = 0;
        for (int i = 0; i < HueBuckets; i++)
        {
            if (hueCounts[i] > bestCount)
            {
                bestCount = hueCounts[i];
                bestBucket = i;
            }
        }

        if (bestCount == 0)
        {
            // No saturated pixels found, return defaults
            return ("#5B5BF0", "#111116");
        }

        // Average the RGB values in the dominant bucket
        byte avgR = (byte)(hueR[bestBucket] / bestCount);
        byte avgG = (byte)(hueG[bestBucket] / bestCount);
        byte avgB = (byte)(hueB[bestBucket] / bestCount);

        RgbToHsl(avgR, avgG, avgB, out float fh, out float fs, out float fl);

        // Generate highly vibrant Accent color
        float accentS = Math.Clamp(fs * 1.5f, 0.7f, 1.0f); // Boost saturation heavily
        float accentL = Math.Clamp(fl, 0.5f, 0.65f);       // Ensure it's bright enough
        HslToRgb(fh, accentS, accentL, out byte rA, out byte gA, out byte bA);

        // Generate dark muted Card color from the same hue
        float cardS = Math.Clamp(fs * 0.3f, 0.1f, 0.25f);  // Desaturate for background
        float cardL = 0.10f;                               // Very dark
        HslToRgb(fh, cardS, cardL, out byte rC, out byte gC, out byte bC);

        return ($"#{rA:X2}{gA:X2}{bA:X2}", $"#{rC:X2}{gC:X2}{bC:X2}");
    }

    private static void RgbToHsl(byte r, byte g, byte b, out float h, out float s, out float l)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        l = (max + min) / 2f;

        if (delta < 0.0001f)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l < 0.5f ? delta / (max + min) : delta / (2f - max - min);

        if (Math.Abs(max - rf) < 0.0001f)
            h = ((gf - bf) / delta) % 6f;
        else if (Math.Abs(max - gf) < 0.0001f)
            h = (bf - rf) / delta + 2f;
        else
            h = (rf - gf) / delta + 4f;

        h *= 60f;
        if (h < 0) h += 360f;
    }

    private static void HslToRgb(float h, float s, float l, out byte r, out byte g, out byte b)
    {
        if (s < 0.0001f)
        {
            r = g = b = (byte)(l * 255);
            return;
        }

        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs(h / 60f % 2f - 1f));
        float m = l - c / 2f;

        float rf, gf, bf;
        if (h < 60) { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else { rf = c; gf = 0; bf = x; }

        r = (byte)Math.Clamp((rf + m) * 255f, 0, 255);
        g = (byte)Math.Clamp((gf + m) * 255f, 0, 255);
        b = (byte)Math.Clamp((bf + m) * 255f, 0, 255);
    }
}

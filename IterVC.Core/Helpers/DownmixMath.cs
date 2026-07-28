namespace IterVC.Core.Helpers;

/// <summary>
/// Allocation-free pure functions for multichannel (5.1 / 7.1 / N) to stereo conversion.
/// </summary>
/// <remarks>
/// Orden de canales asumido (Windows <c>WAVEFORMATEXTENSIBLE</c>):
/// <list type="bullet">
///   <item><description><b>6 (5.1)</b>: FL, FR, FC, LFE, LS, RS</description></item>
///   <item><description><b>8 (7.1)</b>: FL, FR, FC, LFE, BL, BR, SL, SR</description></item>
/// </list>
/// Para cualquier otro N se usa <see cref="DownmixGeneric"/>.
///
/// </remarks>
public static class DownmixMath
{
    /// <summary>1 / √2 (-3 dB), used for center and surround contributions.</summary>
    public const float InvSqrt2 = 0.7071067811865475f;

    /// <summary>LFE contribution in the stereo matrix (-6 dB).</summary>
    public const float LfeGain = 0.5f;

    /// <summary>Applies the deterministic 5.1-to-stereo matrix to one frame.</summary>
    /// <param name="frame">Six interleaved samples ordered as FL, FR, FC, LFE, LS, RS.</param>
    /// <returns>A stereo pair using:
    /// <c>L = FL + 0.707·FC + 0.5·LFE + 0.707·LS</c>,
    /// <c>R = FR + 0.707·FC + 0.5·LFE + 0.707·RS</c>.
    /// The established channel gains intentionally preserve normal listening level.
    /// Correlated channels may exceed full scale in the IEEE-float intermediate;
    /// final post-mix protection, not this method, handles those peaks.</returns>
    public static (float Left, float Right) Downmix5_1(ReadOnlySpan<float> frame)
    {
        if (frame.Length < 6)
            return (frame.Length > 0 ? frame[0] : 0f, frame.Length > 1 ? frame[1] : 0f);

        var fl = frame[0];
        var fr = frame[1];
        var fc = frame[2];
        var lfe = frame[3];
        var ls = frame[4];
        var rs = frame[5];

        return (
            fl + InvSqrt2 * fc + LfeGain * lfe + InvSqrt2 * ls,
            fr + InvSqrt2 * fc + LfeGain * lfe + InvSqrt2 * rs);
    }

    /// <summary>Applies the validated 7.1-to-stereo matrix to one frame.</summary>
    /// <param name="frame">Eight interleaved samples ordered as FL, FR, FC, LFE, BL, BR, SL, SR.</param>
    /// <returns>A stereo pair using
    /// <c>L = FL + 0.707·FC + 0.5·BL + 0.707·SL + 0.5·LFE</c> and
    /// <c>R = FR + 0.707·FC + 0.5·BR + 0.707·SR + 0.5·LFE</c>.
    /// No normalization or nonlinear clipping is applied here because both changed
    /// the validated 7.1 sound; the complete mix is protected at the final output.</returns>
    public static (float Left, float Right) Downmix7_1(ReadOnlySpan<float> frame)
    {
        if (frame.Length < 8)
            return DownmixGeneric(frame);

        var fl = frame[0];
        var fr = frame[1];
        var fc = frame[2];
        var lfe = frame[3];
        var bl = frame[4];
        var br = frame[5];
        var sl = frame[6];
        var sr = frame[7];

        return (
            fl + InvSqrt2 * fc + 0.5f * bl + InvSqrt2 * sl + LfeGain * lfe,
            fr + InvSqrt2 * fc + 0.5f * br + InvSqrt2 * sr + LfeGain * lfe);
    }

    /// <summary>
    /// Fallback genérico para N canales desconocidas. ch[0]→L, ch[1]→R (preservando fase y espacialidad);
    /// el resto se suma y se aplica con factor <c>1/√N</c> para evitar saturación.
    /// </summary>
    public static (float Left, float Right) DownmixGeneric(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return (0f, 0f);

        var n = frame.Length;
        if (n == 1) return (frame[0], frame[0]);
        if (n == 2) return (frame[0], frame[1]);
        var invSqrtN = 1f / MathF.Sqrt(n);

        if (n >= 2)
        {
            var lBase = frame[0];
            var rBase = frame[1];

            float sumRest = 0f;
            for (var i = 2; i < n; i++) sumRest += frame[i];

            var shared = sumRest * invSqrtN;
            return ((lBase + shared) * invSqrtN, (rBase + shared) * invSqrtN);
        }
        else
        {
            float sum = 0f;
            for (var i = 0; i < n; i++) sum += frame[i];

            var mixed = sum * invSqrtN;
            return (mixed, mixed);
        }
    }

    /// <summary>
    /// Selector puro que delega al downmix correcto según el número de canales.
    /// </summary>
    public static (float Left, float Right) DownmixForChannels(ReadOnlySpan<float> frame) => frame.Length switch
    {
        6 => Downmix5_1(frame),
        8 => Downmix7_1(frame),
        _ => DownmixGeneric(frame),
    };

}

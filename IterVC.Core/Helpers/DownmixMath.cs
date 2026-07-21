namespace IterVC.Core.Helpers;

/// <summary>
/// Funciones puras para conversión multicanal (5.1 / 7.1 / N) a estéreo.
/// Sin estado, sin alocación: un único frame a la vez, aptas para inlining del JIT.
/// Esta capa es independiente de NAudio/WASAPI para permitir tests unitarios sin audio.
/// </summary>
/// <remarks>
/// Orden de canales asumido (Windows <c>WAVEFORMATEXTENSIBLE</c>):
/// <list type="bullet">
///   <item><description><b>6 (5.1)</b>: FL, FR, FC, LFE, LS, RS</description></item>
///   <item><description><b>8 (7.1)</b>: FL, FR, FC, LFE, BL, BR, SL, SR</description></item>
/// </list>
/// Para cualquier otro N se usa <see cref="DownmixGeneric"/>.
///
/// Nota extra: No tengo ni idea de como funciona, totalmente generado por IA esta parte
/// </remarks>
public static class DownmixMath
{
    /// <summary>1 / √2 — atenuación estándar (-3 dB) usada en matrices estéreo para canales centrales/surround.</summary>
    public const float InvSqrt2 = 0.7071067811865475f;

    /// <summary>Ganancia del LFE en downmix estéreo (-6 dB). El subwoofer no aporta imagen espacial L/R,
    /// por eso se atenúa más que los canales de imagen (FC, LS/RS, etc.), que usan <see cref="InvSqrt2"/>.</summary>
    public const float LfeGain = 0.5f;

    /// <summary>Función matemática pura 5.1 → estéreo. Llamar una vez por frame.</summary>
    /// <param name="frame">Vector interleaved con <b>6</b> muestras (FL, FR, FC, LFE, LS, RS).</param>
    /// <returns>Tupla (L, R) usando la fórmula espacial estándar:
    /// <c>L = FL + 0.707·FC + 0.5·LFE + 0.707·LS</c>,
    /// <c>R = FR + 0.707·FC + 0.5·LFE + 0.707·RS</c>.
    /// Ambas componentes se pasan por <see cref="SoftClip"/> para evitar clipping digital.</returns>
    public static (float Left, float Right) Downmix5_1(ReadOnlySpan<float> frame)
    {
        if (frame.Length < 6)
            return (SoftClip(frame.Length > 0 ? frame[0] : 0f), SoftClip(frame.Length > 1 ? frame[1] : 0f));

        var fl = frame[0];
        var fr = frame[1];
        var fc = frame[2];
        var lfe = frame[3];
        var ls = frame[4];
        var rs = frame[5];

        return (
            SoftClip(fl + InvSqrt2 * fc + LfeGain * lfe + InvSqrt2 * ls),
            SoftClip(fr + InvSqrt2 * fc + LfeGain * lfe + InvSqrt2 * rs));
    }

    /// <summary>Función matemática pura 7.1 → estéreo. Llamar una vez por frame.</summary>
    /// <param name="frame">Vector interleaved con <b>8</b> muestras (FL, FR, FC, LFE, BL, BR, SL, SR).</param>
    /// <returns>Tupla (L, R): <c>L = FL + 0.707·FC + 0.5·BL + 0.707·SL + 0.5·LFE</c>,
    /// <c>R = FR + 0.707·FC + 0.5·BR + 0.707·SR + 0.5·LFE</c>.</returns>
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
            SoftClip(fl + InvSqrt2 * fc + 0.5f * bl + InvSqrt2 * sl + LfeGain * lfe),
            SoftClip(fr + InvSqrt2 * fc + 0.5f * br + InvSqrt2 * sr + LfeGain * lfe));
    }

    /// <summary>
    /// Fallback genérico para N canales desconocidas. ch[0]→L, ch[1]→R (preservando fase y espacialidad);
    /// el resto se suma y se aplica con factor <c>1/√N</c> para evitar saturación.
    /// </summary>
    public static (float Left, float Right) DownmixGeneric(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return (0f, 0f);

        var n = frame.Length;
        var invSqrtN = 1f / MathF.Sqrt(n);

        if (n >= 2)
        {
            var lBase = frame[0];
            var rBase = frame[1];

            float sumRest = 0f;
            for (var i = 2; i < n; i++) sumRest += frame[i];

            var shared = sumRest * invSqrtN;
            return (SoftClip(lBase + shared), SoftClip(rBase + shared));
        }
        else
        {
            float sum = 0f;
            for (var i = 0; i < n; i++) sum += frame[i];

            var mixed = sum * invSqrtN;
            return (SoftClip(mixed), SoftClip(mixed));
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

    /// <summary>
    /// Clamp duro en [-1, 1] (el nombre "SoftClip" es heredado, pero no aplica ninguna curva
    /// de saturación; es un recorte lineal). Para PCM IEEE float los valores legítimos están
    /// en ese rango; recortar fuera de él es la forma más simple de evitar overflow/aliasing
    /// en el destino (VB-Cable / Discord / OBS). Si en el futuro se quiere una saturación
    /// real (tipo tanh), esta es la función a sustituir.
    /// </summary>
    public static float SoftClip(float x)
    {
        if (x > 1f) return 1f;
        if (x < -1f) return -1f;
        return x;
    }
}
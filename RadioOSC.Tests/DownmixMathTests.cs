using Microsoft.VisualStudio.TestTools.UnitTesting;
using RadioOSC.Core.Helpers;

namespace RadioOSC.Tests;

/// <summary>
/// Tests unitarios puros para <see cref="DownmixMath"/>. No tocan WASAPI/NAudio,
/// así que corren en milisegundos en cualquier máquina (incluso sin audio stack).
/// </summary>
[TestClass]
public sealed class DownmixMathTests
{
    private const float Tol = 1e-5f;

    // ---------- SoftClip ----------

    [TestMethod]
    public void SoftClip_ClampsPositiveOverOne()
    {
        Assert.AreEqual(1f, DownmixMath.SoftClip(2.5f), Tol);
        Assert.AreEqual(1f, DownmixMath.SoftClip(1.0001f), Tol);
    }

    [TestMethod]
    public void SoftClip_ClampsNegativeBelowMinusOne()
    {
        Assert.AreEqual(-1f, DownmixMath.SoftClip(-3f), Tol);
        Assert.AreEqual(-1f, DownmixMath.SoftClip(-1.5f), Tol);
    }

    [TestMethod]
    public void SoftClip_PassesThroughRangeValues()
    {
        Assert.AreEqual(0f, DownmixMath.SoftClip(0f), Tol);
        Assert.AreEqual(0.5f, DownmixMath.SoftClip(0.5f), Tol);
        Assert.AreEqual(-0.5f, DownmixMath.SoftClip(-0.5f), Tol);
        Assert.AreEqual(1f, DownmixMath.SoftClip(1f), Tol);
        Assert.AreEqual(-1f, DownmixMath.SoftClip(-1f), Tol);
    }

    [TestMethod]
    public void SoftClip_HandlesEdgeValues()
    {
        Assert.AreEqual(0.99999f, DownmixMath.SoftClip(0.99999f), Tol);
        Assert.AreEqual(1f, DownmixMath.SoftClip(float.MaxValue), Tol);
        Assert.AreEqual(-1f, DownmixMath.SoftClip(float.MinValue), Tol);
    }

    // ---------- Downmix 5.1 ----------

    [TestMethod]
    public void Downmix5_1_AppliesFormula_FrontOnly()
    {
        // Sólo FL y FR activos (todo lo demás cero).
        var frame = new float[6] { 1f, -1f, 0f, 0f, 0f, 0f };
        var (l, r) = DownmixMath.Downmix5_1(frame);
        Assert.AreEqual(1f, l, Tol);
        Assert.AreEqual(-1f, r, Tol);
    }

    [TestMethod]
    public void Downmix5_1_AppliesFormula_AllChannels()
    {
        // Caso del enunciado: L = FL + 0.707*FC + 0.707*LFE + 0.707*LS, igual para R con RS.
        // Valores pequeños para evitar SoftClip (>1).
        var fl = 0.30f; var fr = -0.20f; var fc = 0.10f; var lfe = 0.05f;
        var ls = 0.15f; var rs = -0.15f;
        var frame = new float[6] { fl, fr, fc, lfe, ls, rs };
        var (l, r) = DownmixMath.Downmix5_1(frame);
        var expectedL = fl + DownmixMath.InvSqrt2 * fc + DownmixMath.InvSqrt2 * lfe + DownmixMath.InvSqrt2 * ls;
        var expectedR = fr + DownmixMath.InvSqrt2 * fc + DownmixMath.InvSqrt2 * lfe + DownmixMath.InvSqrt2 * rs;
        // 0.30 + 0.0707 + 0.0354 + 0.1061 ≈ 0.5122  (no satura)
        // -0.20 + 0.0707 + 0.0354 + -0.1061 ≈ -0.20
        Assert.AreEqual(expectedL, l, 1e-4f);
        Assert.AreEqual(expectedR, r, 1e-4f);
    }

    [TestMethod]
    public void Downmix5_1_ClipsToOneWhenSaturating()
    {
        // Si los canales suman > 1, debe saturar (no propagar > 1).
        var frame = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };
        var (l, r) = DownmixMath.Downmix5_1(frame);
        Assert.AreEqual(1f, l, Tol);
        Assert.AreEqual(1f, r, Tol);
    }

    [TestMethod]
    public void Downmix5_1_SurroundOnlyAffectsOwnChannel()
    {
        // LS=1 afecta L, RS=1 afecta R, sin mezclarse entre canales.
        var frameLs = new float[6] { 0f, 0f, 0f, 0f, 1f, 0f };
        var (lLs, rLs) = DownmixMath.Downmix5_1(frameLs);
        Assert.AreEqual(DownmixMath.InvSqrt2, lLs, Tol);
        Assert.AreEqual(0f, rLs, Tol);

        var frameRs = new float[6] { 0f, 0f, 0f, 0f, 0f, 1f };
        var (lRs, rRs) = DownmixMath.Downmix5_1(frameRs);
        Assert.AreEqual(0f, lRs, Tol);
        Assert.AreEqual(DownmixMath.InvSqrt2, rRs, Tol);
    }

    [TestMethod]
    public void Downmix5_1_ShortSpanFallsBackGracefully()
    {
        // Menos de 6 muestras -> fallback usando disponibles, sin excepción.
        var frame = new float[2] { 0.3f, 0.6f };
        var (l, r) = DownmixMath.Downmix5_1(frame);
        Assert.AreEqual(0.3f, l, Tol);
        Assert.AreEqual(0.6f, r, Tol);
    }

    // ---------- Downmix 7.1 ----------

    [TestMethod]
    public void Downmix7_1_AppliesFormula_CenterAndSides()
    {
        // FC=0.5 se reparte en ambos canales con factor 0.707 (sin desfase).
        var frame = new float[8] { 0f, 0f, 0.5f, 0f, 0f, 0f, 0f, 0f };
        var (l, r) = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(DownmixMath.InvSqrt2 * 0.5f, l, 1e-5f);
        Assert.AreEqual(DownmixMath.InvSqrt2 * 0.5f, r, 1e-5f);
    }

    [TestMethod]
    public void Downmix7_1_FrontChannelsPassThrough()
    {
        // FL y FR limpios; LFE/BL/BR/SL/SR = 0.
        var frame = new float[8] { 1f, -1f, 0f, 0f, 0f, 0f, 0f, 0f };
        var (l, r) = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(1f, l, Tol);
        Assert.AreEqual(-1f, r, Tol);
    }

    [TestMethod]
    public void Downmix7_1_BackAndSideChannelsContributeToCorrectSide()
    {
        // BL=0.4 -> L (0.5*0.4=0.2), SR=0.6 -> R (0.707*0.6=0.4243).
        var frame = new float[8] { 0f, 0f, 0f, 0f, 0.4f, 0f, 0f, 0.6f };
        var (l, r) = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(0.2f, l, 1e-5f);
        Assert.AreEqual(DownmixMath.InvSqrt2 * 0.6f, r, 1e-5f);
    }

    [TestMethod]
    public void Downmix7_1_LfeAttenuatedToLfeGainFactor()
    {
        // LFE = 1.0 -> ambos canales suman LfeGain = 0.5.
        var frame = new float[8] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f };
        var (l, r) = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(DownmixMath.LfeGain, l, Tol);
        Assert.AreEqual(DownmixMath.LfeGain, r, Tol);
    }

    [TestMethod]
    public void Downmix7_1_ShortSpanFallsBackToGeneric()
    {
        // Si pasan menos de 8 muestras, cae al genérico sin lanzar.
        var frame = new float[4] { 1f, -1f, 0.3f, 0.1f };
        var (l, r) = DownmixMath.Downmix7_1(frame);
        // N=4, invSqrt4=0.5; ch[2]+ch[3]=0.4; shared=0.2; L=1.2->SoftClip=1; R=-0.8
        Assert.AreEqual(1f, l, Tol);
        Assert.AreEqual(-0.8f, r, 1e-5f);
    }

    // ---------- Downmix genérico ----------

    [TestMethod]
    public void DownmixGeneric_Mono_DuplicatesToLR()
    {
        var frame = new float[1] { 0.42f };
        var (l, r) = DownmixMath.DownmixGeneric(frame);
        // 1 muestra -> ambos canales = 0.42 * 1/√1 = 0.42.
        Assert.AreEqual(0.42f, l, Tol);
        Assert.AreEqual(0.42f, r, Tol);
    }

    [TestMethod]
    public void DownmixGeneric_EmptySpan_ReturnsZeros()
    {
        var frame = System.Array.Empty<float>();
        var (l, r) = DownmixMath.DownmixGeneric(frame);
        Assert.AreEqual(0f, l);
        Assert.AreEqual(0f, r);
    }

    [TestMethod]
    public void DownmixGeneric_TwoChannels_PreservesLR()
    {
        var frame = new float[2] { 0.5f, -0.7f };
        var (l, r) = DownmixMath.DownmixGeneric(frame);
        Assert.AreEqual(0.5f, l, Tol);
        Assert.AreEqual(-0.7f, r, Tol);
    }

    [TestMethod]
    public void DownmixGeneric_ExtraChannels_MixWithInvSqrtN()
    {
        // N=4 -> invSqrtN = 0.5. ch[2]+ch[3]=0.20+(-0.10)=0.10; shared=0.05.
        // Valores pequeños para evitar SoftClip (>1).
        var frame = new float[4] { 0.50f, -0.50f, 0.20f, -0.10f };
        var (l, r) = DownmixMath.DownmixGeneric(frame);
        Assert.AreEqual(0.50f + 0.05f, l, 1e-5f);
        Assert.AreEqual(-0.50f + 0.05f, r, 1e-5f);
    }

    [TestMethod]
    public void DownmixGeneric_AvoidsSaturationWithManyChannels()
    {
        // 16 canales todos a 1.0 -> sin invSqrtN satura a 16; con invSqrtN a 1/4 = 4 (-> SoftClip a 1).
        var frame = new float[16];
        for (var i = 0; i < 16; i++) frame[i] = 1f;
        var (l, r) = DownmixMath.DownmixGeneric(frame);
        Assert.IsTrue(l <= 1f, $"L saturó por encima de 1: {l}");
        Assert.IsTrue(r <= 1f, $"R saturó por encima de 1: {r}");
    }

    // ---------- Selector DownmixForChannels ----------

    [TestMethod]
    public void DownmixForChannels_FiveOne_UsesFiveOneFormula()
    {
        var frame = new float[6] { 1f, -1f, 0.5f, 0f, 0.7f, -0.7f };
        var actual = DownmixMath.DownmixForChannels(frame);
        var expected = DownmixMath.Downmix5_1(frame);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void DownmixForChannels_SevenOne_UsesSevenOneFormula()
    {
        var frame = new float[8] { 1f, -1f, 0.5f, 0f, 0.2f, 0.3f, 0.4f, 0.5f };
        var actual = DownmixMath.DownmixForChannels(frame);
        var expected = DownmixMath.Downmix7_1(frame);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void DownmixForChannels_Other_UsesGeneric()
    {
        var frame = new float[4] { 1f, -1f, 0.3f, -0.1f };
        var actual = DownmixMath.DownmixForChannels(frame);
        var expected = DownmixMath.DownmixGeneric(frame);
        Assert.AreEqual(expected, actual);
    }

    // ---------- Constantes ----------

    [TestMethod]
    public void InvSqrt2_IsOneOverRootTwo()
    {
        Assert.AreEqual(0.7071067811865475f, DownmixMath.InvSqrt2, Tol);
    }

    [TestMethod]
    public void LfeGain_IsHalf()
    {
        Assert.AreEqual(0.5f, DownmixMath.LfeGain, Tol);
    }
}

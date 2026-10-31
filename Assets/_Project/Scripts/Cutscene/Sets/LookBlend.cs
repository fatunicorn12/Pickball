using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The blend between two <see cref="LookState"/>s. Pure and stateless: the result depends only
    /// on the two looks and t, so a Timeline scrubbed backwards, a skipped frame or a different
    /// frame rate all land on the same look.
    ///
    /// <b>Guarantees:</b> t ≤ 0 returns <c>a</c> exactly and t ≥ 1 returns <c>b</c> exactly, so the
    /// end of the resolve is bit-for-bit the gameplay lighting. Fog density blends in log space
    /// (thick fog thins evenly instead of all at once). RenderSettings cannot mix two fog modes,
    /// so when the modes differ the blend keeps <c>a</c>'s mode and moves its density toward
    /// <c>b.FogBlendDensity</c>; the switch to <c>b</c>'s mode happens at t = 1, where that
    /// density was chosen to look like <c>b</c>'s fog.
    /// </summary>
    public static class LookBlend
    {
        /// <summary>The look at t (clamped to 0..1) between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public static LookState Evaluate(in LookState a, in LookState b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;

            float densityTarget = a.FogMode == b.FogMode ? b.FogDensity : b.FogBlendDensity;
            return new LookState(
                a.FogMode,
                Color.Lerp(a.FogColor, b.FogColor, t),
                LogLerp(a.FogDensity, densityTarget, t),
                Mathf.Lerp(a.FogStart, b.FogStart, t),
                Mathf.Lerp(a.FogEnd, b.FogEnd, t),
                LogLerp(a.FogBlendDensity, b.FogBlendDensity, t),
                Color.Lerp(a.AmbientSky, b.AmbientSky, t),
                Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
                Color.Lerp(a.AmbientGround, b.AmbientGround, t),
                Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
                Color.Lerp(a.SunColor, b.SunColor, t),
                Mathf.Lerp(a.SunShadowStrength, b.SunShadowStrength, t));
        }

        /// <summary>Interpolates two positive values evenly in log space; falls back to a linear
        /// lerp if either is not positive.</summary>
        public static float LogLerp(float from, float to, float t)
        {
            if (from <= 0f || to <= 0f) return Mathf.Lerp(from, to, t);
            return Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), t));
        }
    }
}

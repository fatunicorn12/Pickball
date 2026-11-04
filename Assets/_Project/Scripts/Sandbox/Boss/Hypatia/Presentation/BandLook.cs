using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The floor fire's look as plain maths, so the promises that keep it fair can
    /// be tested: the burn front arrives exactly when the band ignites, flames never
    /// stand on safe ground, and the bright boundary line sits on the real edge of
    /// the fire. Used by <see cref="BandFireView"/>.
    /// </summary>
    public static class BandLook
    {
        /// <summary>How far through its warning a band is, 0..1, from the real
        /// countdown. It reads the countdown rather than keeping its own, so the
        /// front can never drift from the ignition, and freezing her holds it.</summary>
        public static float WarningProgress(float remaining, float lead)
        {
            if (lead <= 0f) return 1f;
            return 1f - Mathf.Clamp01(remaining / lead);
        }

        /// <summary>Floor area of a band, m².</summary>
        public static float Area(float inner, float outer) =>
            Mathf.PI * Mathf.Max(0f, outer * outer - inner * inner);

        /// <summary>Particles per second for a band: a density over its area, up to
        /// <paramref name="cap"/>, so a big band does not get a huge overdraw bill.</summary>
        public static float Rate(float inner, float outer, float perSquareMetre, float cap) =>
            Mathf.Min(Mathf.Max(0f, cap), Area(inner, outer) * Mathf.Max(0f, perSquareMetre));

        /// <summary>
        /// The Circle shape's Radius Thickness that emits only between
        /// <paramref name="inner"/> + <paramref name="margin"/> and
        /// <paramref name="outer"/>. The margin is half a flame's width, so a flame
        /// standing at the edge does not reach over ground that is safe. On a band too
        /// narrow for the margin, it emits along the outer edge instead.
        /// </summary>
        public static float RadiusThickness(float inner, float outer, float margin)
        {
            if (outer <= 0f) return 0f;
            float from = Mathf.Max(0f, inner) + Mathf.Max(0f, margin);
            return Mathf.Clamp01((outer - from) / outer);
        }

        /// <summary>True when this band's inner edge is the boundary of the ground
        /// that is safe: now, or once the current warning catches. That line is drawn
        /// hottest; the old seams between burned bands stay dim.</summary>
        public static bool IsFrontier(float inner, float safeRadius, float projectedSafeRadius) =>
            Mathf.Abs(inner - safeRadius) < 0.01f || Mathf.Abs(inner - projectedSafeRadius) < 0.01f;

        /// <summary>A flash fading from 1 to 0 over <paramref name="seconds"/>,
        /// fastest at the start.</summary>
        public static float Flash(float age, float seconds)
        {
            if (seconds <= 0f) return 0f;
            float u = 1f - Mathf.Clamp01(age / seconds);
            return u * u;
        }
    }
}

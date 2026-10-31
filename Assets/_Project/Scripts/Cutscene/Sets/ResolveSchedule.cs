using UnityEngine;

namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The timing math of the construction-line resolve: how much of each stroke is drawn, and how
    /// bright the lines are, at a given progress. Pure and stateless so it is tested without a
    /// scene, and so scrubbing the Timeline backwards un-draws the lines exactly.
    ///
    /// Progress runs 0..1 over the whole resolve. The first <c>drawEnd</c> of it draws every
    /// stroke (each in its own window, so arcs lead and the hill curve finishes last); from
    /// <c>fadeStart</c> to 1 the lines dim from full to the faint permanent construction lines.
    /// Every stroke's drawn fraction is non-decreasing in progress.
    /// </summary>
    public static class ResolveSchedule
    {
        /// <summary>Progress mapped into the drawing phase, 0..1 (1 once drawing is done).</summary>
        public static float DrawPhase(float progress, float drawEnd)
        {
            if (drawEnd <= 0f) return progress > 0f ? 1f : 0f;
            return Mathf.Clamp01(progress / drawEnd);
        }

        /// <summary>
        /// One stroke's window inside its group's window (both in draw-phase units). Each stroke
        /// takes <paramref name="share"/> of the group window; <paramref name="order"/> (0..1)
        /// slides it from the group's start to its end, so strokes follow one another.
        /// </summary>
        public static Vector2 StrokeWindow(Vector2 groupWindow, float order, float share)
        {
            float span = Mathf.Max(groupWindow.y - groupWindow.x, 0f);
            float length = span * Mathf.Clamp01(share);
            float start = groupWindow.x + Mathf.Clamp01(order) * (span - length);
            return new Vector2(start, start + length);
        }

        /// <summary>
        /// The drawn fraction of a stroke (0 = not started, 1 = complete), eased out so the pen
        /// decelerates into the end of the stroke. Monotonic in <paramref name="drawPhase"/>.
        /// </summary>
        public static float Visible(float drawPhase, Vector2 window)
        {
            float length = window.y - window.x;
            float t = length <= 0f ? (drawPhase >= window.x ? 1f : 0f) : Mathf.Clamp01((drawPhase - window.x) / length);
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>The lines' brightness: 1 until <paramref name="fadeStart"/>, then down to
        /// <paramref name="faintAlpha"/> at progress 1.</summary>
        public static float Alpha(float progress, float fadeStart, float faintAlpha)
        {
            if (progress <= fadeStart) return 1f;
            if (fadeStart >= 1f) return 1f;
            float t = Mathf.Clamp01((progress - fadeStart) / (1f - fadeStart));
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(1f, faintAlpha, t);
        }
    }
}

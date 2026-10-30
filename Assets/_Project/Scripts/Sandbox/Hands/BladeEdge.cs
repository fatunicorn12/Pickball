using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// Which side of a blade carries the cutting edge, so a held weapon's
    /// grip anchor can point +Z at it and the edge leads with the knuckles
    /// (see <see cref="GripFrame"/>).
    ///
    /// The mesh's wider girth axis finds the plane of the blade, but not which
    /// of its two long sides is sharp. Shape doesn't say either: on the ruler
    /// sword the steel edge is the straighter side and the ruler's tick edge
    /// runs higher at the tip, which a different model could easily reverse.
    /// Colour does. A steel edge is grey (unsaturated) where the rest of the
    /// blade is painted, so this compares the colour of the outermost strip on
    /// each side, sampled from the texture at the vertices.
    ///
    /// Pure maths on (offset, colour) samples, so EditMode tests can pin it
    /// down without a model; the held-weapon builder does the sampling.
    /// </summary>
    public static class BladeEdge
    {
        /// <summary>Samples darker than this (HSV value) are ignored: the
        /// ruler's black tick marks and baked shadow say nothing about steel.</summary>
        public const float MinValue = 0.25f;

        /// <summary>
        /// How much a colour looks like bare steel, 0-1: one minus its
        /// saturation. Negative for a colour too dark to judge.
        /// </summary>
        public static float Steeliness(Color colour)
        {
            Color.RGBToHSV(colour, out _, out float saturation, out float value);
            return value < MinValue ? -1f : 1f - saturation;
        }

        /// <summary>
        /// The side of the blade that is steel: +1 if it is at the high end of
        /// <paramref name="offsets"/>, −1 at the low end, 0 if the two sides
        /// can't be told apart.
        /// </summary>
        /// <param name="offsets">Each sample's position across the blade, along
        /// the wide axis, in any units.</param>
        /// <param name="colours">Each sample's colour, same order.</param>
        /// <param name="rimFraction">How much of the blade's width, from each
        /// side, counts as that side's edge (0-0.5). The ruler's steel strip
        /// is about 8% of its width; 0.15 takes the strip and its bevel.</param>
        /// <param name="minDifference">Smallest difference in mean steeliness
        /// that decides a side; below it the result is 0.</param>
        public static int SteelSide(IReadOnlyList<float> offsets, IReadOnlyList<Color> colours,
                                    float rimFraction = 0.15f, float minDifference = 0.1f)
        {
            Score(offsets, colours, rimFraction, out float low, out float high);
            if (low < 0f || high < 0f || Mathf.Abs(high - low) < minDifference) return 0;
            return high > low ? 1 : -1;
        }

        /// <summary>
        /// Mean steeliness of the outermost strip on each side; −1 for a side
        /// with no usable sample. Exposed for the builder's log.
        /// </summary>
        public static void Score(IReadOnlyList<float> offsets, IReadOnlyList<Color> colours, float rimFraction,
                                 out float lowSide, out float highSide)
        {
            lowSide = highSide = -1f;
            if (offsets == null || colours == null) return;
            int count = Mathf.Min(offsets.Count, colours.Count);
            if (count == 0) return;

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                min = Mathf.Min(min, offsets[i]);
                max = Mathf.Max(max, offsets[i]);
            }
            float band = (max - min) * Mathf.Clamp(rimFraction, 0f, 0.5f);
            if (band <= 0f) return;

            float lowSum = 0f, highSum = 0f;
            int lowN = 0, highN = 0;
            for (int i = 0; i < count; i++)
            {
                float s = Steeliness(colours[i]);
                if (s < 0f) continue;
                if (offsets[i] <= min + band) { lowSum += s; lowN++; }
                else if (offsets[i] >= max - band) { highSum += s; highN++; }
            }
            if (lowN > 0) lowSide = lowSum / lowN;
            if (highN > 0) highSide = highSum / highN;
        }
    }
}

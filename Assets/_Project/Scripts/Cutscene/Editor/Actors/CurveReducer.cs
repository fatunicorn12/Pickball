using System;
using System.Collections.Generic;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Keyframe reduction for baked clips: from a curve sampled every frame, keeps only the keys that
    /// smooth (Hermite) interpolation between the kept neighbours can't reproduce within a
    /// tolerance. Plain C#, deterministic: the same samples always give the same keys.
    /// </summary>
    /// <remarks>
    /// A Ramer–Douglas–Peucker-style refinement. Start from the first and last samples (plus any
    /// key the caller must keep, such as a loop seam). Give every kept key the sampled curve's own
    /// local slope (central difference; wrapped across the seam for a looping curve). Evaluate every
    /// dropped sample, and in each segment whose worst sample misses by more than the tolerance, keep
    /// that sample. Repeat until every sample is within the tolerance. With every sample kept the
    /// curve passes through all of them, so the loop always ends. A curve whose samples all lie within
    /// the tolerance of one value becomes a single key. A final pass drops any key whose two
    /// neighbours already reproduce every sample between them.
    /// </remarks>
    public static class CurveReducer
    {
        /// <summary>One reduced key: time, value and Hermite in/out slopes (equal here).</summary>
        public struct Key
        {
            /// <summary>Seconds.</summary>
            public float Time;
            /// <summary>Value.</summary>
            public float Value;
            /// <summary>Incoming slope, per second.</summary>
            public float InSlope;
            /// <summary>Outgoing slope, per second.</summary>
            public float OutSlope;
        }

        /// <summary>
        /// Reduces a sampled curve. <paramref name="times"/> must increase. <paramref name="keep"/>
        /// (optional, same length) marks samples that must stay keys. With <paramref name="loop"/>
        /// the end slopes wrap (the last sample equals the first, as the baker writes loops).
        /// </summary>
        public static Key[] Reduce(IReadOnlyList<float> times, IReadOnlyList<float> values, float tolerance,
                                   IReadOnlyList<bool> keep = null, bool loop = false)
        {
            int n = values.Count;
            if (n == 0) return new Key[0];
            if (times.Count != n) throw new ArgumentException("times and values differ in length");

            float min = values[0], max = values[0];
            for (int i = 1; i < n; i++)
            {
                min = Math.Min(min, values[i]);
                max = Math.Max(max, values[i]);
            }
            bool mustKeepInterior = false;
            if (keep != null)
                for (int i = 1; i < n - 1; i++) mustKeepInterior |= keep[i];
            if (n == 1 || (max - min <= tolerance && !mustKeepInterior))
                return new[] { new Key { Time = times[0], Value = 0.5f * (min + max) } };

            var kept = new bool[n];
            kept[0] = kept[n - 1] = true;
            if (keep != null)
                for (int i = 0; i < n; i++) kept[i] |= keep[i];

            var index = new List<int>(n);
            // Each key's slope is the sampled curve's own local slope (central difference), not the
            // chord to its kept neighbours: the reduced curve then leaves each key along the motion,
            // which fits with far fewer keys (about a third fewer on the baked clips).
            var slope = SampleSlopes(times, values, keep, loop);
            while (true)
            {
                index.Clear();
                for (int i = 0; i < n; i++) if (kept[i]) index.Add(i);

                bool added = false;
                for (int s = 0; s + 1 < index.Count; s++)
                {
                    int a = index[s], b = index[s + 1];
                    int worstAt = Worst(times, values, slope, a, b, tolerance);
                    if (worstAt >= 0)
                    {
                        kept[worstAt] = true;
                        added = true;
                    }
                }
                if (!added) break;
            }

            // Prune: the refinement adds a key per failing segment and can over-key. Each key's slope
            // is fixed by the samples, so dropping a key only changes the one merged segment; drop it
            // when that segment still fits.
            for (int k = 1; k + 1 < index.Count; )
            {
                int i = index[k];
                if (keep != null && keep[i]) { k++; continue; }
                int a = index[k - 1], b = index[k + 1];
                if (Worst(times, values, slope, a, b, tolerance) < 0) index.RemoveAt(k);
                else k++;
            }

            var keys = new Key[index.Count];
            for (int k = 0; k < index.Count; k++)
            {
                int i = index[k];
                keys[k] = new Key { Time = times[i], Value = values[i], InSlope = slope[i], OutSlope = slope[i] };
            }
            return keys;
        }

        /// <summary>
        /// The sample to keep when the segment a..b misses by more than <paramref name="tolerance"/>,
        /// or −1 when it fits. Checks every sample and the quarter points between samples, where the
        /// per-frame curve is the Hermite through neighbouring samples with their own slopes, so the
        /// reduced curve stays within tolerance of the full bake between frames too (a fingertip on
        /// MC_ReachOut's poke missed by 2.04 mm half a frame off).
        /// </summary>
        static int Worst(IReadOnlyList<float> times, IReadOnlyList<float> values, float[] slope, int a, int b, float tolerance)
        {
            float worst = tolerance;
            int worstAt = -1;
            for (int j = a + 1; j <= b; j++)
            {
                if (j < b)
                {
                    float e = Math.Abs(Hermite(times[a], values[a], slope[a], times[b], values[b], slope[b], times[j]) - values[j]);
                    if (e > worst) { worst = e; worstAt = j; }
                }
                for (int q = 1; q <= 3; q++)
                {
                    float tm = times[j - 1] + 0.25f * q * (times[j] - times[j - 1]);
                    float full = Hermite(times[j - 1], values[j - 1], slope[j - 1], times[j], values[j], slope[j], tm);
                    float em = Math.Abs(Hermite(times[a], values[a], slope[a], times[b], values[b], slope[b], tm) - full);
                    if (em > worst)
                    {
                        // Keep the sample beside this point that is inside the segment.
                        int at = q <= 2 ? (j - 1 > a ? j - 1 : (j < b ? j : -1)) : (j < b ? j : (j - 1 > a ? j - 1 : -1));
                        if (at < 0) continue; // a single-frame segment matches the full curve exactly
                        worst = em;
                        worstAt = at;
                    }
                }
            }
            return worstAt;
        }

        /// <summary>Hermite interpolation between two keys (Unity's curve evaluation for non-weighted keys).</summary>
        public static float Hermite(float t0, float v0, float m0, float t1, float v1, float m1, float t)
        {
            float dt = t1 - t0;
            if (dt <= 0f) return v0;
            float u = (t - t0) / dt;
            float u2 = u * u, u3 = u2 * u;
            float h00 = 2f * u3 - 3f * u2 + 1f, h10 = u3 - 2f * u2 + u, h01 = -2f * u3 + 3f * u2, h11 = u3 - u2;
            return h00 * v0 + h10 * dt * m0 + h01 * v1 + h11 * dt * m1;
        }

        /// <summary>Evaluates reduced keys at <paramref name="t"/> (constant outside the range).</summary>
        public static float Evaluate(Key[] keys, float t)
        {
            if (keys.Length == 0) return 0f;
            if (keys.Length == 1 || t <= keys[0].Time) return keys[0].Value;
            for (int k = 1; k < keys.Length; k++)
                if (t <= keys[k].Time)
                    return Hermite(keys[k - 1].Time, keys[k - 1].Value, keys[k - 1].OutSlope,
                                   keys[k].Time, keys[k].Value, keys[k].InSlope, t);
            return keys[keys.Length - 1].Value;
        }

        /// <summary>
        /// The local slope at every sample: central differences, one-sided at the ends, across the
        /// wrap for a loop, and one-sided at a must-keep sample (a seam, where the curve may jump).
        /// </summary>
        static float[] SampleSlopes(IReadOnlyList<float> times, IReadOnlyList<float> values, IReadOnlyList<bool> keep, bool loop)
        {
            int n = values.Count;
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool seam = keep != null && keep[i] && i > 0 && i < n - 1;
                if (i > 0 && i < n - 1 && !seam)
                    s[i] = (values[i + 1] - values[i - 1]) / (times[i + 1] - times[i - 1]);
                else if (seam)
                    s[i] = (values[i + 1] - values[i]) / (times[i + 1] - times[i]);
                else if (loop && n > 2)
                {
                    float span = (times[n - 1] - times[n - 2]) + (times[1] - times[0]);
                    s[i] = span > 0f ? (values[1] - values[n - 2]) / span : 0f;
                }
                else if (i == 0)
                    s[i] = (values[1] - values[0]) / (times[1] - times[0]);
                else
                    s[i] = (values[n - 1] - values[n - 2]) / (times[n - 1] - times[n - 2]);
            }
            return s;
        }
    }
}

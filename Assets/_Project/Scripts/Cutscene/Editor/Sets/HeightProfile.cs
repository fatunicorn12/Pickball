using System;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// A smooth 1-D height curve through knots that never overshoots them (monotone cubic
    /// Hermite, Fritsch–Carlson). Used for the olive hill's rise from the meadow to the crest:
    /// a plain spline would bulge above the crest or dip below the meadow between knots, which
    /// would put markers under the ground or make a slope steeper than the knots promise.
    /// Clamped to the end values outside the knot range.
    /// </summary>
    public sealed class HeightProfile
    {
        private readonly float[] _x;
        private readonly float[] _y;
        private readonly float[] _m;

        /// <summary>Builds the curve. <paramref name="x"/> must be strictly increasing.</summary>
        public HeightProfile(float[] x, float[] y)
        {
            if (x == null || y == null || x.Length != y.Length || x.Length < 2)
                throw new ArgumentException("Need at least two knots with matching x and y.");
            _x = (float[])x.Clone();
            _y = (float[])y.Clone();
            int n = _x.Length;
            var delta = new float[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                float h = _x[i + 1] - _x[i];
                if (h <= 0f) throw new ArgumentException("Knot x values must be strictly increasing.");
                delta[i] = (_y[i + 1] - _y[i]) / h;
            }

            _m = new float[n];
            _m[0] = 0f;          // flat into the first knot (the meadow)
            _m[n - 1] = 0f;      // flat onto the last knot (the crest)
            for (int i = 1; i < n - 1; i++)
                _m[i] = delta[i - 1] * delta[i] <= 0f ? 0f : (delta[i - 1] + delta[i]) * 0.5f;

            for (int i = 0; i < n - 1; i++)
            {
                if (Mathf.Approximately(delta[i], 0f))
                {
                    _m[i] = 0f;
                    _m[i + 1] = 0f;
                    continue;
                }
                float a = _m[i] / delta[i], b = _m[i + 1] / delta[i];
                float s = a * a + b * b;
                if (s > 9f)
                {
                    float tau = 3f / Mathf.Sqrt(s);
                    _m[i] = tau * a * delta[i];
                    _m[i + 1] = tau * b * delta[i];
                }
            }
        }

        /// <summary>Height at <paramref name="x"/>.</summary>
        public float Evaluate(float x)
        {
            int n = _x.Length;
            if (x <= _x[0]) return _y[0];
            if (x >= _x[n - 1]) return _y[n - 1];
            int i = 0;
            while (i < n - 2 && x > _x[i + 1]) i++;
            float h = _x[i + 1] - _x[i];
            float t = (x - _x[i]) / h;
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * _y[i] + (t3 - 2f * t2 + t) * h * _m[i]
                 + (-2f * t3 + 3f * t2) * _y[i + 1] + (t3 - t2) * h * _m[i + 1];
        }
    }
}

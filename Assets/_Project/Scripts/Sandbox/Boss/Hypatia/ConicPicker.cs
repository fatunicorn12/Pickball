using System;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Chooses which conic she fires next and at what eccentricity. Kept out of
    /// the brain so the odds can be tested with a seeded <see cref="Random"/>
    /// instead of in Play mode.
    ///
    /// The kind is a weighted draw (the config weights), with an optional rule
    /// against the same kind twice in a row, because two identical volleys back
    /// to back teach nothing new. The eccentricity is what the astrolabe needle
    /// shows, so it must always name the right shape: exactly 1 for a parabola,
    /// below 1 for an ellipse, above 1 for a hyperbola, whatever the config says.
    /// </summary>
    public sealed class ConicPicker
    {
        /// <summary>Highest ellipse e ever returned (the config's own Range cap). An
        /// orbit at e near 1 would reach far outside the arena.</summary>
        public const float MaxEllipseEccentricity = 0.95f;

        /// <summary>Lowest hyperbola e ever returned (the config's own Min). At 1 it
        /// would read as a parabola on the needle.</summary>
        public const float MinHyperbolaEccentricity = 1.01f;

        const int KindCount = 3;

        readonly Random _random;
        readonly float[] _weights = new float[KindCount];
        ConicKind _last;
        bool _hasLast;

        /// <param name="random">The draw source. Pass a seeded one in tests; null
        /// makes a time-seeded one.</param>
        public ConicPicker(Random random)
        {
            _random = random ?? new Random();
        }

        /// <summary>The last kind picked, if any; the no-repeat rule avoids it.</summary>
        public bool TryGetLast(out ConicKind kind)
        {
            kind = _last;
            return _hasLast;
        }

        /// <summary>Forget the last pick (a new fight).</summary>
        public void Reset() => _hasLast = false;

        /// <summary>Pick with the config's weights and no-repeat rule.</summary>
        public bool TryPick(HypatiaConfig config, out ConicKind kind)
        {
            if (config == null)
            {
                kind = default;
                return false;
            }
            return TryPick(config.ParabolaWeight, config.EllipseWeight, config.HyperbolaWeight,
                           config.NoConicRepeat, out kind);
        }

        /// <summary>
        /// Weighted pick. Negative or NaN weights count as zero. With
        /// <paramref name="noRepeat"/>, the last kind is skipped whenever another
        /// kind has weight; if it is the only one with weight it repeats rather than
        /// leaving her silent. False only when every weight is zero.
        /// </summary>
        public bool TryPick(float parabolaWeight, float ellipseWeight, float hyperbolaWeight,
                            bool noRepeat, out ConicKind kind)
        {
            _weights[(int)ConicKind.Parabola] = Sanitise(parabolaWeight);
            _weights[(int)ConicKind.Ellipse] = Sanitise(ellipseWeight);
            _weights[(int)ConicKind.Hyperbola] = Sanitise(hyperbolaWeight);

            double total = 0d;
            for (int i = 0; i < KindCount; i++) total += _weights[i];

            if (noRepeat && _hasLast)
            {
                float lastWeight = _weights[(int)_last];
                if (total - lastWeight > 0d)
                {
                    _weights[(int)_last] = 0f;
                    total -= lastWeight;
                }
            }

            if (total <= 0d)
            {
                kind = default;
                return false;
            }

            double r = _random.NextDouble() * total;
            int chosen = -1;
            for (int i = 0; i < KindCount; i++)
            {
                if (_weights[i] <= 0f) continue;
                chosen = i;              // the last positive one catches rounding
                if (r < _weights[i]) break;
                r -= _weights[i];
            }

            kind = (ConicKind)chosen;
            _last = kind;
            _hasLast = true;
            return true;
        }

        /// <summary>The needle's e for <paramref name="kind"/>, from the config's ranges.</summary>
        public float PickEccentricity(ConicKind kind, HypatiaConfig config)
        {
            if (config == null) return PickEccentricity(kind, 0.5f, 0.5f, 1.5f, 1.5f, _random);
            return PickEccentricity(kind, config.EllipseEccentricityMin, config.EllipseEccentricityMax,
                                    config.HyperbolaEccentricityMin, config.HyperbolaEccentricityMax,
                                    _random);
        }

        /// <summary>
        /// Parabola: exactly 1. Ellipse: uniform in [<paramref name="ellipseMin"/>,
        /// <paramref name="ellipseMax"/>], kept within [0, <see cref="MaxEllipseEccentricity"/>].
        /// Hyperbola: uniform in [<paramref name="hyperbolaMin"/>, <paramref name="hyperbolaMax"/>],
        /// kept at or above <see cref="MinHyperbolaEccentricity"/>. Reversed bounds are swapped.
        /// </summary>
        public static float PickEccentricity(ConicKind kind, float ellipseMin, float ellipseMax,
                                             float hyperbolaMin, float hyperbolaMax, Random random)
        {
            random ??= new Random();
            switch (kind)
            {
                case ConicKind.Parabola:
                    return 1f;
                case ConicKind.Ellipse:
                    return Uniform(Clamp(ellipseMin, 0f, MaxEllipseEccentricity),
                                   Clamp(ellipseMax, 0f, MaxEllipseEccentricity), random);
                default:
                    return Uniform(Math.Max(MinHyperbolaEccentricity, NaNTo(hyperbolaMin, MinHyperbolaEccentricity)),
                                   Math.Max(MinHyperbolaEccentricity, NaNTo(hyperbolaMax, MinHyperbolaEccentricity)),
                                   random);
            }
        }

        static float Uniform(float a, float b, Random random)
        {
            if (b < a) (a, b) = (b, a);
            return a + (b - a) * (float)random.NextDouble();
        }

        static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? lo : Math.Min(hi, Math.Max(lo, v));

        static float NaNTo(float v, float fallback) => float.IsNaN(v) ? fallback : v;

        static float Sanitise(float w) => float.IsNaN(w) || w < 0f || float.IsInfinity(w) ? 0f : w;
    }
}

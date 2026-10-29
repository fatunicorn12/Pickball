using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// An elliptical orbit timed by Kepler's laws: semi-major axis a, eccentricity
    /// e, period T, starting at periapsis at t = 0. Position comes from solving
    /// Kepler's equation M = E − e sin E for the eccentric anomaly E, so an orbiter
    /// sweeps equal areas in equal times: it whips past her at periapsis and
    /// lingers at apoapsis, which is the readable rhythm the ellipse volley is built
    /// on (the player crosses an orbit where it is slow).
    ///
    /// Perifocal frame: focus at the origin, periapsis on +X, motion
    /// counter-clockwise (+Y). <see cref="ConicPlane"/> places it in the world.
    /// Plain C#, allocation-free; maths in double.
    /// </summary>
    public readonly struct KeplerOrbit
    {
        /// <summary>Newton iterations never exceed this. At e ≤ 0.95 with the
        /// starting guesses below, convergence takes well under ten.</summary>
        public const int MaxIterations = 30;

        /// <summary>Iteration stops when the step in E is below this, rad.</summary>
        public const double Tolerance = 1e-6;

        readonly double _a, _e, _period;

        /// <param name="semiMajor">a, m (&gt; 0).</param>
        /// <param name="eccentricity">e, clamped to [0, 0.99] so the orbit stays closed.</param>
        /// <param name="period">T, s (&gt; 0).</param>
        public KeplerOrbit(float semiMajor, float eccentricity, float period)
        {
            _a = System.Math.Max(1e-6, semiMajor);
            _e = System.Math.Min(0.99, System.Math.Max(0.0, eccentricity));
            _period = System.Math.Max(1e-4, period);
        }

        public float SemiMajor => (float)_a;
        public float Eccentricity => (float)_e;
        public float Period => (float)_period;

        /// <summary>b = a√(1−e²), m.</summary>
        public float SemiMinor => (float)(_a * System.Math.Sqrt(1.0 - _e * _e));

        /// <summary>a(1−e), m: the closest pass to the focus.</summary>
        public float Periapsis => (float)(_a * (1.0 - _e));

        /// <summary>a(1+e), m: the furthest reach.</summary>
        public float Apoapsis => (float)(_a * (1.0 + _e));

        /// <summary>Mean anomaly 2πt/T, wrapped to [0, 2π).</summary>
        public double MeanAnomalyAt(double time)
        {
            double m = 2.0 * System.Math.PI * (time / _period);
            m %= 2.0 * System.Math.PI;
            if (m < 0.0) m += 2.0 * System.Math.PI;
            return m;
        }

        /// <summary>
        /// Solve M = E − e sin E for E by Newton's method. Starts at E = M for
        /// e &lt; 0.8 and at E = π otherwise (the standard guess that keeps Newton
        /// from overshooting near periapsis of a thin orbit). Bounded by
        /// <see cref="MaxIterations"/>, so a bad input can never hang a frame.
        /// </summary>
        public static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity, out int iterations)
        {
            double twoPi = 2.0 * System.Math.PI;
            double m = meanAnomaly % twoPi;
            if (m < 0.0) m += twoPi;

            double e = eccentricity;
            double E = e < 0.8 ? m : System.Math.PI;
            iterations = 0;
            while (iterations < MaxIterations)
            {
                iterations++;
                double f = E - e * System.Math.Sin(E) - m;
                double fp = 1.0 - e * System.Math.Cos(E);
                double step = f / fp; // fp ≥ 1 − e > 0 for a closed orbit
                E -= step;
                if (System.Math.Abs(step) < Tolerance) break;
            }
            return E;
        }

        /// <summary>Eccentric anomaly at <paramref name="time"/> seconds after periapsis.</summary>
        public double EccentricAnomalyAt(double time) =>
            SolveEccentricAnomaly(MeanAnomalyAt(time), _e, out _);

        /// <summary>True anomaly (angle from periapsis seen from the focus), rad in (−π, π].</summary>
        public float TrueAnomalyAt(float time)
        {
            double E = EccentricAnomalyAt(time);
            return (float)(2.0 * System.Math.Atan2(System.Math.Sqrt(1.0 + _e) * System.Math.Sin(E * 0.5),
                                                    System.Math.Sqrt(1.0 - _e) * System.Math.Cos(E * 0.5)));
        }

        /// <summary>r = a(1 − e cos E), m.</summary>
        public float RadiusAt(float time) =>
            (float)(_a * (1.0 - _e * System.Math.Cos(EccentricAnomalyAt(time))));

        /// <summary>Perifocal position at <paramref name="time"/>: (a(cos E − e), b sin E).</summary>
        public Vector2 PositionAt(float time)
        {
            double E = EccentricAnomalyAt(time);
            double b = _a * System.Math.Sqrt(1.0 - _e * _e);
            return new Vector2((float)(_a * (System.Math.Cos(E) - _e)), (float)(b * System.Math.Sin(E)));
        }

        /// <summary>
        /// Perifocal velocity at <paramref name="time"/>, m/s, analytic:
        /// dE/dt = n / (1 − e cos E) with n = 2π/T, so v = (−a sin E, b cos E) dE/dt.
        /// The Orbital Tether reads this through <see cref="IOrbitalAnchor.Velocity"/>.
        /// </summary>
        public Vector2 VelocityAt(float time)
        {
            double E = EccentricAnomalyAt(time);
            double n = 2.0 * System.Math.PI / _period;
            double dE = n / (1.0 - _e * System.Math.Cos(E));
            double b = _a * System.Math.Sqrt(1.0 - _e * _e);
            return new Vector2((float)(-_a * System.Math.Sin(E) * dE), (float)(b * System.Math.Cos(E) * dE));
        }
    }
}

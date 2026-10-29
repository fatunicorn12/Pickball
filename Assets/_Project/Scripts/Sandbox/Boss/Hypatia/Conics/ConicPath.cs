using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// One conic section in polar form about a focus: r(θ) = p / (1 + e cos θ),
    /// with the semi-latus rectum p = q(1 + e) taken from the periapsis distance q.
    /// θ (the true anomaly) is measured from the periapsis direction, which is +X
    /// of the perifocal frame; the focus is the origin.
    ///
    /// Why polar about the focus rather than the textbook centred forms: she sits
    /// at the focus of every conic she fires, and one formula covers all three
    /// kinds, so the needle's single number e really does decide the shape. The
    /// guarantees the fight leans on are the ones the tests pin:
    /// <list type="bullet">
    /// <item>e = 0 is a circle of radius q; 0 &lt; e &lt; 1 reaches q(1+e)/(1−e) at apoapsis.</item>
    /// <item>A parabola is defined for |θ| &lt; π and runs off to infinity as θ → π.</item>
    /// <item>A hyperbola is defined for |θ| &lt; θ∞ = acos(−1/e) and every point of it
    /// lies strictly between its asymptotes, inside the wedge that holds the focus:
    /// the flyby can be dodged by reading the two lines on the floor.</item>
    /// </list>
    /// Plain C#; maths in double, results in float for Unity.
    /// </summary>
    public readonly struct ConicPath
    {
        /// <summary>|e − 1| at or below this counts as a parabola, so a needle that
        /// shows "1.00" never produces a hairline ellipse or hyperbola.</summary>
        public const float ParabolaTolerance = 1e-4f;

        readonly double _q;
        readonly double _e;

        /// <param name="periapsis">Closest distance to the focus, m (q &gt; 0).</param>
        /// <param name="eccentricity">e ≥ 0. Negative values are treated as 0.</param>
        public ConicPath(float periapsis, float eccentricity)
        {
            _q = System.Math.Max(1e-6, periapsis);
            _e = System.Math.Max(0.0, eccentricity);
        }

        /// <summary>Periapsis distance q, m.</summary>
        public float Periapsis => (float)_q;

        /// <summary>Eccentricity e.</summary>
        public float Eccentricity => (float)_e;

        /// <summary>Semi-latus rectum p = q(1 + e): the radius at θ = ±90°.</summary>
        public float SemiLatusRectum => (float)P;

        double P => _q * (1.0 + _e);

        public bool IsParabola => System.Math.Abs(_e - 1.0) <= ParabolaTolerance;
        public bool IsEllipse => _e < 1.0 - ParabolaTolerance;
        public bool IsHyperbola => _e > 1.0 + ParabolaTolerance;

        /// <summary>
        /// Largest |θ| the curve exists at: π for an ellipse (every θ is valid) and
        /// for a parabola (exclusive); θ∞ = acos(−1/e) for a hyperbola (exclusive).
        /// </summary>
        public float MaxTrueAnomaly => (float)MaxAnomalyD;

        double MaxAnomalyD => IsHyperbola ? System.Math.Acos(-1.0 / _e) : System.Math.PI;

        /// <summary>The asymptote angle θ∞ of a hyperbola; π otherwise.</summary>
        public float AsymptoteAngle => MaxTrueAnomaly;

        /// <summary>Whether r(θ) is finite and on this branch. Every θ for an ellipse.</summary>
        public bool IsValidAnomaly(float trueAnomaly)
        {
            if (IsEllipse) return true;
            return System.Math.Abs(trueAnomaly) < MaxAnomalyD;
        }

        /// <summary>r(θ), m. Positive infinity where the curve does not exist
        /// (outside the valid range of a parabola or hyperbola).</summary>
        public float RadiusAt(float trueAnomaly)
        {
            if (!IsEllipse && !IsValidAnomaly(trueAnomaly)) return float.PositiveInfinity;
            double denom = 1.0 + _e * System.Math.Cos(trueAnomaly);
            if (denom <= 0.0) return float.PositiveInfinity;
            return (float)(P / denom);
        }

        /// <summary>The point at true anomaly θ in the perifocal frame (focus at the
        /// origin, periapsis on +X). Infinite outside the valid range.</summary>
        public Vector2 PointAt(float trueAnomaly)
        {
            float r = RadiusAt(trueAnomaly);
            if (float.IsPositiveInfinity(r)) return new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            return new Vector2(r * Mathf.Cos(trueAnomaly), r * Mathf.Sin(trueAnomaly));
        }

        /// <summary>Apoapsis distance q(1+e)/(1−e) for an ellipse; infinity for an
        /// open conic.</summary>
        public float Apoapsis => IsEllipse ? (float)(_q * (1.0 + _e) / (1.0 - _e)) : float.PositiveInfinity;

        /// <summary>|a|: the ellipse's semi-major axis q/(1−e), or the hyperbola's
        /// q/(e−1). Infinity for a parabola.</summary>
        public float SemiMajorMagnitude =>
            IsParabola ? float.PositiveInfinity : (float)(_q / System.Math.Abs(1.0 - _e));

        /// <summary>
        /// The geometric centre in the perifocal frame: (−ae, 0) for an ellipse, on
        /// the far side of the focus from periapsis; (q + |a|, 0) for a hyperbola,
        /// beyond the vertex, where its asymptotes cross. Infinite for a parabola.
        /// </summary>
        public Vector2 Centre
        {
            get
            {
                if (IsParabola) return new Vector2(float.PositiveInfinity, 0f);
                double a = _q / System.Math.Abs(1.0 - _e);
                return IsEllipse ? new Vector2((float)(_q - a), 0f) : new Vector2((float)(_q + a), 0f);
            }
        }

        /// <summary>
        /// Hyperbola only: the unit direction, from <see cref="Centre"/>, of the
        /// asymptote the curve leaves along as θ → +θ∞, i.e. (cos θ∞, sin θ∞). A
        /// body moving with increasing θ exits travelling this way.
        /// </summary>
        public Vector2 OutgoingAsymptote
        {
            get
            {
                double t = MaxAnomalyD;
                return new Vector2((float)System.Math.Cos(t), (float)System.Math.Sin(t));
            }
        }

        /// <summary>
        /// Hyperbola only: the unit direction, from <see cref="Centre"/>, of the
        /// asymptote the curve arrives from as θ → −θ∞, i.e. (cos θ∞, −sin θ∞).
        /// It points toward where the body comes FROM; it travels along its negation.
        /// </summary>
        public Vector2 IncomingAsymptote
        {
            get
            {
                double t = MaxAnomalyD;
                return new Vector2((float)System.Math.Cos(t), -(float)System.Math.Sin(t));
            }
        }

        /// <summary>Hyperbola only: b = |a|√(e²−1), the closest either asymptote
        /// line passes to the focus, m.</summary>
        public float ImpactParameter =>
            IsHyperbola ? (float)(_q / (_e - 1.0) * System.Math.Sqrt(_e * _e - 1.0)) : 0f;

        /// <summary>
        /// Hyperbola only: true when <paramref name="perifocalPoint"/> is strictly
        /// inside the wedge between the asymptotes that contains the focus, the
        /// region the near branch can never leave.
        /// </summary>
        public bool IsBetweenAsymptotes(Vector2 perifocalPoint)
        {
            if (!IsHyperbola) return false;
            double t = MaxAnomalyD;
            double cx = _q + _q / (_e - 1.0);
            double wx = perifocalPoint.x - cx, wy = perifocalPoint.y;
            double ox = System.Math.Cos(t), oy = System.Math.Sin(t);
            // Focus side of the outgoing line: cross(out, w) > 0. Focus side of the
            // incoming line (direction (ox, -oy)): cross(in, w) < 0.
            double crossOut = ox * wy - oy * wx;
            double crossIn = ox * wy + oy * wx;
            return crossOut > 0.0 && crossIn < 0.0;
        }

        /// <summary>
        /// Hyperbola only: the point at hyperbolic anomaly F, (|a|(e − cosh F),
        /// |a|√(e²−1) sinh F). F = 0 is periapsis; F &gt; 0 is the outgoing leg.
        /// Used to sample the flyby path, because equal steps in F spread points
        /// along the arms far more evenly than equal steps in θ, which bunch at the
        /// vertex and starve the asymptotic legs.
        /// </summary>
        public Vector2 HyperbolicPoint(float hyperbolicAnomaly)
        {
            double a = _q / (_e - 1.0);
            double f = hyperbolicAnomaly;
            return new Vector2((float)(a * (_e - System.Math.Cosh(f))),
                               (float)(a * System.Math.Sqrt(_e * _e - 1.0) * System.Math.Sinh(f)));
        }

        /// <summary>
        /// Hyperbola only: the non-negative hyperbolic anomaly at which the curve is
        /// <paramref name="radius"/> from the focus (r = |a|(e cosh F − 1)). Radii
        /// at or inside periapsis give 0.
        /// </summary>
        public float HyperbolicAnomalyAtRadius(float radius)
        {
            double a = _q / (_e - 1.0);
            double c = (radius / a + 1.0) / _e;
            if (c <= 1.0) return 0f;
            return (float)System.Math.Log(c + System.Math.Sqrt(c * c - 1.0)); // acosh
        }
    }
}

using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// The hyperbola flyby's geometry: which way to turn it so it is aimed at the
    /// player, the path the shot follows, and the telegraph drawn before it. Plain
    /// C#, EditMode-tested; <see cref="ConicCaster"/> only feeds it numbers.
    /// </summary>
    public static class HyperbolaFlyby
    {
        /// <summary>
        /// The plane angle (see <see cref="ConicPlane.Angle"/>) that makes the
        /// OUTGOING asymptote ray, drawn from the hyperbola's centre, pass through
        /// <paramref name="target"/> (a horizontal offset from the focus: x = world
        /// X, y = world Z). The telegraph's exit line then runs straight through the
        /// player, so the read is "it leaves along that line, at me".
        /// </summary>
        /// <remarks>
        /// In the perifocal frame the outgoing line is C + s·d with |C| = c and
        /// C·d = −|a|, so the point on it at the player's distance D satisfies
        /// s² − 2|a|s + c² − D² = 0, i.e. s = |a| + √(D² − b²) (the root further out,
        /// on the receding side). Turning that point onto the player's bearing gives
        /// the angle. A player nearer than the impact parameter b cannot lie on the
        /// line at all; the closest point of the line is aimed at them instead.
        /// </remarks>
        public static float ExitAngleTowards(ConicPath path, Vector2 target, int sense)
        {
            double d = target.magnitude;
            if (d < 1e-4 || !path.IsHyperbola) return 0f;

            double e = path.Eccentricity, q = path.Periapsis;
            double a = q / (e - 1.0);
            double b = a * System.Math.Sqrt(e * e - 1.0);
            double c = q + a;
            double thetaInf = System.Math.Acos(-1.0 / e);
            double dx = System.Math.Cos(thetaInf), dy = System.Math.Sin(thetaInf);

            double s = a + System.Math.Sqrt(System.Math.Max(0.0, d * d - b * b));
            double qx = c + s * dx, qy = s * dy;
            double alpha = System.Math.Atan2(qy, qx) * (sense < 0 ? -1.0 : 1.0);
            double bearing = System.Math.Atan2(target.y, target.x);
            return (float)(bearing - alpha);
        }

        /// <summary>
        /// Fill <paramref name="into"/> with the flyby path: from
        /// <paramref name="reach"/> metres out on the incoming leg, round periapsis,
        /// out to <paramref name="reach"/> on the outgoing leg, sampled evenly in
        /// hyperbolic anomaly. A reach inside periapsis is raised to just past it so
        /// the path is never empty. Uses the buffer's whole capacity.
        /// </summary>
        public static void FillPath(ConicPolyline into, ConicPath path, ConicPlane plane, float reach)
        {
            into.Clear();
            if (!path.IsHyperbola) return;

            float r = Mathf.Max(reach, path.Periapsis * 1.05f);
            float fMax = path.HyperbolicAnomalyAtRadius(r);
            int n = into.Capacity;
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.Lerp(-fMax, fMax, i / (float)(n - 1));
                into.Add(plane.ToWorld(path.HyperbolicPoint(f)));
            }
        }

        /// <summary>The telegraph for a filled path (see <see cref="HyperbolaTelegraph"/>).</summary>
        public static HyperbolaTelegraph Telegraph(ConicPath path, ConicPlane plane, ConicPolyline filled, float leadSeconds)
        {
            Vector3 entry = filled.Count > 0 ? filled[0] : plane.Focus;
            Vector3 exit = filled.Count > 0 ? filled[filled.Count - 1] : plane.Focus;
            return new HyperbolaTelegraph(
                plane.ToWorld(path.Centre),
                plane.DirectionToWorld(path.IncomingAsymptote).normalized,
                plane.DirectionToWorld(path.OutgoingAsymptote).normalized,
                plane.ToWorld(path.PointAt(0f)),
                plane.Focus, entry, exit, path.Eccentricity, leadSeconds, filled);
        }
    }
}

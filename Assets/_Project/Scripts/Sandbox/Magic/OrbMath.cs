using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The arithmetic of the aimed spell, kept out of <see cref="MagicOrb"/> and
    /// <see cref="SpellCaster"/> so it can be tested without a scene: how hard
    /// the blast hits at a distance, how far the orb may turn toward its target
    /// in one frame, and which way it leaves the hand.
    ///
    /// Everything here is pure: no Unity object state, no time, no physics
    /// queries. The components feed it frame values; OrbMathTests pins the
    /// edge cases (opposite and zero vectors, an aim point behind the hand)
    /// that would otherwise only show up as an orb flying backwards in play.
    /// </summary>
    public static class OrbMath
    {
        const float Epsilon = 1e-6f;

        // Below this dot product (about 179.2°) two directions count as
        // opposite, and Steer picks the turn axis itself.
        const float OppositeDot = -0.9999f;

        /// <summary>
        /// Blast damage at <paramref name="distance"/> metres from the burst
        /// centre: <paramref name="max"/> at the centre, falling linearly to
        /// <paramref name="min"/> at the edge of <paramref name="radius"/>, and 0
        /// outside it.
        ///
        /// The edge still deals <paramref name="min"/> rather than fading to zero
        /// so that being caught by the blast always reads as a hit; the cliff
        /// at the radius is what the burst shell draws. A non-positive radius
        /// means "no blast" and returns 0 everywhere.
        /// </summary>
        public static float BlastDamageAt(float distance, float radius, float max, float min)
        {
            if (radius <= 0f) return 0f;
            if (distance < 0f) distance = 0f;
            if (distance > radius) return 0f;
            return Mathf.Lerp(max, min, distance / radius);
        }

        /// <summary>
        /// Turns <paramref name="direction"/> toward <paramref name="toTarget"/> by
        /// at most <paramref name="maxDegrees"/>, reaching it exactly when it is
        /// within that angle. Always returns a unit vector.
        ///
        /// Degenerate input never produces a zero or NaN direction, which would
        /// freeze the orb in mid-air: a zero <paramref name="direction"/> adopts
        /// the target direction (or world forward if both are zero), a zero
        /// <paramref name="toTarget"/> or non-positive <paramref name="maxDegrees"/>
        /// leaves the direction unchanged, and a target straight behind turns
        /// about a perpendicular axis (horizontal where possible) instead of
        /// stalling on a cross product of zero.
        /// </summary>
        public static Vector3 Steer(Vector3 direction, Vector3 toTarget, float maxDegrees)
        {
            bool hasTarget = toTarget.sqrMagnitude > Epsilon;
            if (direction.sqrMagnitude <= Epsilon)
                return hasTarget ? toTarget.normalized : Vector3.forward;

            Vector3 current = direction.normalized;
            if (!hasTarget || maxDegrees <= 0f) return current;

            Vector3 target = toTarget.normalized;
            if (maxDegrees >= 180f) return target;

            if (Vector3.Dot(current, target) < OppositeDot)
            {
                // Every turn axis is equally short. Prefer a sideways turn:
                // about world up, made perpendicular to the travel. Travelling
                // straight up or down, any perpendicular will do.
                Vector3 axis = Vector3.ProjectOnPlane(Vector3.up, current);
                if (axis.sqrMagnitude < Epsilon) axis = Vector3.Cross(current, Vector3.right);
                return (Quaternion.AngleAxis(maxDegrees, axis.normalized) * current).normalized;
            }

            Vector3 turned = Vector3.RotateTowards(current, target, maxDegrees * Mathf.Deg2Rad, 0f);
            return turned.sqrMagnitude > Epsilon ? turned.normalized : current;
        }

        /// <summary>
        /// The direction an orb leaves the hand at <paramref name="origin"/> to fly
        /// to <paramref name="aimPoint"/>, the world point under the crosshair.
        ///
        /// The hand is offset from the camera, so normally the orb flies from the
        /// hand to the point the camera sees (parallax-correct). Two cases fall
        /// back to <paramref name="cameraForward"/> instead:
        ///  - the aim point is closer to the hand than
        ///    <paramref name="minAimDistance"/> — aiming at a wall you are hugging,
        ///    where hand→point swings wildly with every millimetre of movement;
        ///  - the aim point is level with or behind the hand along the camera's
        ///    view — the crosshair is on something between the camera and the
        ///    player, and hand→point would fire the orb back into the lens.
        /// Always returns a unit vector (world forward if the camera forward is zero).
        /// </summary>
        public static Vector3 LaunchDirection(Vector3 origin, Vector3 aimPoint, Vector3 cameraForward,
                                              float minAimDistance)
        {
            Vector3 fallback = cameraForward.sqrMagnitude > Epsilon ? cameraForward.normalized : Vector3.forward;

            Vector3 to = aimPoint - origin;
            float distance = to.magnitude;
            if (distance <= Mathf.Max(minAimDistance, Epsilon)) return fallback;

            Vector3 direction = to / distance;
            if (Vector3.Dot(direction, fallback) <= 0f) return fallback;
            return direction;
        }

        /// <summary>
        /// The direction an orb leaves the hand at <paramref name="origin"/> when
        /// the crosshair is on a target: straight at the target's body centre
        /// <paramref name="targetCentre"/>, at any distance. Unlike
        /// <see cref="LaunchDirection"/> there is no near-aim fallback, because at
        /// point-blank range camera forward points at the floor and the orb would
        /// burst there instead of on the enemy. Only a target centred on the hand
        /// itself falls back to <paramref name="cameraForward"/> (or world forward).
        /// </summary>
        public static Vector3 TargetLaunchDirection(Vector3 origin, Vector3 targetCentre, Vector3 cameraForward)
        {
            Vector3 to = targetCentre - origin;
            if (to.sqrMagnitude > Epsilon) return to.normalized;
            return cameraForward.sqrMagnitude > Epsilon ? cameraForward.normalized : Vector3.forward;
        }
    }
}

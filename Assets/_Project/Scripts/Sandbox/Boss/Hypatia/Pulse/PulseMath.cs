using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Pulse
{
    /// <summary>
    /// The knockback pulse's numbers, in plain C# so they are tested without a
    /// scene: who it reaches, and how hard to push them so they stop where the
    /// design wants.
    ///
    /// <b>Why the push is solved, not tuned.</b> The chibi's carried momentum
    /// decays LINEARLY (<c>MovementMotor</c>: <c>Vector3.MoveTowards</c> at Momentum
    /// Ground Drag k m/s² on the ground), so an impulse v slides exactly
    /// v² / (2k) metres before it is gone. To land a player standing d metres out
    /// at the target radius R, push with v = sqrt(2k(R − d)). That only holds while
    /// k matches the movement config, hence the copy on HypatiaConfig and the
    /// EditMode test that fails if they drift. In the air the drag is lower
    /// (1.6 m/s²), so a player pulsed mid-jump flies further, up to the wall.
    /// </summary>
    public static class PulseMath
    {
        /// <summary>Closer than this, m, the player is "on top of her" and the
        /// fallback direction is used.</summary>
        public const float MinDistance = 1e-3f;

        /// <summary>Horizontal distance between two points, m (height ignored).</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Inside the pulse: horizontal distance strictly below
        /// <paramref name="reach"/>. Height never matters (it cannot be jumped).</summary>
        public static bool InReach(float distance, float reach) =>
            !float.IsNaN(distance) && distance < reach;

        /// <summary>
        /// Push speed, m/s, that slides a grounded player from
        /// <paramref name="distance"/> to <paramref name="targetRadius"/> against
        /// <paramref name="groundDrag"/> m/s², plus <paramref name="extraSpeed"/>.
        /// A player already at or past the target gets only the extra.
        /// </summary>
        public static float PushSpeed(float distance, float targetRadius, float groundDrag, float extraSpeed)
        {
            float gap = Mathf.Max(0f, targetRadius - Mathf.Max(0f, distance));
            float k = Mathf.Max(0f, groundDrag);
            return Mathf.Sqrt(2f * k * gap) + Mathf.Max(0f, extraSpeed);
        }

        /// <summary>How far a push of <paramref name="speed"/> slides on the ground, m.</summary>
        public static float SlideDistance(float speed, float groundDrag) =>
            groundDrag > 0f ? speed * speed / (2f * groundDrag) : float.PositiveInfinity;

        /// <summary>
        /// The horizontal unit direction from her to the player. When they are on
        /// top of her (closer than <see cref="MinDistance"/>), the player's own
        /// back (<c>−playerForward</c>, flattened), then <paramref name="herForward"/>,
        /// then world +Z: always a unit vector, never zero or NaN.
        /// </summary>
        public static Vector3 PushDirection(Vector3 her, Vector3 player, Vector3 playerForward, Vector3 herForward)
        {
            var away = new Vector3(player.x - her.x, 0f, player.z - her.z);
            if (away.sqrMagnitude > MinDistance * MinDistance) return away.normalized;
            var back = new Vector3(-playerForward.x, 0f, -playerForward.z);
            if (back.sqrMagnitude > 1e-6f) return back.normalized;
            var forward = new Vector3(herForward.x, 0f, herForward.z);
            if (forward.sqrMagnitude > 1e-6f) return forward.normalized;
            return Vector3.forward;
        }

        /// <summary>
        /// The whole rule: false (no hit) when the player is at or beyond
        /// <paramref name="reach"/>; otherwise the horizontal push velocity to hand
        /// to <c>ChibiLocomotion.AddMomentum</c> (y is always 0).
        /// </summary>
        public static bool TryComputePush(Vector3 her, Vector3 player, Vector3 playerForward, Vector3 herForward,
                                          float reach, float targetRadius, float groundDrag, float extraSpeed,
                                          out Vector3 push)
        {
            float d = HorizontalDistance(her, player);
            if (!InReach(d, reach))
            {
                push = Vector3.zero;
                return false;
            }
            push = PushDirection(her, player, playerForward, herForward) *
                   PushSpeed(d, targetRadius, groundDrag, extraSpeed);
            return true;
        }
    }
}

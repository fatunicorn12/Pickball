using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How one air swing carries the player to its target, Kingdom Hearts II
    /// style. Worked out once as the swing starts and applied through
    /// <see cref="ICombatImpulses"/> as events, never as a per-frame push:
    ///
    ///  - <b>Across:</b> one impulse, then a stop after <see cref="TravelSeconds"/>.
    ///    The stop is the point of the plan. Momentum barely decays in the air, so
    ///    a lone impulse carries him straight past the target.
    ///  - <b>Up or down:</b> one vertical speed, chosen so the swing's hover (which
    ///    eases vertical speed to rest at a known rate) brings him to rest where the
    ///    blade can reach the target. Stopping from v at rate a covers v²/2a, so
    ///    v = √(2a·Δh).
    ///
    /// The plan aims at where the target is now, with no prediction. A juggled body
    /// is moved by each hit, and the next swing re-aims. Plain maths, so it is
    /// EditMode-testable.
    /// </summary>
    public readonly struct AirHomingPlan
    {
        /// <summary>False when the target is out of range: the swing doesn't home.</summary>
        public readonly bool IsValid;

        /// <summary>Horizontal velocity to give him, in m/s. Zero when he is already
        /// close enough.</summary>
        public readonly Vector3 Velocity;

        /// <summary>Seconds until he has closed the gap and should be stopped.</summary>
        public readonly float TravelSeconds;

        /// <summary>True when <see cref="VerticalSpeed"/> should be applied.</summary>
        public readonly bool SetsVertical;

        /// <summary>Vertical speed to give him, in m/s; negative is down.</summary>
        public readonly float VerticalSpeed;

        AirHomingPlan(Vector3 velocity, float travelSeconds, bool setsVertical, float verticalSpeed)
        {
            IsValid = true;
            Velocity = velocity;
            TravelSeconds = travelSeconds;
            SetsVertical = setsVertical;
            VerticalSpeed = verticalSpeed;
        }

        /// <param name="settings">Tuning.</param>
        /// <param name="feet">The player's position (his feet), world space.</param>
        /// <param name="target">The target's chest, world space.</param>
        /// <param name="targetRadius">The target's horizontal radius, m.</param>
        /// <param name="arriveSeconds">How soon he should arrive: the blade's live moment.</param>
        /// <param name="haltRate">The rate the hover eases vertical speed to rest, m/s². 0
        /// or less leaves his height alone.</param>
        /// <param name="mayChangeHeight">False for a swing that manages its own vertical
        /// speed (a lift, a slam, one that doesn't hover).</param>
        public static AirHomingPlan Solve(in AirHomingSettings settings, Vector3 feet, Vector3 target,
                                          float targetRadius, float arriveSeconds, float haltRate,
                                          bool mayChangeHeight)
        {
            Vector3 to = target - (feet + Vector3.up * settings.ChestHeight);
            if (settings.Range <= 0f || to.magnitude > settings.Range) return default;

            Vector3 velocity = Vector3.zero;
            float travelSeconds = 0f;
            Vector3 flat = new(to.x, 0f, to.z);
            float distance = flat.magnitude;
            float gap = distance - Mathf.Max(0f, targetRadius) - settings.StopGap;
            if (gap > 0f && distance > 1e-3f)
            {
                float speed = Mathf.Min(gap / Mathf.Max(0.01f, arriveSeconds), settings.MaxSpeed);
                velocity = flat / distance * speed;
                travelSeconds = gap / speed;
            }

            float change = mayChangeHeight && haltRate > 0f ? HeightChange(settings, to.y) : 0f;
            bool setsVertical = change != 0f;
            float verticalSpeed = setsVertical
                ? Mathf.Sign(change) * Mathf.Sqrt(2f * haltRate * Mathf.Abs(change))
                : 0f;
            return new AirHomingPlan(velocity, travelSeconds, setsVertical, verticalSpeed);
        }

        /// <summary>The height to gain (or, negative, lose) for a target
        /// <paramref name="above"/> metres above his chest. Up goes all the way to its
        /// height. Down goes only to within <see cref="AirHomingSettings.DropTolerance"/>
        /// of it, because the blade reaches below his chest and a juggled body is
        /// usually rising to meet him.</summary>
        static float HeightChange(in AirHomingSettings settings, float above)
        {
            float change = above > settings.HeightTolerance ? above
                         : above < -settings.DropTolerance ? above + settings.DropTolerance
                         : 0f;
            return Mathf.Clamp(change, -settings.MaxHeightChange, settings.MaxHeightChange);
        }
    }
}

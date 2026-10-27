using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// An optional radial strike a step releases partway through its swing —
    /// the Kingdom Hearts "Explosion" / "Ripple Drive" family, and the hook every
    /// area finisher rides on.
    ///
    /// It is a growing ring rather than an instant sphere so it can be read and
    /// jumped: a target is struck the moment the expanding edge reaches it, once.
    /// The maths lives here, as pure functions, so it is EditMode-testable;
    /// <c>MeleeCombat</c> runs the ring and its presentation listener draws it.
    /// </summary>
    [Serializable]
    public struct AreaBurst
    {
        [Tooltip("Off = this step has no burst; everything below is ignored.")]
        [SerializeField] private bool enabled;

        [Tooltip("Fraction of the swing (0-1) at which the ring is released. " +
                 "Line it up with the clip's impact frame (usually activeEnd).")]
        [Range(0f, 1f)] [SerializeField] private float triggerAt;

        [Tooltip("Final radius of the ring, in metres, measured on the ground.")]
        [Min(0f)] [SerializeField] private float radius;

        [Tooltip("Seconds for the ring to grow to full radius. 0 = instant. " +
                 "~0.3 s reads as a wave you could have jumped.")]
        [Min(0f)] [SerializeField] private float expandSeconds;

        [Tooltip("Metres in front of the player the ring is centred. 0 = on the " +
                 "player (a spin); ~1 m = where the blade lands (a slam).")]
        [SerializeField] private float forwardOffset;

        [Tooltip("Half-height of the ring's band, in metres: a target whose " +
                 "closest point is further above or below the centre than this " +
                 "is missed. Small for a ground wave, large for an air spin.")]
        [Min(0.05f)] [SerializeField] private float heightTolerance;

        [Tooltip("What the ring does to each target it reaches (damage in HP, " +
                 "knockback outward in m/s, launch upward in m/s).")]
        [SerializeField] private HitSettings hit;

        public bool Enabled => enabled;
        public float TriggerAt => triggerAt;
        public float Radius => radius;
        public float ExpandSeconds => expandSeconds;
        public float ForwardOffset => forwardOffset;
        public float HeightTolerance => heightTolerance;
        public HitSettings Hit => hit;

        public AreaBurst(float triggerAt, float radius, float expandSeconds, HitSettings hit,
                         float forwardOffset = 0f, float heightTolerance = 0.6f)
        {
            enabled = true;
            this.triggerAt = Mathf.Clamp01(triggerAt);
            this.radius = Mathf.Max(0f, radius);
            this.expandSeconds = Mathf.Max(0f, expandSeconds);
            this.forwardOffset = forwardOffset;
            this.heightTolerance = Mathf.Max(0.05f, heightTolerance);
            this.hit = hit;
        }

        /// <summary>Ring radius, in metres, <paramref name="elapsed"/> seconds after
        /// release. Eases out — fast off the blade, slowing as it spreads — which
        /// reads as an impact rather than a scanner sweep.</summary>
        public float RadiusAt(float elapsed) => radius * GrowthFraction(elapsed, expandSeconds);

        /// <summary>The 0-1 growth curve shared by the damage ring and its
        /// visual, so the drawn edge is exactly where targets are being struck.</summary>
        public static float GrowthFraction(float elapsed, float expandSeconds)
        {
            if (expandSeconds <= 0f) return 1f;
            float t = Mathf.Clamp01(elapsed / expandSeconds);
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>True once the ring has reached full size and can be retired.</summary>
        public bool IsFinished(float elapsed) => elapsed >= expandSeconds;

        /// <summary>Whether a target whose closest point is <paramref name="point"/>
        /// is inside the ring's band when its edge is at <paramref name="currentRadius"/>.</summary>
        public bool Reaches(Vector3 centre, Vector3 point, float currentRadius)
        {
            if (Mathf.Abs(point.y - centre.y) > heightTolerance) return false;
            float dx = point.x - centre.x, dz = point.z - centre.z;
            return dx * dx + dz * dz <= currentRadius * currentRadius;
        }

        /// <summary>The ring's centre for an attacker standing at
        /// <paramref name="origin"/> and facing <paramref name="forward"/>.</summary>
        public Vector3 CentreFor(Vector3 origin, Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-6f ? origin + forward.normalized * forwardOffset : origin;
        }
    }
}

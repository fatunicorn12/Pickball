using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// The player's velocity as she sees it: a smoothed finite difference of
    /// position, so the parabola can lead the player without the boss reading
    /// any movement code (the dependency rule in CodingStandards §1). Plain C#.
    ///
    /// Smoothing is exponential and frame-rate independent; a teleport (respawn)
    /// shows up as one huge sample, so samples above <see cref="MaxPlausibleSpeed"/>
    /// reset the estimate instead of feeding it.
    /// </summary>
    public sealed class VelocityTracker
    {
        /// <summary>Above this, m/s, a sample is treated as a teleport.</summary>
        public const float MaxPlausibleSpeed = 40f;

        readonly float _smoothingRate;
        Vector3 _last;
        bool _hasSample;

        /// <param name="smoothingRate">1/s. Higher follows changes faster but
        /// jitters more; 10 settles in about a tenth of a second.</param>
        public VelocityTracker(float smoothingRate = 10f)
        {
            _smoothingRate = Mathf.Max(0.01f, smoothingRate);
        }

        /// <summary>Smoothed velocity, m/s. Zero until two samples exist.</summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>Feed this frame's position. A dt of zero or less is ignored.</summary>
        public void Sample(Vector3 position, float dt)
        {
            if (!_hasSample || dt <= 0f)
            {
                if (!_hasSample) { _last = position; _hasSample = true; }
                return;
            }

            Vector3 raw = (position - _last) / dt;
            _last = position;
            if (raw.sqrMagnitude > MaxPlausibleSpeed * MaxPlausibleSpeed || float.IsNaN(raw.x))
            {
                Velocity = Vector3.zero;
                return;
            }

            float k = 1f - Mathf.Exp(-_smoothingRate * dt);
            Velocity += (raw - Velocity) * k;
        }

        /// <summary>Forget everything (respawn, scene reset).</summary>
        public void Reset()
        {
            _hasSample = false;
            Velocity = Vector3.zero;
        }
    }
}

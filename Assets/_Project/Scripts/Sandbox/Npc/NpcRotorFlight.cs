using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// The path of a rotor knocked off a dying NPC: an arc from where it was on
    /// the head to a spot on the ground, a whole number of somersaults on the
    /// way, a small hop on landing, and a spin that winds down to a stop.
    ///
    /// <b>Why.</b> The rotor used to follow the head, and a body lying on its
    /// back turns the head's up horizontal, so the blades stood upright out of
    /// the floor. Off the head, it can lie the right way up.
    ///
    /// <b>Guarantees.</b> It starts exactly at the start pose and ends exactly
    /// upright (the rotor's up is world up) at the landing spot, so a rotor
    /// whose origin is the bottom of its mast rests with no part below the
    /// ground. It is upright before it comes down (by
    /// <see cref="UprightBy"/> of the flight) and the somersaults are whole
    /// turns, so it never lands on its edge or upside down. Pure: a function of
    /// time and the landing spot, which the caller may keep moving until
    /// touchdown (the head is still settling while the rotor is in the air).
    /// </summary>
    public sealed class NpcRotorFlight
    {
        /// <summary>Share of the flight by which the rotor has turned upright
        /// and finished its somersaults, so the last stretch of the descent is
        /// level and the blade tips don't clip the ground.</summary>
        public const float UprightBy = 0.8f;

        readonly Vector3 _from;
        readonly Quaternion _fromTilt;
        readonly Quaternion _upright;
        readonly Vector3 _tumbleAxis;
        readonly float _seconds, _height, _turns, _bounceHeight, _bounceSeconds, _spin, _spinDown;

        /// <param name="from">Where the rotor's origin is when it is knocked off.</param>
        /// <param name="fromTilt">Its rotation then, without its spin.</param>
        /// <param name="firstLanding">Where it is expected to land, to aim the somersaults.</param>
        /// <param name="seconds">Time in the air, s.</param>
        /// <param name="height">Height of the arc above the straight line, m.</param>
        /// <param name="turns">Whole somersaults.</param>
        /// <param name="bounceHeight">Height of the hop on landing, m (0 = none).</param>
        /// <param name="bounceSeconds">Length of that hop, s.</param>
        /// <param name="spin">Spin while airborne, °/s.</param>
        /// <param name="spinDownSeconds">Time from landing to a standstill, s.</param>
        public NpcRotorFlight(Vector3 from, Quaternion fromTilt, Vector3 firstLanding, float seconds, float height,
                              int turns, float bounceHeight, float bounceSeconds, float spin, float spinDownSeconds)
        {
            _from = from;
            _fromTilt = fromTilt;
            _upright = Upright(fromTilt);
            _seconds = Mathf.Max(0.01f, seconds);
            _height = Mathf.Max(0f, height);
            _turns = Mathf.Max(0, turns);
            _bounceHeight = Mathf.Max(0f, bounceHeight);
            _bounceSeconds = Mathf.Max(0.01f, bounceSeconds);
            _spin = spin;
            _spinDown = Mathf.Max(0f, spinDownSeconds);

            // Somersault forward, over the direction of travel.
            Vector3 travel = firstLanding - from;
            travel.y = 0f;
            _tumbleAxis = travel.sqrMagnitude > 1e-6f
                ? Vector3.Cross(Vector3.up, travel.normalized)
                : fromTilt * Vector3.right;
        }

        /// <summary>Time from the knock to touchdown, s.</summary>
        public float LandingTime => _seconds;

        /// <summary>Time from the knock until nothing moves any more, s.</summary>
        public float SettledTime => _seconds + Mathf.Max(_bounceHeight > 0f ? _bounceSeconds : 0f, _spinDown);

        /// <summary>Whether the rotor has touched down by <paramref name="t"/>.</summary>
        public bool HasLanded(float t) => t >= _seconds;

        /// <summary><paramref name="tilt"/> turned the least amount that makes
        /// its up point straight up (its heading is kept).</summary>
        public static Quaternion Upright(Quaternion tilt)
            => Quaternion.FromToRotation(tilt * Vector3.up, Vector3.up) * tilt;

        /// <summary>
        /// Where the rotor's origin is, and how it is tilted (spin not
        /// included), <paramref name="t"/> s after the knock, landing at
        /// <paramref name="landing"/>.
        /// </summary>
        public void Sample(float t, Vector3 landing, out Vector3 position, out Quaternion tilt)
        {
            if (t < _seconds)
            {
                float s = Mathf.Max(0f, t) / _seconds;
                position = Vector3.Lerp(_from, landing, s) + Vector3.up * (4f * _height * s * (1f - s));
                float u = Mathf.Clamp01(s / UprightBy);
                u = u * u * (3f - 2f * u);
                tilt = Quaternion.AngleAxis(360f * _turns * u, _tumbleAxis) * Quaternion.Slerp(_fromTilt, _upright, u);
                return;
            }

            position = landing;
            tilt = _upright;
            float b = (t - _seconds) / _bounceSeconds;
            if (_bounceHeight > 0f && b < 1f) position += Vector3.up * (4f * _bounceHeight * b * (1f - b));
        }

        /// <summary>Spin, °/s, <paramref name="t"/> s after the knock: the
        /// airborne spin, then a steady wind-down to zero after landing.</summary>
        public float SpinAt(float t)
        {
            if (t < _seconds) return _spin;
            if (_spinDown <= 0f) return 0f;
            return _spin * Mathf.Clamp01(1f - (t - _seconds) / _spinDown);
        }
    }
}

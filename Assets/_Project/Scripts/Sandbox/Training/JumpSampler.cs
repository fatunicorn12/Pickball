using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Measures a jump for the training readout. Take-off is the LAST GROUNDED
    /// sample, not the first airborne one: a controller reports "not grounded"
    /// only after it has already moved off the floor, so latching on that frame
    /// drops the first slice of the rise and the carry (the readout showed
    /// 2.41 m where the harness measured 2.57 m). The apex is the highest
    /// sample seen. Call <see cref="Sample"/> once per tick after the motor has
    /// moved (LateUpdate) so the end-of-tick position is what gets seen.
    /// </summary>
    public sealed class JumpSampler
    {
        public readonly struct Result
        {
            public readonly float Rise, Carry, AirTime;
            public Result(float rise, float carry, float airTime) { Rise = rise; Carry = carry; AirTime = airTime; }
        }

        /// <summary>Air time below this is a stumble off a step, not a jump.</summary>
        public float MinAirTime { get; set; }
        public bool Airborne { get; private set; }

        Vector3 _lastGrounded;
        float _lastGroundedTime;
        bool _hasGrounded;
        float _apexY;

        public JumpSampler(float minAirTime) { MinAirTime = minAirTime; }

        /// <summary>Forget any jump in progress (after a teleport).</summary>
        public void Reset() { Airborne = false; _hasGrounded = false; }

        /// <summary>Returns true and fills <paramref name="result"/> when a jump just landed.</summary>
        public bool Sample(Vector3 pos, bool grounded, float now, out Result result)
        {
            result = default;
            if (grounded)
            {
                bool counted = Airborne && now - _lastGroundedTime >= MinAirTime;
                if (counted)
                {
                    result = new Result(
                        _apexY - _lastGrounded.y,
                        Vector2.Distance(new Vector2(_lastGrounded.x, _lastGrounded.z), new Vector2(pos.x, pos.z)),
                        now - _lastGroundedTime);
                }
                Airborne = false;
                _lastGrounded = pos;
                _lastGroundedTime = now;
                _hasGrounded = true;
                return counted;
            }

            if (!Airborne)
            {
                if (!_hasGrounded) { _lastGrounded = pos; _lastGroundedTime = now; }   // spawned mid-air
                Airborne = true;
                _apexY = pos.y;
            }
            else _apexY = Mathf.Max(_apexY, pos.y);
            return false;
        }
    }
}

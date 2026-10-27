using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Auto-sprint timing: after <c>delay</c> seconds of continuous running the
    /// character ramps from run speed to sprint speed over <c>rampTime</c>.
    ///
    /// "Continuous" forgives short gaps (<c>grace</c>): a stick flicked through
    /// neutral on its way to a reversal, or a keyboard finger lifting between
    /// keys, must not throw away a second of build-up. That is also what lets
    /// the skid turn come out the other side still sprinting — momentum kept
    /// means the sprint is kept, not re-earned.
    ///
    /// Plain C# so the timing rules can be tested without a scene.
    /// </summary>
    public sealed class SprintRamp
    {
        float _runTime;
        float _gapTime;

        /// <summary>Seconds of qualifying movement banked so far.</summary>
        public float RunTime => _runTime;

        /// <summary>True once the delay has elapsed — the character is sprinting,
        /// even while <see cref="Blend"/> is still ramping up.</summary>
        public bool IsSprinting(float delay) => _runTime >= delay;

        /// <summary>0 at run speed, 1 at full sprint.</summary>
        public float Blend(float delay, float rampTime)
        {
            if (_runTime < delay) return 0f;
            // With a tolerance, so ForceFull lands on exactly 1 rather than a float
            // hair under it. A plain >= is not enough: Mono may hold delay+rampTime
            // at higher precision than the stored float it is compared with.
            if (rampTime <= 0f || _runTime - delay >= rampTime - 1e-4f) return 1f;
            return Mathf.Clamp01((_runTime - delay) / rampTime);
        }

        /// <summary>Advances the ramp. <paramref name="qualifying"/> is "running
        /// hard enough to build toward a sprint" this frame (stick pushed, not
        /// crouched, not held by a gravity well).</summary>
        public void Tick(bool qualifying, float dt, float grace)
        {
            if (qualifying)
            {
                _runTime += dt;
                _gapTime = 0f;
                return;
            }
            _gapTime += dt;
            if (_gapTime > grace) Reset();
        }

        /// <summary>Jumps straight to full sprint — the sprint button's shortcut,
        /// and the state a move that promises to keep a sprint restores.</summary>
        public void ForceFull(float delay, float rampTime)
        {
            _runTime = Mathf.Max(_runTime, delay + Mathf.Max(rampTime, 0f));
            _gapTime = 0f;
        }

        /// <summary>Forgets any build-up.</summary>
        public void Reset()
        {
            _runTime = 0f;
            _gapTime = 0f;
        }
    }
}

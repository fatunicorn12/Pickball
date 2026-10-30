using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// How long a finished swing keeps its chain alive so a late press still
    /// continues it (R1 playtest: clicks 0.9 s apart restarted the chain every
    /// time). Pure and dt-driven so it is frame-rate independent and testable.
    /// </summary>
    public sealed class ChainGrace
    {
        float _left;

        /// <summary>True while a late press would still continue the chain.</summary>
        public bool IsOpen => _left > 0f;

        public void Start(float seconds) => _left = Mathf.Max(0f, seconds);

        public void Cancel() => _left = 0f;

        /// <summary>Advance by <paramref name="dt"/>. True on the single call in
        /// which the grace runs out.</summary>
        public bool Tick(float dt)
        {
            if (_left <= 0f) return false;
            _left -= dt;
            if (_left > 0f) return false;
            _left = 0f;
            return true;
        }
    }

    /// <summary>Gap-closing lunge toward the locked target: the swing's lunge
    /// direction is turned toward the target, by a capped angle.</summary>
    public static class HomingLunge
    {
        /// <summary>
        /// Direction for a lunge. Flat. Returns the flattened
        /// <paramref name="forward"/> when there is no target in
        /// <paramref name="range"/> or homing is off.
        /// </summary>
        /// <param name="toTarget">Vector from player to target (Y ignored).</param>
        /// <param name="turnRate">Max degrees per second the direction may bend.</param>
        /// <param name="seconds">Time the turn rate is applied for (the swing's length).</param>
        public static Vector3 Direction(Vector3 forward, Vector3 toTarget, float range,
                                        float turnRate, float seconds)
        {
            forward.y = 0f;
            toTarget.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return Vector3.zero;
            forward.Normalize();
            if (range <= 0f || turnRate <= 0f) return forward;
            float dist = toTarget.magnitude;
            if (dist < 1e-3f || dist > range) return forward;
            float maxRadians = turnRate * Mathf.Max(0f, seconds) * Mathf.Deg2Rad;
            return Vector3.RotateTowards(forward, toTarget / dist, maxRadians, 0f).normalized;
        }

        /// <summary>Lunge speed after not shoving the player into the target:
        /// zero once the gap is under <paramref name="minGap"/>, ramping to full
        /// at twice that.</summary>
        public static float Speed(float baseSpeed, float gap, float minGap)
        {
            if (minGap <= 0f) return baseSpeed;
            return baseSpeed * Mathf.Clamp01((gap - minGap) / minGap);
        }
    }

    /// <summary>
    /// Air-combo hang time (KH): while an air swing plays the player stays up,
    /// and when the chain ends he falls. MeleeCombat asks this every frame and
    /// refreshes movement's gravity suspension with the answer, so the hang
    /// lasts exactly as long as the chain plus a short release, and stops the
    /// frame a dash or tether takes the body.
    ///
    /// Bounded by the air chain itself: one airtime buys one air combo, so the
    /// hang grows only as the chain does (Air Combo Plus, air finishers).
    /// </summary>
    public static class AirHang
    {
        /// <summary>
        /// Seconds to hold gravity off from now, or 0 for no hang. Refreshed per
        /// frame, so a hold always covers at least the next couple of frames:
        /// the motor may tick its countdown before this frame's refresh lands.
        /// </summary>
        /// <param name="swingOnAirTrack">A swing is playing (hitstop included) and it
        /// was started on the air track.</param>
        /// <param name="stepHangs">The playing step's <c>HangInAir</c>. Off for a
        /// step that wants to fall, such as a plunge.</param>
        /// <param name="grounded">Raw grounded flag: nothing to hold up.</param>
        /// <param name="busy">An exclusive move (dash, tether) owns the body.</param>
        /// <param name="releaseSeconds">How long the hang outlasts the chain.</param>
        public static float HoldSeconds(bool swingOnAirTrack, bool stepHangs, bool grounded,
                                        bool busy, float releaseSeconds, float dt)
        {
            if (!swingOnAirTrack || !stepHangs || grounded || busy) return 0f;
            return Mathf.Max(releaseSeconds, 2f * dt);
        }
    }

    /// <summary>Countdown for the white hit flash on a struck target.</summary>
    public struct FlashTimer
    {
        float _left;
        float _total;

        public bool IsActive => _left > 0f;

        /// <summary>0 (just ended) to 1 (just struck).</summary>
        public float Strength => _total > 0f ? Mathf.Clamp01(_left / _total) : 0f;

        public void Trigger(float seconds)
        {
            _total = Mathf.Max(0f, seconds);
            _left = _total;
        }

        /// <summary>Advance; true while still flashing afterwards.</summary>
        public bool Tick(float dt)
        {
            if (_left <= 0f) return false;
            _left -= dt;
            if (_left <= 0f) { _left = 0f; return false; }
            return true;
        }
    }
}

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
    /// Air-combo hang time, owned by the swing (Kingdom Hearts II and Castle
    /// Crashers both do it this way): every air swing holds the player up
    /// through its own hover window, a fraction of the swing set on the step,
    /// <b>whether or not it connects</b>. KH2 puts a "no gravity" frame range on
    /// each air attack animation; Castle Crashers' air strings float you with
    /// nothing to hit. The previous rule (only landed hits bought hang time)
    /// dropped the player mid-combo whenever a swing whiffed and made the air
    /// game feel random.
    ///
    /// <b>Bounded by the air chain</b>, as in KH2: one airtime buys one air
    /// combo (<see cref="ComboProgress"/>, refilled only on landing), so the
    /// longest float is the learned air chain's length, and the chain's end,
    /// a landing, or a dash or tether drops him at once.
    ///
    /// <see cref="JustOpened"/> marks the frame a hover window opens, when
    /// MeleeCombat catches a fall (vertical speed to at least zero, KH2's "no
    /// gravity" and Castle Crashers' light-attack rule). Rising speed is left
    /// to ease off, so a jump's or a lift's height carries into the hang.
    /// MeleeCombat ticks this every frame and refreshes movement's gravity
    /// suspension with the answer.
    /// </summary>
    public sealed class AirHang
    {
        /// <summary>True while the playing air swing is holding him up.</summary>
        public bool IsHolding { get; private set; }

        /// <summary>True on the single tick on which a hold began: a hover window
        /// opened, from not holding. Not raised when one hovering swing chains
        /// straight into another.</summary>
        public bool JustOpened { get; private set; }

        /// <summary>True when <paramref name="progress"/> (0-1 through the swing)
        /// is inside the hover window [<paramref name="start"/>, <paramref name="end"/>].
        /// An empty window (end at or before start) never hovers.</summary>
        public static bool InWindow(float progress, float start, float end)
            => end > start && progress >= start && progress <= end;

        public void Cancel()
        {
            IsHolding = false;
            JustOpened = false;
        }

        /// <summary>
        /// Advance by <paramref name="dt"/>. Returns the seconds of gravity
        /// suspension to request this frame, or 0 for none. A hold covers two
        /// frames, because the motor may tick its countdown before this frame's
        /// refresh lands; the hang therefore outlives its window by at most a
        /// frame or two.
        /// </summary>
        /// <param name="hoveringSwing">An air-track swing whose step hangs in the air
        /// is playing (hitstop included). False drops him: the chain is over.</param>
        /// <param name="progress">0-1 through that swing.</param>
        /// <param name="start">Hover window start, fraction of the swing.</param>
        /// <param name="end">Hover window end, fraction of the swing.</param>
        /// <param name="grounded">Raw grounded flag: nothing to hold up.</param>
        /// <param name="busy">An exclusive move (dash, tether) owns the body.</param>
        public float Tick(bool hoveringSwing, float progress, float start, float end,
                          bool grounded, bool busy, float dt)
        {
            bool hold = hoveringSwing && !grounded && !busy && InWindow(progress, start, end);
            JustOpened = hold && !IsHolding;
            IsHolding = hold;
            return hold ? 2f * dt : 0f;
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

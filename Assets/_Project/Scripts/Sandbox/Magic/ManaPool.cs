using System;
using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The mana bar's arithmetic: Terraria 1.4's <c>Player.UpdateManaRegen</c>,
    /// reproduced exactly, in plain C# so it is testable without a scene.
    /// <see cref="Mana"/> is the component that owns one and ticks it.
    ///
    /// <b>Why fixed ticks.</b> Terraria's regen is integer arithmetic run once
    /// per 1/60 s frame, and its feel lives in the truncations: a fraction of a
    /// point is banked in a counter of 1/120ths, rates are truncated to whole
    /// counts, and the bar gains whole points. Scaling that by a variable
    /// <c>dt</c> would blur exactly those steps and make the refill time drift
    /// with frame rate. Instead <see cref="Tick"/> banks real time in an
    /// accumulator and runs one Terraria frame per 1/60 s of it, so 30, 60 and
    /// 144 fps give the same bar at the same moment, point for point. The
    /// Sandbox otherwise simulates in <c>Update</c>; this is the one place that
    /// sub-steps, because here the integer steps ARE the design.
    ///
    /// <b>One tick</b>, in Terraria's order:
    /// <list type="number">
    /// <item>While delayed, the delay drops by 1 tick (by
    /// <see cref="ManaConfig.StillDelaySpeedup"/> standing still).</item>
    /// <item>Once it reaches 0 — on that same tick — regen is
    /// <c>(int)(max × baseShare) + flat</c>, plus <c>(int)(max × stillShare)</c>
    /// standing still, then <c>(int)(regen × (fill × (1 − atEmpty) + atEmpty) ×
    /// multiplier)</c>. The fill term is what makes the bar start slow and
    /// accelerate.</item>
    /// <item>The regen is banked in a counter; every 120 in it buys one point.
    /// Reaching max zeroes the counter (Terraria's sparkle-and-chime moment),
    /// and at max the counter keeps cycling back to 0, so a spend from full
    /// carries at most a fraction of a point of head start.</item>
    /// </list>
    /// A spend restarts the delay from <see cref="ManaConfig.RegenDelayFor"/>
    /// at the fill the spend left, rounded UP to whole ticks so it is never
    /// shorter than the tooltip says (0.525 s → 32 ticks, 3 s → exactly 180).
    /// A spend that cannot be afforded changes nothing, including a running delay.
    ///
    /// Truncations take a 1e-4 slack before rounding down, so a value that is a
    /// whole number on paper (0.7 × 10, 20 × 1.15) is not knocked one lower by
    /// float representation error, the way Terraria's integer division would
    /// not be. The default numbers are nowhere near a boundary, so the slack
    /// changes none of them.
    ///
    /// <b>Hot reload.</b> <c>[Serializable]</c>, with every field a private
    /// <c>[SerializeField]</c> and none <c>readonly</c>, so Unity's domain-reload
    /// backup (which serializes a MonoBehaviour's private fields) carries the
    /// bar, the delay and the counter across a script reload in Play mode. The
    /// serializer can also hand back a default-constructed, unbound pool;
    /// <see cref="Mana"/> checks <see cref="IsBound"/> and rebuilds a full bar
    /// rather than ticking an empty shell.
    /// </summary>
    [Serializable]
    public sealed class ManaPool
    {
        /// <summary>Terraria frames per second; one regen step per 1/60 s.</summary>
        public const int TicksPerSecond = 60;

        /// <summary>Counter units per point of mana: regen is banked in 1/120ths.</summary>
        public const int CountPerPoint = 120;

        const double TickSeconds = 1.0 / TicksPerSecond;

        // Real time within this of a tick boundary counts as reaching it. Frame
        // times are floats (1/144 s is not exact), and without the slack a sum
        // that lands a hair short of a boundary would run that tick a frame late,
        // breaking "same elapsed time, same bar" at the boundary. 1e-5 s is
        // 0.06% of a tick; the remainder goes negative and the error never builds.
        const double BoundarySlack = 1e-5;

        // Rounding slack for seconds → ticks, so 3.0000002 s × 60 is 180 ticks,
        // not 181.
        const double TickRoundingSlack = 1e-3;

        // See the class summary: whole numbers on paper stay whole.
        const double TruncationSlack = 1e-4;

        // One call never simulates more than this, s. Time.deltaTime is capped
        // far below it (Time.maximumDeltaTime, 1/3 s by default); it only stops
        // a garbage dt (a huge or infinite value) hanging the frame.
        const float MaxCatchUpSeconds = 60f;

        [SerializeField] private ManaConfig config;
        [SerializeField] private int current;
        [SerializeField] private int count;             // banked regen, 1/120ths of a point
        [SerializeField] private float delayTicks;      // remaining, in moving-speed ticks
        [SerializeField] private float delayTotalTicks; // what the running delay started at
        [SerializeField] private double tickAccumulator; // real time not yet run as ticks, s

        // For Unity's serializer only (see "Hot reload" above): an instance made
        // this way is unbound until a Mana replaces it.
        ManaPool() { }

        /// <summary>A full bar governed by <paramref name="config"/>, which is
        /// read live every tick so Inspector tuning applies in Play mode.</summary>
        public ManaPool(ManaConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            current = config.MaxMana;
        }

        /// <summary>Mana now, whole points.</summary>
        public int Current => current;

        /// <summary>Mana in a full bar, points (from the config, ≥ 1).</summary>
        public int Max => config != null ? config.MaxMana : 1;

        /// <summary>Current ÷ Max, 0–1, for a bar.</summary>
        public float Normalised => Mathf.Clamp01((float)current / Max);

        /// <summary>Seconds of regen delay left, counted at the moving rate (it
        /// runs out faster than real time while standing still). 0 when none.</summary>
        public float DelayRemaining => delayTicks > 0f ? delayTicks / TicksPerSecond : 0f;

        /// <summary>Seconds the running delay started at, so a HUD can show its
        /// progress as <c>DelayRemaining / DelayTotal</c>. 0 when none is running.</summary>
        public float DelayTotal => delayTicks > 0f ? delayTotalTicks / TicksPerSecond : 0f;

        /// <summary>True while a spend's delay is holding regen back.</summary>
        public bool IsDelayed => delayTicks > 0f;

        /// <summary>True when the bar holds Max.</summary>
        public bool IsFull => current >= Max;

        /// <summary>True when a config is attached. False only for an instance
        /// Unity's serializer default-constructed, which its owner must replace.</summary>
        internal bool IsBound => config != null;

        /// <summary>The config being read, for the owner's rebinding check.</summary>
        internal ManaConfig Config => config;

        /// <summary>Whether <paramref name="cost"/> could be paid now. A cost of
        /// zero or less is always affordable (and free).</summary>
        public bool CanAfford(int cost) => cost <= 0 || current >= cost;

        /// <summary>
        /// Pay <paramref name="cost"/> and restart the regen delay, sized by the
        /// fill the spend leaves. Returns false — and changes nothing, not even
        /// a running delay — if the bar holds less than the cost. A cost of zero
        /// or less succeeds without touching anything: a free action is not a
        /// spend and must not pause regen.
        /// </summary>
        public bool TrySpend(int cost)
        {
            if (cost <= 0) return true;
            if (!IsBound) return false;

            int max = config.MaxMana;
            if (current > max) current = max;
            if (current < cost) return false;

            current -= cost;
            float ticks = SecondsToTicks(config.RegenDelayFor((float)current / max));
            delayTicks = ticks;
            delayTotalTicks = ticks;
            return true;
        }

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds of real time, running one
        /// Terraria tick per 1/60 s banked (so a frame can run zero, one or
        /// several). <paramref name="standingStill"/> applies to every tick in
        /// the call. Returns true if the bar reached Max from below during the
        /// call — the "mana full" cue, raised once per refill. Non-positive or
        /// NaN <paramref name="dt"/> does nothing.
        /// </summary>
        public bool Tick(float dt, bool standingStill)
        {
            if (!(dt > 0f) || !IsBound) return false;

            tickAccumulator += Mathf.Min(dt, MaxCatchUpSeconds);
            bool reachedFull = false;
            while (tickAccumulator >= TickSeconds - BoundarySlack)
            {
                tickAccumulator -= TickSeconds;
                if (Step(standingStill)) reachedFull = true;
            }
            return reachedFull;
        }

        /// <summary>Fill the bar instantly (respawn, checkpoint, debug) and clear
        /// any delay. Deliberately not a "reached full" moment: like Terraria's
        /// potions it does not chime — <see cref="Tick"/> reports only refills
        /// that regen earned.</summary>
        public void Refill()
        {
            if (!IsBound) return;
            current = config.MaxMana;
            count = 0;
            delayTicks = 0f;
            delayTotalTicks = 0f;
        }

        /// <summary>
        /// The regen rate right now, points per second, at the current fill —
        /// what the next ticks will bank, averaged over the 120-count steps.
        /// 0 while delayed and when full (no points are being gained). The rate
        /// rises with every point gained, so over a stretch of regen the real
        /// gain runs a little ahead of the rate read at its start.
        /// </summary>
        public float RegenPerSecond(bool standingStill)
        {
            if (!IsBound || IsDelayed || IsFull) return 0f;
            return RegenPerTick(standingStill, config.MaxMana) * (float)TicksPerSecond / CountPerPoint;
        }

        /// <summary>Point at <paramref name="newConfig"/>, keeping the bar
        /// (clamped to the new max) and any running delay.</summary>
        internal void Rebind(ManaConfig newConfig)
        {
            if (newConfig == null) return;
            config = newConfig;
            if (current > config.MaxMana) current = config.MaxMana;
        }

        /// <summary>One Terraria frame of <c>UpdateManaRegen</c>. True if it took
        /// the bar to Max from below.</summary>
        bool Step(bool standingStill)
        {
            int max = config.MaxMana;
            if (current > max) current = max;   // max lowered live in the Inspector

            if (delayTicks > 0f)
            {
                delayTicks -= 1f;
                if (standingStill) delayTicks -= config.StillDelaySpeedup - 1f;
            }

            int regen = 0;
            if (delayTicks <= 0f)
            {
                delayTicks = 0f;
                delayTotalTicks = 0f;
                regen = RegenPerTick(standingStill, max);
            }

            count += regen;
            bool reachedFull = false;
            while (count >= CountPerPoint)
            {
                count -= CountPerPoint;
                bool gained = false;
                if (current < max)
                {
                    current++;
                    gained = true;
                }
                if (current >= max)
                {
                    if (gained) reachedFull = true;
                    count = 0;
                    current = max;
                }
            }
            return reachedFull;
        }

        /// <summary>Counter units banked per tick at the current fill, in
        /// Terraria's truncation order.</summary>
        int RegenPerTick(bool standingStill, int max)
        {
            int regen = Truncate((double)max * config.BaseRegenShareOfMax) + config.FlatRegen;
            if (standingStill) regen += Truncate((double)max * config.StillRegenShareOfMax);

            double atEmpty = config.RegenAtEmptyFraction;
            double fill = (double)current / max;
            regen = Truncate(regen * (fill * (1.0 - atEmpty) + atEmpty) * config.RegenMultiplier);
            return regen > 0 ? regen : 0;
        }

        static int Truncate(double value) => (int)(value + TruncationSlack);

        /// <summary>Seconds → whole ticks, rounded up (never shorter than asked).</summary>
        static float SecondsToTicks(float seconds)
        {
            if (!(seconds > 0f)) return 0f;
            return (float)Math.Max(0.0, Math.Ceiling(seconds * (double)TicksPerSecond - TickRoundingSlack));
        }
    }
}

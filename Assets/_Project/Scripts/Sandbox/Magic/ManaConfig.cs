using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Every tunable of the player's mana, in one asset.
    ///
    /// <b>Where the defaults come from.</b> Terraria 1.4's
    /// <c>Player.UpdateManaRegen</c>, number for number, with one change the
    /// user asked for: the regen delay after emptying the bar is 3 s instead of
    /// Terraria's 3.325 s. Terraria's delay is <c>0.7 × (45 + 240 × missing)</c>
    /// frames, i.e. 0.525 s after a sip from a full bar up to 3.325 s from
    /// empty; we keep its 0.525 s short end and its linear shape and pin the
    /// long end at 3 s. The rate side is untouched: each 1/60 s tick adds
    /// <c>(max/3 + 1)</c> (plus <c>max/3</c> standing still), scaled by
    /// <c>fill × 0.8 + 0.2</c> and by 1.15, in 1/120ths of a point. That
    /// <c>fill</c> term is the feel the user described — "fills up
    /// exponentially fast, but starts slow initially": at empty the bar refills
    /// at a fifth of its near-full rate, and every point it gains speeds up the
    /// next.
    ///
    /// <b>What the defaults feel like</b> (max 100, computed tick by tick by
    /// <see cref="ManaPool"/> and pinned by <c>ManaPoolTests</c>): an empty bar
    /// waits 3 s, then refills in ≈10.7 s on the move (full 13.7 s after the
    /// spend); standing still the wait is 1.5 s and the refill ≈5.4 s (full
    /// 6.9 s after the spend). A 15-mana spell from a full bar delays regen
    /// 0.9 s.
    ///
    /// Read live by <see cref="ManaPool"/> every tick, so a change in the
    /// Inspector is felt immediately in Play mode.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Magic/Mana Config", fileName = "ManaConfig")]
    public class ManaConfig : ScriptableObject
    {
        // --------------------------------------------------------------- pool
        [Header("Pool")]
        [Tooltip("Mana in a full bar, points. Regen scales with it (it is a share of " +
                 "max), so a bigger bar refills in about the same time, not longer. " +
                 "Spells cost whole points — the default spell costs 15.")]
        [SerializeField, Min(1)] private int maxMana = 100;

        // -------------------------------------------------------------- delay
        [Header("Regen delay (after a spend)")]
        [Tooltip("Seconds before regen starts after a spend that leaves the bar EMPTY. " +
                 "The delay scales linearly with how empty the spend left the bar, from " +
                 "the at-full value up to this. Terraria's is 3.325 s; 3 s was asked for. " +
                 "Higher punishes emptying the bar; rounded up to whole 1/60 s ticks.")]
        [SerializeField, Min(0f)] private float regenDelayAtEmpty = 3f;

        [Tooltip("Seconds before regen starts after a spend that leaves the bar (almost) " +
                 "FULL — Terraria's 0.525 s. The floor of the delay: even a cheap spell " +
                 "pauses regen this long. Rounded up to whole 1/60 s ticks (0.525 s → " +
                 "0.533 s).")]
        [SerializeField, Min(0f)] private float regenDelayAtFull = 0.525f;

        [Tooltip("How many times faster the delay counts down while standing still, ×. " +
                 "Terraria: 2 (an empty-bar delay of 3 s ends after 1.5 s standing still). " +
                 "1 = standing still does not shorten the delay.")]
        [SerializeField, Min(1f)] private float stillDelaySpeedup = 2f;

        // --------------------------------------------------------------- rate
        [Header("Regen rate")]
        [Tooltip("Regen on the move, as a share of max mana per tick, before the fill " +
                 "scaling: each 1/60 s tick adds (max × this, truncated) + flat regen " +
                 "1/120ths of a point. Terraria 1.4: 1/3. With the defaults: ≈19.5 points/s " +
                 "near full, 3.5 points/s at empty (an empty bar refills in ≈10.7 s on " +
                 "the move).")]
        [SerializeField, Min(0f)] private float baseRegenShareOfMax = 1f / 3f;

        [Tooltip("Extra regen while standing still, as a share of max mana per tick " +
                 "(truncated), added before the fill scaling. Terraria 1.4: 1/3, which " +
                 "roughly doubles the rate (≈38.5 points/s near full; an empty bar refills " +
                 "in ≈5.4 s standing still). 0 = standing still only shortens the delay.")]
        [SerializeField, Min(0f)] private float stillRegenShareOfMax = 1f / 3f;

        [Tooltip("Flat regen added every tick, in 1/120ths of a point per 1/60 s tick, " +
                 "before the fill scaling. Terraria: 1 (its manaRegenBonus items add to " +
                 "this). Matters only for tiny bars.")]
        [SerializeField, Min(0)] private int flatRegen = 1;

        [Tooltip("Regen rate at an empty bar as a fraction of the near-full rate, 0–1. " +
                 "The rate rises linearly with the fill from this up to 1: the 'starts " +
                 "slow, then accelerates' curve. Terraria: 0.2. 1 = a constant rate; " +
                 "lower = a slower start and a sharper finish.")]
        [SerializeField, Range(0f, 1f)] private float regenAtEmptyFraction = 0.2f;

        [Tooltip("Final multiplier on the regen rate, ×. Terraria: 1.15. Scales every " +
                 "refill time by its inverse (2 ≈ refills in half the time).")]
        [SerializeField, Min(0f)] private float regenMultiplier = 1.15f;

        // ------------------------------------------------------ standing still
        [Header("Standing still")]
        [Tooltip("At or below this speed (m/s, planar and vertical together) the player " +
                 "counts as standing still: faster delay and the extra regen above. " +
                 "Terraria demands exactly zero velocity; a little slack absorbs the " +
                 "controller's settling jitter. Falling and jumping count as moving.")]
        [SerializeField, Min(0f)] private float standingStillSpeed = 0.15f;

        /// <summary>Mana in a full bar, points (never below 1, so a fill fraction
        /// can always be computed).</summary>
        public int MaxMana => Mathf.Max(1, maxMana);

        /// <summary>Delay after a spend that empties the bar, s.</summary>
        public float RegenDelayAtEmpty => Mathf.Max(0f, regenDelayAtEmpty);

        /// <summary>Delay after a spend that leaves the bar full, s — the shortest
        /// delay any spend can cause.</summary>
        public float RegenDelayAtFull => Mathf.Max(0f, regenDelayAtFull);

        /// <summary>Delay countdown speed while standing still, × (≥ 1).</summary>
        public float StillDelaySpeedup => Mathf.Max(1f, stillDelaySpeedup);

        /// <summary>Moving regen per tick as a share of max, before fill scaling.</summary>
        public float BaseRegenShareOfMax => Mathf.Max(0f, baseRegenShareOfMax);

        /// <summary>Extra standing-still regen per tick as a share of max, before
        /// fill scaling.</summary>
        public float StillRegenShareOfMax => Mathf.Max(0f, stillRegenShareOfMax);

        /// <summary>Flat regen per tick, 1/120ths of a point.</summary>
        public int FlatRegen => Mathf.Max(0, flatRegen);

        /// <summary>Regen rate at an empty bar relative to a full one, 0–1.</summary>
        public float RegenAtEmptyFraction => Mathf.Clamp01(regenAtEmptyFraction);

        /// <summary>Final regen multiplier, ×.</summary>
        public float RegenMultiplier => Mathf.Max(0f, regenMultiplier);

        /// <summary>Speed at or below which the owner counts as standing still, m/s.</summary>
        public float StandingStillSpeed => Mathf.Max(0f, standingStillSpeed);

        /// <summary>
        /// The regen delay, in seconds, after a spend that left the bar at
        /// <paramref name="fillFraction"/> (0 = empty, 1 = full).
        ///
        /// Takes the fill AFTER the spend on purpose: the spell that empties the
        /// bar is the one that earns the long wait, whatever the bar held before
        /// it. Linear between the two ends, like Terraria's
        /// <c>45 + 240 × missing</c>. <see cref="ManaPool"/> rounds the result
        /// up to whole 1/60 s ticks.
        /// </summary>
        public float RegenDelayFor(float fillFraction)
            => Mathf.Lerp(RegenDelayAtFull, RegenDelayAtEmpty, 1f - Mathf.Clamp01(fillFraction));
    }
}

using System;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Health with a Bloodborne-style regain window.
    ///
    /// Damage does not all land at once. Most of it is provisional — it leaves
    /// the bar but sits in a "recoverable" pool that hitting the boss converts
    /// back into real health. Stop fighting and the pool drains away for good.
    /// The intent is that getting hit is a prompt to press, not a reason to
    /// retreat: the fight is meant to be survivable on fumes.
    ///
    /// The bar is therefore three regions, not two:
    ///
    ///   [ ==== current ==== ][ ~~ recoverable ~~ ][ ---- gone ---- ]
    ///                        ^ still winnable      ^ locked in
    ///
    /// <see cref="Damage"/> takes a <c>recoverable</c> flag so an attack can opt
    /// out of the whole system and take health permanently on contact — the
    /// Fluxion Beam does exactly that, which is what makes standing still the
    /// correct answer to it rather than a stylistic preference.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Whether hurting and defeating this pays the player combat XP (Lemmas). " +
                 "Off for friendly NPCs, so cutting one down is never a shortcut to a level.")]
        [SerializeField] private bool grantsXp = true;

        [Header("Regain")]
        [Tooltip("Fraction of incoming recoverable damage that goes to the grey " +
                 "pool instead of being lost outright. 1 = every point is " +
                 "winnable back, 0 = the regain system is off.")]
        [Range(0f, 1f)]
        [SerializeField] private float regainShare = 0.7f;

        [Tooltip("Seconds after the last hit before the grey pool starts draining.")]
        [SerializeField] private float regainGrace = 2.5f;

        [Tooltip("Points per second the grey pool drains once the grace expires.")]
        [SerializeField] private float regainDecay = 14f;

        [Tooltip("Health returned per point of damage the owner deals. Above 1 a " +
                 "single good combo can cash in a whole bad exchange.")]
        [SerializeField] private float regainPerDamageDealt = 0.9f;

        [Header("Hit grace")]
        [Tooltip("Seconds of invulnerability after a DISCRETE hit lands (i-frames). " +
                 "0 = off, which enemies and dummies keep. Continuous damage (per-frame " +
                 "ticks, see hitGraceMinDamage) is never blocked and never starts a window.")]
        [Min(0f)]
        [SerializeField] private float hitGraceSeconds = 0f;

        [Tooltip("Hits smaller than this count as continuous ticks (beam, well core: " +
                 "dps x dt) and ignore the grace window in both directions.")]
        [Min(0f)]
        [SerializeField] private float hitGraceMinDamage = 2f;

        /// <summary>Current health, the solid part of the bar.</summary>
        public float Current { get; private set; }

        /// <summary>The grey pool — damage taken but not yet locked in.</summary>
        public float Recoverable { get; private set; }

        public float Max => maxHealth;
        public bool IsDead => Current <= 0f;

        /// <summary>Whether damage to this pays combat XP. False for friendly NPCs.</summary>
        public bool GrantsXp => grantsXp;

        /// <summary>Normalised for UI. Recoverable is expressed as the far edge of
        /// the grey region so a bar can draw it as one span from Current.</summary>
        public float CurrentNormalised => maxHealth > 0f ? Current / maxHealth : 0f;
        public float RecoverableNormalised
            => maxHealth > 0f ? Mathf.Clamp01((Current + Recoverable) / maxHealth) : 0f;

        /// <summary>Fired on every change so UI never has to poll.</summary>
        public event Action<Health> Changed;

        /// <summary>Fired once, when Current first reaches zero.</summary>
        public event Action<Health> Died;

        /// <summary>Fired on each damaging hit: amount actually lost, world point.</summary>
        public event Action<float, Vector3> Hurt;

        /// <summary>
        /// Optional hook letting whoever owns this body reinterpret incoming
        /// damage before it lands; returns the amount to actually apply.
        ///
        /// It exists so the weapon never has to know who it hit. MeleeCombat
        /// finds an IDamageable and deals its number; rules like "Newton's weak
        /// point is close range" are the boss's business, so the boss installs a
        /// filter here rather than the sword growing a special case.
        /// </summary>
        public Func<float, Vector3, float> IncomingDamageFilter;

        /// <summary>Same as <see cref="IncomingDamageFilter"/>, but also told the
        /// hit's <see cref="DamageKind"/> — Newton uses it to exempt spells from his
        /// range rule. Applied after <see cref="IncomingDamageFilter"/> when both are set.</summary>
        public Func<float, Vector3, DamageKind, float> IncomingDamageFilterByKind;

        /// <summary>What <see cref="IncomingDamageFilter"/> did to the most recent
        /// hit, as applied ÷ raw: above 1 the hit was amplified (Newton's close-range
        /// weak point), below 1 it was resisted. Valid inside a <see cref="Hurt"/>
        /// handler, so feedback can say WHY a number is big without knowing who
        /// installed the filter.</summary>
        public float LastHitScale { get; private set; } = 1f;

        /// <summary>Whether the most recent hit fed the grey pool. False for the
        /// permanent kind — the Fluxion Beam — which UI shows differently.</summary>
        public bool LastHitRecoverable { get; private set; } = true;

        /// <summary>True when the most recent landed hit was a discrete blow rather
        /// than a continuous tick; feedback (hurt flash) keys off it.</summary>
        public bool LastHitDiscrete { get; private set; }

        /// <summary>True while post-hit grace is protecting this body.</summary>
        public bool InGrace => hitGraceSeconds > 0f && _grace.IsOpen(Now);

        public float HitGraceSeconds { get => hitGraceSeconds; set => hitGraceSeconds = Mathf.Max(0f, value); }

        /// <summary>Clock override for tests; null reads <see cref="Time.time"/>.</summary>
        public Func<float> TimeSource;

        float Now => TimeSource != null ? TimeSource() : Time.time;

        HitGrace _grace;
        float _lastHitTime = -999f;
        bool _deathAnnounced;

        void Awake() => Current = maxHealth;

        void Update()
        {
            if (Recoverable <= 0f || IsDead) return;
            if (Time.time - _lastHitTime < regainGrace) return;

            Recoverable = Mathf.Max(0f, Recoverable - regainDecay * Time.deltaTime);
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Apply damage.
        /// </summary>
        /// <param name="amount">Points of damage.</param>
        /// <param name="at">World point, for VFX and floating numbers.</param>
        /// <param name="recoverable">False makes the loss immediate and permanent —
        /// nothing enters the grey pool and nothing can win it back.</param>
        /// <param name="continuous">True marks a per-frame tick that must never be
        /// refused by, or open, hit grace. Tiny hits are treated so automatically.</param>
        /// <param name="kind">What dealt it; passed to <see cref="IncomingDamageFilterByKind"/>.</param>
        public void Damage(float amount, Vector3 at, bool recoverable = true, bool continuous = false,
                           DamageKind kind = DamageKind.Physical)
        {
            if (amount <= 0f || IsDead) return;

            bool discrete = hitGraceSeconds > 0f
                            && HitGrace.IsDiscrete(amount, hitGraceMinDamage, continuous);
            float now = Now;
            if (_grace.Blocks(now, discrete)) return;

            float raw = amount;
            if (IncomingDamageFilter != null) amount = IncomingDamageFilter(amount, at);
            if (IncomingDamageFilterByKind != null) amount = IncomingDamageFilterByKind(amount, at, kind);
            if (amount <= 0f) return;

            float applied = Mathf.Min(amount, Current);
            Current -= applied;

            if (recoverable)
            {
                Recoverable = Mathf.Min(Recoverable + applied * regainShare,
                                        maxHealth - Current);
            }
            // Permanent damage banks nothing, and needs no clamp. Current has only
            // just DECREASED, so the headroom (maxHealth - Current) only grew and
            // the existing pool cannot have been pushed over it — the invariant
            // Current + Recoverable <= Max still holds untouched.
            //
            // A re-clamp used to sit here claiming it stopped the pool refilling
            // you past where the beam cut you down to. It never did anything: the
            // bound it tested against can only loosen inside this method. Removed
            // rather than kept as reassurance, because a line that looks like a
            // safety check and is provably inert is worse than no line at all.

            _lastHitTime = Time.time;
            LastHitDiscrete = HitGrace.IsDiscrete(amount, hitGraceMinDamage, continuous);
            if (discrete) _grace.Open(now, hitGraceSeconds);
            LastHitScale = amount / raw;
            LastHitRecoverable = recoverable;
            Hurt?.Invoke(applied, at);
            Changed?.Invoke(this);

            if (Current <= 0f && !_deathAnnounced)
            {
                _deathAnnounced = true;
                Died?.Invoke(this);
            }
        }

        /// <summary>Convert grey back into health. Called with the damage this
        /// owner just dealt — landing hits is how the pool is cashed in.</summary>
        public void CreditDamageDealt(float damageDealt)
        {
            if (damageDealt <= 0f || Recoverable <= 0f || IsDead) return;

            float gained = Mathf.Min(damageDealt * regainPerDamageDealt, Recoverable);
            Current += gained;
            Recoverable -= gained;
            Changed?.Invoke(this);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead) return;
            Current = Mathf.Min(Current + amount, maxHealth);
            Recoverable = Mathf.Min(Recoverable, maxHealth - Current);
            Changed?.Invoke(this);
        }

        public void ResetToFull()
        {
            Current = maxHealth;
            Recoverable = 0f;
            _deathAnnounced = false;
            _grace.Clear();
            Changed?.Invoke(this);
        }

        /// <summary>Weapon entry point. The sword's knockback and launch are for
        /// the receiver to interpret — a boss ignores being launched, a crate
        /// does not — so they are passed on rather than applied here.</summary>
        public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                            DamageKind kind = DamageKind.Physical)
        {
            Damage(amount, hitPoint, kind: kind);
            Knocked?.Invoke(knockback, launch);
        }

        /// <summary>Raised by <see cref="TakeHit"/> so whoever owns the body can
        /// decide what a shove means for them.</summary>
        public event Action<Vector3, bool> Knocked;
    }
}

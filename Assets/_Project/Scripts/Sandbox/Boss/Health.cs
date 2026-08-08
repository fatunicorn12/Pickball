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

        /// <summary>Current health, the solid part of the bar.</summary>
        public float Current { get; private set; }

        /// <summary>The grey pool — damage taken but not yet locked in.</summary>
        public float Recoverable { get; private set; }

        public float Max => maxHealth;
        public bool IsDead => Current <= 0f;

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
        public void Damage(float amount, Vector3 at, bool recoverable = true)
        {
            if (amount <= 0f || IsDead) return;

            if (IncomingDamageFilter != null) amount = IncomingDamageFilter(amount, at);
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
            Changed?.Invoke(this);
        }

        /// <summary>Weapon entry point. The sword's knockback and launch are for
        /// the receiver to interpret — a boss ignores being launched, a crate
        /// does not — so they are passed on rather than applied here.</summary>
        public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch)
        {
            Damage(amount, hitPoint);
            Knocked?.Invoke(knockback, launch);
        }

        /// <summary>Raised by <see cref="TakeHit"/> so whoever owns the body can
        /// decide what a shove means for them.</summary>
        public event Action<Vector3, bool> Knocked;
    }
}

using System;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The player's mana: owns a <see cref="ManaPool"/>, ticks it every frame,
    /// and announces what changed. Spells spend through <see cref="TrySpend"/>;
    /// the events carry the one-shot moments (a spend, a failed spend, the
    /// bar filling), and the HUD reads the bar itself per frame, the way
    /// HealthBarView reads Health.
    ///
    /// Gameplay only. It raises <see cref="Filled"/> for Terraria's
    /// sparkle-and-chime moment but plays nothing itself — presentation
    /// subscribes, as everywhere else in the Sandbox.
    ///
    /// <b>Standing still</b> (faster delay, extra regen) is read from an
    /// <see cref="ICombatMovement"/>, normally <see cref="ChibiMovementAdapter"/>,
    /// the same narrow view of movement the sword uses, so magic never
    /// references a movement class. Unlike Terraria, hanging from a grapple
    /// (our tether) does not count: only speed does. With no movement source at
    /// all the owner counts as standing still — the same reading the adapter
    /// gives without locomotion (zero velocity), and the honest one for a body
    /// that cannot move (a test rig, a turret).
    ///
    /// <b>Lazy, reload-safe state.</b> The pool is built on first use, not in
    /// <c>Awake</c>, so EditMode tests and editor tooling (whose objects never
    /// get <c>Awake</c>/<c>OnEnable</c>) can use the component directly. It is
    /// a private <c>[Serializable]</c> field without <c>[SerializeField]</c>:
    /// never saved into the scene (a scene must not remember half a bar), but
    /// carried across a script reload in Play mode by Unity's domain-reload
    /// backup, so the bar survives an agent saving a file mid-fight. If the
    /// backup ever hands back an unbound pool, the lazy getter builds a fresh,
    /// full one instead of failing. Event subscriptions are not serialized;
    /// listeners re-subscribe from their own <c>OnEnable</c>, which a reload
    /// does run.
    /// </summary>
    public class Mana : MonoBehaviour
    {
        [Tooltip("Pool size, regen delay and regen curve. Without one the shipped " +
                 "defaults (Terraria's tuning with a 3 s empty delay) are used and a " +
                 "warning is logged on enable.")]
        [SerializeField] private ManaConfig config;

        [Header("Wiring")]
        [Tooltip("Component implementing ICombatMovement — normally ChibiMovementAdapter. " +
                 "Its velocity decides standing still (regen delay counts down twice as " +
                 "fast and regen roughly doubles). Found on this object if left empty; " +
                 "with none at all the owner counts as standing still.")]
        [SerializeField] private MonoBehaviour movementSource;

        // Built lazily (see the class summary). Deliberately not [SerializeField].
        ManaPool _pool;

        // Created only when no config is assigned. DontSave, destroyed with us.
        ManaConfig _fallbackConfig;

        // An interface field is not serializable, so a reload clears it; the
        // flag must clear with it or a reload would leave "resolved, to nothing"
        // and the player would count as standing still forever.
        [NonSerialized] ICombatMovement _movement;
        [NonSerialized] Component _movementComponent;
        [NonSerialized] bool _movementResolved;

        /// <summary>Raised whenever <see cref="Current"/> changes — a spend, a
        /// regen point, a live max change — and on every <see cref="Refill"/>.
        /// Regen can gain several points in one long frame; that is one event
        /// with the new value.</summary>
        public event Action<Mana> Changed;

        /// <summary>Raised after a successful spend, with the cost actually paid.
        /// <see cref="Changed"/> has already been raised.</summary>
        public event Action<int> Spent;

        /// <summary>Raised when a spend could not be afforded, with the cost
        /// asked for. Nothing else changed — the running delay included.</summary>
        public event Action<int> SpendFailed;

        /// <summary>Raised once when regen brings the bar to max from below:
        /// Terraria's sparkle-and-chime moment. Raised after the final
        /// <see cref="Changed"/>. Not raised by <see cref="Refill"/>.</summary>
        public event Action Filled;

        /// <summary>The config in use: the assigned asset, or — when none is
        /// assigned — a default instance made on first use. Never null.</summary>
        public ManaConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_fallbackConfig == null)
                {
                    _fallbackConfig = ScriptableObject.CreateInstance<ManaConfig>();
                    _fallbackConfig.name = "ManaConfig (defaults)";
                    _fallbackConfig.hideFlags = HideFlags.DontSave;
                }
                return _fallbackConfig;
            }
        }

        /// <summary>Mana now, whole points.</summary>
        public int Current => Pool.Current;

        /// <summary>Mana in a full bar, points.</summary>
        public int Max => Pool.Max;

        /// <summary>Current ÷ Max, 0–1.</summary>
        public float Normalised => Pool.Normalised;

        /// <summary>Seconds of regen delay left at the moving rate; 0 when none.
        /// Standing still runs it out up to twice as fast as this reads.</summary>
        public float RegenDelayRemaining => Pool.DelayRemaining;

        /// <summary>Delay progress for a HUD: 1 right after a spend, falling to 0
        /// as regen is about to start; 0 when no delay is running.</summary>
        public float RegenDelayNormalised
        {
            get
            {
                ManaPool pool = Pool;
                float total = pool.DelayTotal;
                return total > 0f ? Mathf.Clamp01(pool.DelayRemaining / total) : 0f;
            }
        }

        /// <summary>True while the bar is actually filling: no delay, not full.</summary>
        public bool IsRegenerating
        {
            get
            {
                ManaPool pool = Pool;
                return !pool.IsDelayed && !pool.IsFull;
            }
        }

        /// <summary>True when the bar holds max.</summary>
        public bool IsFull => Pool.IsFull;

        /// <summary>
        /// True when the movement source's speed (planar and vertical together)
        /// is at or below <see cref="ManaConfig.StandingStillSpeed"/>, so falling
        /// or riding a jump counts as moving. True with no movement source (see
        /// the class summary). This is what <c>Update</c> passes to
        /// <see cref="Tick"/>.
        /// </summary>
        public bool IsStandingStill
        {
            get
            {
                ICombatMovement movement = Movement;
                if (movement == null) return true;
                float limit = Config.StandingStillSpeed;
                return movement.Velocity.sqrMagnitude <= limit * limit;
            }
        }

        /// <summary>Whether <paramref name="cost"/> could be paid now.</summary>
        public bool CanAfford(int cost) => Pool.CanAfford(cost);

        /// <summary>
        /// Pay <paramref name="cost"/> and restart the regen delay. On success
        /// raises <see cref="Changed"/> then <see cref="Spent"/>; on failure
        /// raises only <see cref="SpendFailed"/> and changes nothing. A cost of
        /// zero or less is free: it succeeds and raises nothing.
        /// </summary>
        public bool TrySpend(int cost)
        {
            if (!Pool.TrySpend(cost))
            {
                SpendFailed?.Invoke(cost);
                return false;
            }
            if (cost > 0)
            {
                Changed?.Invoke(this);
                Spent?.Invoke(cost);
            }
            return true;
        }

        /// <summary>Fill the bar instantly and clear any delay (respawn,
        /// checkpoint, debug). Raises <see cref="Changed"/>, not
        /// <see cref="Filled"/>: the chime belongs to regen.</summary>
        public void Refill()
        {
            Pool.Refill();
            Changed?.Invoke(this);
        }

        /// <summary>
        /// Advance the bar by <paramref name="dt"/> seconds. Public so EditMode
        /// tests can drive time (EditMode has no game loop, and
        /// <c>Time.deltaTime</c> there is meaningless) — the same seam as
        /// <c>SpacetimeView.Tick</c>. <c>Update</c> calls
        /// <c>Tick(Time.deltaTime, IsStandingStill)</c>.
        /// </summary>
        public void Tick(float dt, bool standingStill)
        {
            ManaPool pool = Pool;
            int before = pool.Current;
            bool filled = pool.Tick(dt, standingStill);
            if (pool.Current != before) Changed?.Invoke(this);
            if (filled) Filled?.Invoke();
        }

        /// <summary>The pool, built on first use and rebuilt if a reload handed
        /// back an unbound one. A config swapped in the Inspector is picked up
        /// without losing the bar.</summary>
        ManaPool Pool
        {
            get
            {
                ManaConfig resolved = Config;
                if (_pool == null || !_pool.IsBound) _pool = new ManaPool(resolved);
                else if (!ReferenceEquals(_pool.Config, resolved)) _pool.Rebind(resolved);
                return _pool;
            }
        }

        /// <summary>The movement source, resolved once per enable (and once more
        /// after a reload). A source destroyed since reads as none.</summary>
        ICombatMovement Movement
        {
            get
            {
                if (!_movementResolved) ResolveMovement();
                return _movementComponent != null ? _movement : null;
            }
        }

        /// <summary>The serialized source if it implements the interface, else
        /// whatever on this object does. TryGetComponent rather than
        /// <c>GetComponent ?? …</c>: in the Editor a missing component comes back
        /// as a fake-null object, which C#'s <c>??</c> and interface casts treat
        /// as real (the same reasoning as MeleeCombat's).</summary>
        void ResolveMovement()
        {
            _movementResolved = true;
            _movement = null;
            if (movementSource != null)
            {
                _movement = movementSource as ICombatMovement;
                if (_movement == null)
                    Debug.LogWarning($"[Mana] {name}: movementSource {movementSource.GetType().Name} " +
                                     "does not implement ICombatMovement — ignoring it.", this);
            }
            if (_movement == null && TryGetComponent(out ICombatMovement found)) _movement = found;
            _movementComponent = _movement as Component;
        }

        void OnEnable()
        {
            _movementResolved = false;
            if (config == null)
                Debug.LogWarning($"[Mana] {name} has no ManaConfig — using the default tuning. " +
                                 "Run Tools ▸ Pickleball ▸ Setup Magic.", this);
        }

        void Update() => Tick(Time.deltaTime, IsStandingStill);

        // An Inspector edit (in Play mode especially) may have changed the source.
        void OnValidate() => _movementResolved = false;

        void OnDestroy()
        {
            if (_fallbackConfig == null) return;
            if (Application.isPlaying) Destroy(_fallbackConfig);
            else DestroyImmediate(_fallbackConfig);
        }
    }
}

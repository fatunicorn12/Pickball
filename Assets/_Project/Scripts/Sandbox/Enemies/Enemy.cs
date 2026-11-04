using System;
using Pickleball.Sandbox.Juggle;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// The base of every ordinary enemy, and on its own the basic one: stands still
    /// until the player comes in range, runs at them, and strikes on an interval once
    /// in reach. <b>Bosses do not inherit from this.</b> They script whole fights and
    /// ignore launches, so they share only <see cref="Health"/>.
    ///
    /// <b>The body is ordinary, so nothing else needs to know about enemies.</b> It is a
    /// <see cref="Health"/>, which already makes it hittable by the sword and spells,
    /// lockable, and worth XP. It answers hits through the same
    /// <see cref="JuggleState"/> and <see cref="RevengeGauge"/> as the training dummy, so
    /// every launcher, finisher and combo break-out in the moveset works on it.
    ///
    /// <b>Making a new enemy.</b> Most differences are numbers: make a new
    /// <see cref="EnemyConfig"/>. For new behaviour, subclass and override:
    /// <list type="bullet">
    /// <item><see cref="Think"/>: which state these senses call for.</item>
    /// <item><see cref="Steer"/>: how it moves in a state (a flanker, a kiter).</item>
    /// <item><see cref="Strike"/>: what an attack does (throw something, not a hit).</item>
    /// <item><see cref="OnStateChanged"/> and <see cref="OnDefeated"/>: reactions.</item>
    /// </list>
    /// Unity's messages are virtual here for one reason: a subclass that declared its
    /// own <c>Update</c> over a private one would silently replace it and the enemy
    /// would stop dead. Virtual turns that into a compiler warning. Call base.
    ///
    /// <b>Gameplay only.</b> It raises events; <see cref="EnemyWaddleView"/>,
    /// <see cref="HurtFlash"/> and any audio subscribe. Hot-reload safe: references and
    /// the plain-C# state are rebuilt lazily.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(EnemyMotor))]
    public class Enemy : MonoBehaviour
    {
        /// <summary>Seconds between looks for a player when there is none. A spawner
        /// that calls <see cref="SetTarget"/> never pays for one.</summary>
        const float TargetSearchInterval = 1f;

        /// <summary>Attack rate never slows past this share, however deep the slow, so
        /// a slow cannot divide the interval by zero.</summary>
        const float MinAttackRateScale = 0.1f;

        [Tooltip("This enemy's numbers: health, ranges, speed, damage, attack interval.")]
        [SerializeField] private EnemyConfig config;

        [Tooltip("Optional. The Health to go after. Empty finds the player on first use, " +
                 "which is what a prefab spawned at runtime wants.")]
        [SerializeField] private Health target;

        Health _health;
        EnemyMotor _motor;
        StatusReceiver _status;
        bool _statusLooked;
        JuggleState _juggle;
        RevengeGauge _revenge;
        AttackCadence _cadence;
        EnemyConfig _fallbackConfig;

        bool _vitalsApplied;
        bool _subscribed;
        bool _aware;
        bool _defeated;
        float _despawnAt = float.PositiveInfinity;
        float _nextTargetSearch;
        Vector3 _knockDirection;

        /// <summary>What it is doing now.</summary>
        public EnemyState State { get; private set; } = EnemyState.Idle;

        /// <summary>Raised when <see cref="State"/> changes: from, to.</summary>
        public event Action<EnemyState, EnemyState> StateChanged;

        /// <summary>Raised on each strike, whether or not the target's hit grace
        /// refused it, so the swing always shows.</summary>
        public event Action Struck;

        /// <summary>Raised on a hit it reacted to (not while armored).</summary>
        public event Action<HitImpact> Flinched;

        /// <summary>Raised once per combo break-out; see <see cref="RevengeGauge"/>.</summary>
        public event Action BrokeOut;

        /// <summary>Raised once, when it is finally dead: on the ground, health gone.</summary>
        public event Action Defeated;

        /// <summary>This enemy's numbers. Never null: an unassigned config logs nothing
        /// and falls back to the basic enemy's defaults, so a half-wired one still runs.</summary>
        public EnemyConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_fallbackConfig == null) _fallbackConfig = ScriptableObject.CreateInstance<EnemyConfig>();
                return _fallbackConfig;
            }
        }

        /// <summary>Its hit points.</summary>
        public Health Health => _health != null ? _health : (_health = GetComponent<Health>());

        /// <summary>Its movement. Moves nothing by itself.</summary>
        public EnemyMotor Motor => _motor != null ? _motor : (_motor = GetComponent<EnemyMotor>());

        /// <summary>Its juggle. For reading; hits drive it. Lazy, because plain C# state
        /// comes back null after a hot reload.</summary>
        public JuggleState Juggle => _juggle ??= new JuggleState(Config.Juggle);

        /// <summary>Its combo break-out tally. For reading, and lazy, like <see cref="Juggle"/>.</summary>
        public RevengeGauge Revenge => _revenge ??= new RevengeGauge(Config.Revenge);

        /// <summary>What it goes after, or null while it has no target.</summary>
        public Health Target => target;

        AttackCadence Cadence => _cadence ??= new AttackCadence();

        StatusReceiver Status
        {
            get
            {
                if (!_statusLooked)
                {
                    _status = GetComponent<StatusReceiver>();
                    _statusLooked = true;
                }
                return _status;
            }
        }

        /// <summary>Use these numbers. For a spawner choosing a type at runtime; call it
        /// before the enemy has taken any damage, since it refills the health.</summary>
        public void Configure(EnemyConfig newConfig)
        {
            config = newConfig;
            _vitalsApplied = false;
            ApplyVitals();
        }

        /// <summary>Go after this Health instead of looking for the player.</summary>
        public void SetTarget(Health newTarget) => target = newTarget;

        /// <summary>Subclass: call base first.</summary>
        protected virtual void Awake() => ApplyVitals();

        /// <summary>Subclass: call base first.</summary>
        protected virtual void OnEnable()
        {
            // Again here because a hot reload runs OnEnable but not Awake.
            ApplyVitals();
            Subscribe();
        }

        /// <summary>Subclass: call base.</summary>
        protected virtual void OnDisable() => Unsubscribe();

        /// <summary>Subclass: call base, or call <see cref="Tick"/> yourself.</summary>
        protected virtual void Update() => Tick(Time.time, Time.deltaTime);

        /// <summary>
        /// One frame of the enemy. Public so tests can drive time, since EditMode has no
        /// meaningful clock.
        /// </summary>
        /// <param name="now">Current time, s: the clock the target's Health reads.</param>
        /// <param name="dt">Frame time, s.</param>
        public void Tick(float now, float dt)
        {
            var cfg = Config;
            if (_defeated)
            {
                if (now >= _despawnAt) Despawn();
                return;
            }

            var juggle = Juggle;
            juggle.Settings = cfg.Juggle;
            float heightBefore = juggle.Height;
            bool carried = juggle.IsAirborne;
            if (juggle.Tick(dt) == JuggleLanding.LandedDefeated)
            {
                Defeat(now);
                return;
            }
            var revenge = Revenge;
            revenge.Settings = cfg.Revenge;
            revenge.Tick(dt);

            FindTargetIfNeeded(now);
            var senses = Sense(cfg);
            SetState(Think(in senses));

            bool striking = State == EnemyState.Attack;
            float rate = Mathf.Max(senses.SpeedMultiplier, MinAttackRateScale);
            if (Cadence.Tick(now, striking, cfg.AttackInterval / rate, cfg.FirstHitDelay))
            {
                Strike();
                Struck?.Invoke();
            }

            Vector3 velocity = juggle.IsKnockedAway
                ? _knockDirection * juggle.KnockSpeed
                : Steer(State, in senses, dt);

            var motor = Motor;
            motor.Step(velocity, carried || juggle.IsAirborne, juggle.Height - heightBefore,
                       cfg.Gravity, cfg.MaxFallSpeed, dt);
            if (State == EnemyState.Chase || State == EnemyState.Attack)
                motor.Face(senses.FlatDirection, cfg.TurnRate, dt);
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>Which state these senses call for. Override for new rules; the base
        /// is <see cref="EnemyBrain.Decide"/>.</summary>
        protected virtual EnemyState Think(in EnemySenses senses) => EnemyBrain.Decide(in senses);

        /// <summary>
        /// Horizontal velocity for this state, m/s. Not called while thrown by a
        /// knock-away. The base runs at the target and stops at the stop distance.
        /// </summary>
        protected virtual Vector3 Steer(EnemyState state, in EnemySenses senses, float dt)
        {
            if (state != EnemyState.Chase && state != EnemyState.Attack) return Vector3.zero;
            var cfg = Config;
            return senses.FlatDirection * EnemyBrain.ApproachSpeed(
                senses.FlatDistance, cfg.StopDistance, cfg.RunSpeed * senses.SpeedMultiplier, dt);
        }

        /// <summary>Land one attack; the interval says when. The base deals the config's
        /// damage straight to the target.</summary>
        protected virtual void Strike()
        {
            if (target != null) target.Damage(Config.AttackDamage, target.transform.position);
        }

        /// <summary>Called when <see cref="State"/> changes, before listeners hear.</summary>
        protected virtual void OnStateChanged(EnemyState from, EnemyState to) { }

        /// <summary>Called once at defeat, before listeners hear.</summary>
        protected virtual void OnDefeated() { }

        /// <summary>Remove the body once its corpse time is up. Override to pool it.</summary>
        protected virtual void Despawn()
        {
            if (Application.isPlaying) Destroy(gameObject);
        }

        // --------------------------------------------------------------- internals

        EnemySenses Sense(EnemyConfig cfg)
        {
            var status = Status;
            bool canAct = Juggle.CanAct && (status == null || !status.IsStaggered);
            float speed = status != null ? status.SpeedMultiplier : 1f;

            if (target == null || target.IsDead)
            {
                _aware = false;
                return new EnemySenses(false, canAct, false, false, false, 0f, Vector3.zero, speed);
            }

            Vector3 from = transform.position, to = target.transform.position;
            Vector3 flat = new(to.x - from.x, 0f, to.z - from.z);
            float flatDistance = flat.magnitude;
            Vector3 direction = flatDistance > 1e-4f ? flat / flatDistance : Vector3.zero;

            _aware = EnemyBrain.IsAware(_aware, Vector3.Distance(from, to), cfg.AggroRange, cfg.LoseRange);
            bool inReach = EnemyBrain.IsInReach(from, to, cfg.AttackReach, cfg.AttackHeight);
            return new EnemySenses(false, canAct, true, _aware, inReach, flatDistance, direction, speed);
        }

        void SetState(EnemyState next)
        {
            if (next == State) return;
            var previous = State;
            State = next;
            OnStateChanged(previous, next);
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>
        /// The player, found on first use. Prefabs cannot serialize a scene reference and
        /// spawned enemies have no setup script to wire one, so this is the one runtime
        /// search (CodingStandards §1), as <c>NewtonBoss</c> does. It runs only until one
        /// is found, and at most once a second until then.
        /// </summary>
        void FindTargetIfNeeded(float now)
        {
            if (target != null || now < _nextTargetSearch) return;
            _nextTargetSearch = now + TargetSearchInterval;
            var player = FindFirstObjectByType<ChibiLocomotion>();
            if (player != null) target = player.GetComponent<Health>();
        }

        void ApplyVitals()
        {
            if (_vitalsApplied) return;
            _vitalsApplied = true;
            Health.SetMaxHealth(Config.MaxHealth);
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            var health = Health;
            health.Hurt += OnHurt;
            health.Impacted += OnImpacted;
            health.Died += OnDied;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            var health = Health;
            if (health == null) return;
            health.Hurt -= OnHurt;
            health.Impacted -= OnImpacted;
            health.Died -= OnDied;
        }

        // Hurting an enemy cashes in the player's grey pool, as hurting a boss does: this
        // is what makes the damage it deals winnable back rather than simply lost.
        void OnHurt(float amount, Vector3 at)
        {
            if (target != null) target.CreditDamageDealt(amount);
        }

        void OnImpacted(HitImpact impact)
        {
            if (_defeated) return;

            var cfg = Config;
            var gauge = Revenge;
            gauge.Settings = cfg.Revenge;
            var juggle = Juggle;
            juggle.Settings = cfg.Juggle;

            if (gauge.Add(impact.Damage))
            {
                juggle.BreakOut();
                BrokeOut?.Invoke();
                return;
            }
            if (gauge.IsArmored) return;    // it hurts, but it no longer flinches

            Vector3 flat = Vector3.ProjectOnPlane(impact.Knockback, Vector3.up);
            float flatSpeed = flat.magnitude;
            // A body already flying keeps the line it was thrown on.
            if (flatSpeed > 1e-2f && !juggle.IsKnockedAway) _knockDirection = flat / flatSpeed;
            juggle.Hit(impact.Reaction, flatSpeed);
            Flinched?.Invoke(impact);
        }

        // A KO in the air waits for the ground (Tick hears LandedDefeated), and can still
        // be juggled on the way down.
        void OnDied(Health _)
        {
            var juggle = Juggle;
            juggle.Settings = Config.Juggle;
            if (juggle.Defeat()) Defeat(Time.time);
        }

        void Defeat(float now)
        {
            if (_defeated) return;
            _defeated = true;
            _despawnAt = now + Config.CorpseSeconds;
            Motor.BecomeIntangible();
            SetState(EnemyState.Dead);
            OnDefeated();
            Defeated?.Invoke();
        }

        /// <summary>Frees the stand-in config; it is built at runtime and owned here.</summary>
        protected virtual void OnDestroy()
        {
            if (_fallbackConfig == null) return;
            if (Application.isPlaying) Destroy(_fallbackConfig);
            else DestroyImmediate(_fallbackConfig);
        }
    }
}

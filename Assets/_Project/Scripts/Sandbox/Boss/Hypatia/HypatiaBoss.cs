using System;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Hypatia, the Astrolabe: the fight's brain.
    ///
    /// Owns WHEN things happen: independent timers for rings, conics and (phase 2)
    /// the burn, Newton's pattern rather than a sequencer, so one stalled attack
    /// never holds up the others. The attack components own HOW. Raises C# events
    /// for presentation (needle, window glow, HUD); never touches VFX, UI or audio
    /// itself.
    ///
    /// <b>The loop.</b> Rings spiral out on their own beat. A conic is chosen, the
    /// needle shows its e for <see cref="HypatiaConfig.NeedleLead"/>, the volley
    /// fires, and when it finishes she drops into the recalibration window and
    /// takes extra damage. The next conic's interval only starts counting when the
    /// window closes, so the fight breathes: dodge, read, punish. Every Pulse
    /// Interval she charges a knockback pulse that clears the space around her
    /// (never during the window or a topple: a pulse due then waits).
    ///
    /// <b>The topple.</b> Bat one of her orbiters back into her and she topples
    /// (<see cref="ToppleState"/>): down on the plate for Topple Duration on her
    /// clock, taking Topple Damage Multiplier (which replaces the window's, never
    /// stacks with it), with every attack timer paused, any open window closed,
    /// a conic waiting on its needle cancelled, a charging pulse dropped and every
    /// other orbiter shattered, so one volley cannot chain a second topple. What
    /// she has already thrown keeps flying, and the fire keeps burning.
    ///
    /// <b>Her clock.</b> Every timer and her body motion advance by
    /// <see cref="HypatiaContext.BossDelta"/>: dt times her slow multiplier from
    /// an optional <see cref="StatusReceiver"/> on this root. Nothing divides by
    /// it, so a multiplier of 0 (the planned Stationary Point) freezes her timers,
    /// her window, her hover and her animator exactly, and they resume where they
    /// stopped. What she has already thrown keeps its own clock.
    ///
    /// <b>Reload safety</b> (Newton's gap, QA NB1). Health, the slow source, the
    /// attack casts and the context are re-fetched in every <c>OnEnable</c>, and
    /// every attack is re-bound to the new context. Timers, the window and the
    /// phase are serializable private state, which Unity keeps across a script
    /// reload; a liveness check in <c>Update</c> restarts any timer a lost event
    /// left idle.
    ///
    /// <b>Endings</b> (Newton's gaps, QA NB2 and NB7). Her death stops every timer,
    /// closes the window, stops every attack and raises <see cref="Defeated"/> once.
    /// The player's death stops her attacks too. Both are idempotent. There is no
    /// retry path.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class HypatiaBoss : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Every tunable of the fight.")]
        [SerializeField] private HypatiaConfig config;

        [Header("Scene")]
        [Tooltip("Centre of the Rete plate; its Y is the floor.")]
        [SerializeField] private Transform arenaCentre;
        [Tooltip("The player root (moves with the player).")]
        [SerializeField] private Transform player;
        [Tooltip("The player's Health: takes her damage, and is credited the " +
                 "player's regain when she is hurt.")]
        [SerializeField] private Health playerHealth;

        [Header("Attacks (on this root)")]
        [Tooltip("An IRingAttack.")]
        [SerializeField] private MonoBehaviour rings;
        [Tooltip("An IConicAttack.")]
        [SerializeField] private MonoBehaviour conics;
        [Tooltip("An IArenaBurn.")]
        [SerializeField] private MonoBehaviour burn;
        [Tooltip("An IPulseAttack (the knockback pulse).")]
        [SerializeField] private MonoBehaviour pulse;

        [Header("Body")]
        [Tooltip("The visual model, moved for hover and bob. A child of this root; " +
                 "its local Y is her feet's height above the plate. Its CapsuleCollider " +
                 "is her hurtbox, which batted orbiters home on.")]
        [SerializeField] private Transform body;
        [Tooltip("Her animator; its speed follows her slow in code.")]
        [SerializeField] private Animator animator;

        [Header("Personality")]
        [Tooltip("Her lines. Optional; found on this root when empty.")]
        [SerializeField] private HypatiaBarks barks;

        /// <summary>The needle should show <c>e</c> for this conic now; it fires
        /// after <c>lead</c> seconds (on her clock). Args: kind, e, lead.</summary>
        public event Action<ConicKind, float, float> ConicTelegraphed;

        /// <summary>The recalibration window opened, lasting this many seconds.</summary>
        public event Action<float> RecalibrationStarted;

        /// <summary>The recalibration window closed.</summary>
        public event Action RecalibrationEnded;

        /// <summary>Alexandria's Burning began. Raised once.</summary>
        public event Action PhaseTwoStarted;

        /// <summary>She died; every attack has been stopped. Raised once.</summary>
        public event Action Defeated;

        /// <summary>A batted orbiter toppled her for this many seconds (her clock).</summary>
        public event Action<float> Toppled;

        /// <summary>The topple ended: she rises (also raised when her death or the
        /// player's ends a topple early).</summary>
        public event Action Recovered;

        /// <summary>The fight's tunables, for presentation that needs the same numbers.</summary>
        public HypatiaConfig Config => config;

        /// <summary>The live context; rebuilt in every OnEnable.</summary>
        public HypatiaContext Context { get; private set; }

        /// <summary>1 or 2. One-way; survives a script reload.</summary>
        public int Phase => _phase;

        /// <summary>True during the recalibration window.</summary>
        public bool IsRecalibrating => _window != null && _window.IsOpen;

        /// <summary>Seconds left in the recalibration window, on her clock; 0 when
        /// closed. For a HUD that drains with the window.</summary>
        public float RecalibrationRemaining => _window != null ? _window.Remaining : 0f;

        /// <summary>True while she is toppled.</summary>
        public bool IsToppled => _topple != null && _topple.IsToppled;

        /// <summary>Seconds left in the topple, on her clock; 0 when not toppled.</summary>
        public float ToppleRemaining => _topple != null ? _topple.Remaining : 0f;

        /// <summary>True once she has started and until she or the player dies.</summary>
        public bool IsFighting => _started && !_stopped;

        /// <summary>True from her death on.</summary>
        public bool IsDefeated => _defeated;

        // ── Re-fetched every OnEnable (not serializable, so null after a reload) ──
        Health _health;
        StatusReceiver _status;
        IRingAttack _rings;
        IConicAttack _conics;
        IArenaBurn _burn;
        IPulseAttack _pulse;
        CapsuleCollider _hurtbox;
        Health _subscribedPlayerHealth;
        IConicAttack _subscribedConics;
        RecalibrationWindow _subscribedWindow;
        ToppleState _subscribedTopple;
        ConicPicker _picker;
        bool _bodyMovable;

        // ── Fight state: private and serializable, so a script reload keeps it ──
        RecalibrationWindow _window = new();
        ToppleState _topple = new();
        BossTimer _ringTimer, _conicTimer, _needleTimer, _burnTimer, _pulseTimer;
        bool _pulseDue;
        float _barkTimer;
        int _phase = 1;
        bool _started, _stopped, _defeated;
        bool _conicDue, _awaitingVolley;
        ConicKind _pendingKind;
        float _pendingEccentricity;
        bool _hoverInitialised;
        Vector3 _bodyBase;
        float _hoverHeight, _bobCycle;
        float _bobWeight = 1f;

        void OnEnable()
        {
            _health = GetComponent<Health>();
            _status = GetComponent<StatusReceiver>();
            if (barks == null) barks = GetComponent<HypatiaBarks>();
            _picker ??= new ConicPicker(new System.Random());
            _window ??= new RecalibrationWindow();
            _topple ??= new ToppleState();

            CharacterController playerController =
                player != null ? player.GetComponent<CharacterController>() : null;
            _hurtbox = body != null ? body.GetComponent<CapsuleCollider>() : null;
            if (_defeated && _hurtbox != null) _hurtbox.enabled = false;
            Context = new HypatiaContext(config, transform, arenaCentre, player, playerHealth,
                                         playerController, ReadTimeScale, _health, _hurtbox);
            // A reload (or a disable/enable) mid phase 2 must not drop the attacks
            // back to phase 1 behaviour.
            if (_phase >= 2) Context.EnterPhaseTwo();

            _rings = AsAttack<IRingAttack>(rings, nameof(rings));
            _conics = AsAttack<IConicAttack>(conics, nameof(conics));
            _burn = AsAttack<IArenaBurn>(burn, nameof(burn));
            _pulse = AsAttack<IPulseAttack>(pulse, nameof(pulse));

            if (config == null)
            {
                Debug.LogError($"[HypatiaBoss] No HypatiaConfig on {name}; she will not fight. " +
                               "Re-run Tools ▸ Pickleball ▸ Setup Hypatia Fight.", this);
            }
            else
            {
                _rings?.Bind(Context);
                _conics?.Bind(Context);
                _burn?.Bind(Context);
                _pulse?.Bind(Context);
            }

            _health.Hurt += OnHurt;
            _health.Died += OnDied;
            if (!_defeated) _health.IncomingDamageFilterByKind = FilterDamage;

            _subscribedPlayerHealth = playerHealth;
            if (_subscribedPlayerHealth != null) _subscribedPlayerHealth.Died += OnPlayerDied;

            _subscribedConics = _conics;
            if (_subscribedConics != null)
            {
                _subscribedConics.VolleyFinished += OnVolleyFinished;
                _subscribedConics.OrbiterStruckBoss += OnOrbiterStruckBoss;
            }

            _subscribedWindow = _window;
            _subscribedWindow.Opened += OnWindowOpened;
            _subscribedWindow.Closed += OnWindowClosed;

            _subscribedTopple = _topple;
            _subscribedTopple.Started += OnToppleStarted;
            _subscribedTopple.Ended += OnToppleEnded;

            if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            InitialiseBody();
        }

        void OnDisable()
        {
            if (_health != null)
            {
                _health.Hurt -= OnHurt;
                _health.Died -= OnDied;
                _health.IncomingDamageFilterByKind = null;
            }
            if (_subscribedPlayerHealth != null) _subscribedPlayerHealth.Died -= OnPlayerDied;
            _subscribedPlayerHealth = null;
            if (_subscribedConics != null)
            {
                _subscribedConics.VolleyFinished -= OnVolleyFinished;
                _subscribedConics.OrbiterStruckBoss -= OnOrbiterStruckBoss;
            }
            _subscribedConics = null;
            if (_subscribedWindow != null)
            {
                _subscribedWindow.Opened -= OnWindowOpened;
                _subscribedWindow.Closed -= OnWindowClosed;
            }
            _subscribedWindow = null;
            if (_subscribedTopple != null)
            {
                _subscribedTopple.Started -= OnToppleStarted;
                _subscribedTopple.Ended -= OnToppleEnded;
            }
            _subscribedTopple = null;
        }

        void Start()
        {
            if (config == null || _stopped) return;
            _started = true;
            _ringTimer.Start(config.FirstRingDelay);
            _conicTimer.Start(config.FirstConicDelay);
            _pulseTimer.Start(config.FirstPulseDelay);
            _barkTimer = config.UnpromptedBarkInterval;
            Say(HypatiaBarks.Occasion.Intro);
        }

        void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// One frame of the brain, <paramref name="dt"/> real seconds (her clock
        /// scales it). Called from Update; public as the EditMode test seam, the
        /// way <see cref="Conics.ConicCaster.Tick"/> is.
        /// </summary>
        public void Tick(float dt)
        {
            if (config == null || Context == null) return;

            float bossDt = Context.BossDelta(dt);
            if (animator != null) animator.speed = Context.BossTimeScale;
            if (_defeated)
            {
                // She comes down to the plate instead of dying in mid-air.
                UpdateBody(bossDt);
                return;
            }

            // Real dt: her slow does not slow the player.
            if (player != null) Context.PlayerVelocity.Sample(player.position, dt);
            UpdateBody(bossDt);

            if (!IsFighting) return;
            CheckPhase();
            _topple.Tick(bossDt);
            _window.Tick(bossDt);
            if (IsToppled)
            {
                // Every attack timer holds. Only a volley already in progress is
                // watched, so one that ends during the topple is still closed out.
                if (_awaitingVolley && _conics != null && !_conics.IsBusy) FinishVolley();
            }
            else
            {
                UpdateRings(bossDt);
                UpdateConics(bossDt);
                UpdateBurn(bossDt);
            }
            UpdatePulse(bossDt);
            UpdateUnpromptedBark(dt);
        }

        // ─── Timers ──────────────────────────────────────────────────────────

        void UpdateRings(float bossDt)
        {
            if (_rings == null) return;
            if (IsRecalibrating && !config.RingsDuringRecalibration) return;

            // Liveness: a timer left idle by a reload restarts on the interval.
            if (!_ringTimer.IsRunning) _ringTimer.Start(config.RingInterval(PhaseTwo));
            if (!_ringTimer.Tick(bossDt)) return;

            if (_rings.QueueRing()) Say(HypatiaBarks.Occasion.Rings);
            _ringTimer.Start(config.RingInterval(PhaseTwo));
        }

        /// <summary>
        /// The conic cycle: interval → telegraph (needle) → fire → wait for the
        /// volley → recalibration window → interval. Each stage is one piece of
        /// state, so the cycle can resume from any of them after a reload.
        /// </summary>
        void UpdateConics(float bossDt)
        {
            if (_conics == null) return;

            if (_awaitingVolley)
            {
                // IsBusy runs from Fire to VolleyFinished; if it has dropped with no
                // event (a refused Fire, an event lost to a reload) end the volley
                // here rather than wait forever.
                if (!_conics.IsBusy) FinishVolley();
                return;
            }

            if (_needleTimer.IsRunning)
            {
                if (_needleTimer.Tick(bossDt)) FireConic();
                return;
            }

            if (IsRecalibrating) return;

            if (!_conicDue)
            {
                if (!_conicTimer.IsRunning) _conicTimer.Start(config.ConicInterval(PhaseTwo));
                if (!_conicTimer.Tick(bossDt)) return;
                _conicDue = true;
            }

            // Due but the attack is still finishing something: hold, then go.
            if (_conics.IsBusy) return;
            TelegraphConic();
        }

        void TelegraphConic()
        {
            _conicDue = false;
            if (!_picker.TryPick(config, out ConicKind kind))
            {
                // Every weight is zero: no conics, but keep checking in case they
                // are tuned back in live.
                _conicTimer.Start(config.ConicInterval(PhaseTwo));
                return;
            }

            _pendingKind = kind;
            _pendingEccentricity = _picker.PickEccentricity(kind, config);
            float lead = Mathf.Max(0f, config.NeedleLead);

            ConicTelegraphed?.Invoke(kind, _pendingEccentricity, lead);
            Say(kind switch
            {
                ConicKind.Parabola => HypatiaBarks.Occasion.Parabola,
                ConicKind.Ellipse => HypatiaBarks.Occasion.Ellipse,
                _ => HypatiaBarks.Occasion.Hyperbola,
            });

            if (lead > 0f) _needleTimer.Start(lead);
            else FireConic();
        }

        void FireConic()
        {
            _needleTimer.Stop();
            if (_stopped || _conics == null) return;
            // Set before Fire: a volley that finishes synchronously inside Fire
            // must still be seen as the volley we are waiting for.
            _awaitingVolley = true;
            _conics.Fire(_pendingKind, _pendingEccentricity);
        }

        void OnVolleyFinished(ConicKind kind)
        {
            if (_stopped || !_awaitingVolley) return;
            FinishVolley();
        }

        void FinishVolley()
        {
            _awaitingVolley = false;
            _conicDue = false;
            _conicTimer.Stop();
            if (IsToppled)
            {
                // The topple already is her punish beat: no window on top of it.
                // The next conic's interval starts now and holds until she rises.
                if (!_stopped && config != null) _conicTimer.Start(config.ConicInterval(PhaseTwo));
                return;
            }
            _window.Open(config != null ? config.RecalibrationDuration : 0f);
        }

        void OnWindowOpened(float duration)
        {
            DeferChargingPulse();
            RecalibrationStarted?.Invoke(duration);
            Say(HypatiaBarks.Occasion.Recalibration);
        }

        void OnWindowClosed()
        {
            RecalibrationEnded?.Invoke();
            // The next conic's interval counts from the END of the window.
            if (!_stopped && config != null) _conicTimer.Start(config.ConicInterval(PhaseTwo));
        }

        void UpdateBurn(float bossDt)
        {
            if (!PhaseTwo || _burn == null) return;

            if (!_burnTimer.IsRunning)
            {
                if (!_burn.CanIgnite) return;      // the floor has burned all it can
                _burnTimer.Start(config.BurnInterval);
            }
            if (!_burnTimer.Tick(bossDt)) return;

            if (_burn.CanIgnite) _burn.IgniteNext();
            if (_burn.CanIgnite) _burnTimer.Start(config.BurnInterval);
        }

        /// <summary>
        /// The pulse cycle: first after First Pulse Delay, then every Pulse Interval,
        /// counted from each charge's start. The timer does not run while she is
        /// recalibrating or toppled, and a pulse that comes due then waits for the
        /// window or topple to end before it starts charging.
        /// </summary>
        void UpdatePulse(float bossDt)
        {
            if (_pulse == null) return;
            if (IsRecalibrating || IsToppled) return;

            if (!_pulseDue)
            {
                // Liveness: a timer left idle by a reload restarts on the interval.
                if (!_pulseTimer.IsRunning) _pulseTimer.Start(config.PulseInterval);
                if (!_pulseTimer.Tick(bossDt)) return;
                _pulseDue = true;
            }

            // Still charging the last one (an interval shorter than the charge): go
            // as soon as it has fired.
            if (_pulse.IsCharging) return;
            _pulseDue = false;
            _pulseTimer.Start(config.PulseInterval);
            _pulse.BeginPulse();
        }

        /// <summary>A charge in progress when the window opens or she topples is
        /// dropped, and the pulse is due again as soon as she is back up: it never
        /// fires into her own punish beat.</summary>
        void DeferChargingPulse()
        {
            if (_pulse == null || !_pulse.IsCharging) return;
            _pulse.CancelPulse();
            _pulseTimer.Stop();
            _pulseDue = !_stopped;
        }

        // ─── Topple ──────────────────────────────────────────────────────────

        /// <summary>A batted orbiter reached her (its damage is already dealt).</summary>
        void OnOrbiterStruckBoss(Vector3 at)
        {
            if (!IsFighting || _defeated || config == null) return;
            if (_health != null && _health.IsDead) return;
            _topple.Begin(config.ToppleDuration);   // refused while already toppled
        }

        void OnToppleStarted(float duration)
        {
            // Topple wins over recalibration: close the window (its Closed edge
            // restarts the conic interval, which then holds until she rises).
            _window.Close();
            if (_needleTimer.IsRunning)
            {
                // The conic the needle was announcing never fires.
                _needleTimer.Stop();
                _conicDue = false;
                _conicTimer.Start(config.ConicInterval(PhaseTwo));
            }
            DeferChargingPulse();
            _conics?.ShatterOrbiters();
            Toppled?.Invoke(duration);
            Say(HypatiaBarks.Occasion.Toppled);
        }

        void OnToppleEnded() => Recovered?.Invoke();

        void UpdateUnpromptedBark(float realDt)
        {
            _barkTimer -= realDt;
            if (_barkTimer > 0f) return;
            _barkTimer = config.UnpromptedBarkInterval;
            Say(HypatiaBarks.Occasion.Unprompted);
        }

        // ─── Phase ───────────────────────────────────────────────────────────

        bool PhaseTwo => _phase >= 2;

        void CheckPhase()
        {
            if (PhaseTwo || _stopped || config == null || _health == null || _health.IsDead) return;
            if (_health.CurrentNormalised > config.PhaseTwoAt) return;
            EnterPhaseTwo();
        }

        void EnterPhaseTwo()
        {
            _phase = 2;
            Context?.EnterPhaseTwo();
            _burnTimer.Start(config.FirstBurnDelay);
            PhaseTwoStarted?.Invoke();
            Say(HypatiaBarks.Occasion.PhaseTwo);
        }

        // ─── Damage ──────────────────────────────────────────────────────────

        /// <summary>Damage that landed on her is the player's regain currency, as
        /// with Newton: this is where a good punish pays back a bad exchange.</summary>
        void OnHurt(float applied, Vector3 at)
        {
            if (playerHealth != null) playerHealth.CreditDamageDealt(applied);
            if (_health != null && _health.IsDead) return;   // the killing blow: Died speaks
            Say(HypatiaBarks.Occasion.Hurt);
            CheckPhase();
        }

        float FilterDamage(float amount, Vector3 at, DamageKind kind)
        {
            if (config == null) return ScaleIncomingDamage(amount, false, 1f, 1f);
            return ScaleIncomingDamage(amount, IsToppled, IsRecalibrating,
                                       config.ToppleDamageMultiplier,
                                       config.RecalibrationDamageMultiplier,
                                       config.NormalDamageMultiplier);
        }

        /// <summary>
        /// Her damage rule with the topple: the topple multiplier while toppled
        /// (it REPLACES the recalibration one; the two never multiply), else the
        /// recalibration rule below. Same sanitising: NaN, infinite or non-positive
        /// in, or a NaN multiplier, yields 0.
        /// </summary>
        public static float ScaleIncomingDamage(float amount, bool toppled, bool recalibrating,
                                                float toppleMultiplier, float recalibrationMultiplier,
                                                float normalMultiplier)
        {
            if (!toppled) return ScaleIncomingDamage(amount, recalibrating, recalibrationMultiplier, normalMultiplier);
            return ScaleIncomingDamage(amount, true, toppleMultiplier, normalMultiplier);
        }

        /// <summary>
        /// Her damage rule, for every <see cref="DamageKind"/>: the recalibration
        /// multiplier inside the window, the normal one outside. A NaN, infinite or
        /// non-positive amount, or a NaN multiplier, yields 0 (no hit) rather than
        /// anything Health could misread. Pure, so it is tested without a scene.
        /// </summary>
        public static float ScaleIncomingDamage(float amount, bool recalibrating,
                                                float recalibrationMultiplier,
                                                float normalMultiplier)
        {
            if (!(amount > 0f) || float.IsInfinity(amount)) return 0f;
            float multiplier = recalibrating ? recalibrationMultiplier : normalMultiplier;
            if (!(multiplier > 0f)) return 0f;
            float scaled = amount * multiplier;
            return float.IsInfinity(scaled) ? 0f : scaled;
        }

        // ─── Endings ─────────────────────────────────────────────────────────

        void OnDied(Health health)
        {
            if (_defeated) return;
            _defeated = true;
            StopFighting();
            if (_health != null) _health.IncomingDamageFilterByKind = null;
            // The capsule stays upright where she stood while her body falls away from it: left on,
            // it is an invisible pillar, and it comes down onto anyone standing under her.
            if (_hurtbox != null) _hurtbox.enabled = false;
            Defeated?.Invoke();
            Say(HypatiaBarks.Occasion.Death);
        }

        /// <summary>The player fell: she stops fighting. No retry path yet.</summary>
        void OnPlayerDied(Health health) => StopFighting();

        /// <summary>Stop every timer, close the window, stop every attack. Safe to
        /// call more than once (StopAll is idempotent by contract).</summary>
        void StopFighting()
        {
            // First, so the window's Closed edge does not restart the conic timer
            // and a VolleyFinished raised by StopAll is ignored.
            _stopped = true;
            _ringTimer.Stop();
            _conicTimer.Stop();
            _needleTimer.Stop();
            _burnTimer.Stop();
            _pulseTimer.Stop();
            _pulseDue = false;
            _conicDue = false;
            _awaitingVolley = false;
            _window?.Close();
            _topple?.End();
            _rings?.StopAll();
            _conics?.StopAll();
            _burn?.StopAll();
            _pulse?.StopAll();
        }

        // ─── Body ────────────────────────────────────────────────────────────

        void InitialiseBody()
        {
            _bodyMovable = body != null && body != transform;
            if (!_bodyMovable)
            {
                Debug.LogWarning($"[HypatiaBoss] No separate 'body' child wired on {name}; " +
                                 "she will not hover (the root stays on the plate). Re-run " +
                                 "Tools ▸ Pickleball ▸ Setup Hypatia Fight.", this);
                return;
            }
            if (_hoverInitialised) return;
            _bodyBase = body.localPosition;
            _hoverHeight = body.localPosition.y;   // rise from wherever setup put her
            _hoverInitialised = true;
        }

        /// <summary>Hover toward the state's height (toppled, recalibrating or
        /// normal) with exponential smoothing, bob on top (not while down on the
        /// plate, toppled or dead), and turn the root to face the player (not while
        /// down), all on her clock. The root never translates: she does not walk.</summary>
        void UpdateBody(float bossDt)
        {
            bool down = IsToppled || _defeated;
            if (_bodyMovable)
            {
                float target = _defeated ? config.DefeatedHoverHeight
                             : down ? config.ToppleHoverHeight
                             : IsRecalibrating ? config.RecalibrationHoverHeight
                             : config.HoverHeight;
                float settle = 1f - Mathf.Exp(-config.HoverSettleRate * bossDt);
                _hoverHeight += (target - _hoverHeight) * settle;
                _bobWeight += ((down ? 0f : 1f) - _bobWeight) * settle;
                _bobCycle = Mathf.Repeat(_bobCycle + bossDt / Mathf.Max(0.1f, config.HoverBobPeriod), 1f);
                float y = _hoverHeight + _bobWeight * config.HoverBobAmplitude * Mathf.Sin(_bobCycle * 2f * Mathf.PI);
                body.localPosition = new Vector3(_bodyBase.x, Mathf.Max(0f, y), _bodyBase.z);
            }

            if (down || player == null || !(bossDt > 0f)) return;
            Vector3 look = player.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(look.normalized, Vector3.up),
                config.TurnDegreesPerSecond * bossDt);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        float ReadTimeScale() => _status != null ? _status.SpeedMultiplier : 1f;

        void Say(HypatiaBarks.Occasion occasion)
        {
            if (barks != null) barks.Say(occasion);
        }

        T AsAttack<T>(MonoBehaviour source, string field) where T : class
        {
            if (source == null) return null;
            if (source is T attack) return attack;
            Debug.LogWarning($"[HypatiaBoss] '{field}' on {name} is a {source.GetType().Name}, " +
                             $"not an {typeof(T).Name}; that attack is off.", this);
            return null;
        }
    }
}

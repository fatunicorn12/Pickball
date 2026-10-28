using System;
using Pickleball.Sandbox.Movement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The player's spellcasting, Expedition 33 style: hold aim (right mouse /
    /// LT) to raise the crosshair, press fire (left mouse / RT) to cast the
    /// selected spell at whatever is under it, paying mana per cast.
    ///
    /// It only simulates. Buttons come from <see cref="SpellInput"/>, mana from
    /// <see cref="Mana"/>, the selected spell from whoever calls
    /// <see cref="SetSpell"/> (the <see cref="SpellSelector"/>), and everything
    /// else is announced: the camera, lock-on and sword read
    /// <see cref="IsAiming"/> through <see cref="IAimState"/> and step aside;
    /// the HUD reads the aim, cooldown and channel state; whatever caps
    /// movement while aiming listens to <see cref="AimStarted"/> /
    /// <see cref="AimEnded"/>. The caster itself never touches movement input.
    ///
    /// <b>Deliveries</b> (<see cref="SpellDefinition.Delivery"/>):
    ///  - <b>Orb</b> (Arcane Orb, Fireball): a <see cref="MagicOrb"/> launched from
    ///    the cast origin at the crosshair, homing on what was under it.
    ///  - <b>Strike</b> (Thunderbolt): a <see cref="LightningStrike"/> on the
    ///    ground under the crosshair, following the enemy it was cast on.
    ///  - <b>Zone</b> (Freeze Zone): a <see cref="FreezeZone"/> on the ground under
    ///    the crosshair.
    ///  - <b>Channel</b> (Heal): fire held, standing still, for the spell's time;
    ///    the rules are <see cref="SpellChannel"/>'s. Any movement input, a jump,
    ///    a dash or tether (or any busy move), a real hit (not a chip tick), being
    ///    carried off the spot, letting go, or the gate blocking Spell cancels it.
    /// Strike and Zone need floor under the crosshair (<see cref="AimPlaceable"/>);
    /// without it a press is refused before any mana is spent. It never claims the input gate: the
    ///    player stays free to move, and moving is how they cancel.
    /// Each spell has its own cooldown (<see cref="SpellCooldowns"/>), so
    /// switching never resets one.
    ///
    /// Facing: while aiming it is the body's urgent facing source (registered
    /// with <see cref="ChibiLocomotion.SetCombatFacingSource"/>), which makes the
    /// motor strafe — travel follows the stick while the body squares up to the
    /// crosshair — exactly like holding aim in a shooter. Not aiming, it
    /// declines, and lock-on facing applies as before.
    ///
    /// Aim is resolved at most once per frame, on first use (Update, the facing
    /// query or a HUD read, whichever comes first), so every reader in a frame
    /// sees the same point whatever the script order.
    /// </summary>
    public class SpellCaster : MonoBehaviour, IAimState, IFacingSource
    {
        const int AimHitCapacity = 16;

        // Where the orb leaves from when no cast origin is wired: about chest
        // height, a little in front of the body, in metres. A wiring fallback,
        // not a tunable — the setup gives the caster a real hand anchor.
        const float FallbackOriginHeight = 1.3f;
        const float FallbackOriginForward = 0.4f;

        [Header("Spell")]
        [Tooltip("The spell cast: cost, cooldown, aim and delivery tuning. The SpellSelector " +
                 "swaps it; without one the caster never aims.")]
        [SerializeField] private SpellDefinition spell;

        [Header("Wiring")]
        [Tooltip("Mana paid per shot. Found on this object when empty; without it " +
                 "every shot fails.")]
        [SerializeField] private Mana mana;
        [Tooltip("Aim and fire buttons. Found on this object when empty.")]
        [SerializeField] private SpellInput input;
        [Tooltip("Where orbs spawn (a hand or staff tip). Empty = 1.3 m up and 0.4 m " +
                 "in front of this transform.")]
        [SerializeField] private Transform castOrigin;
        [Tooltip("Camera whose screen centre is the crosshair. Falls back to the main " +
                 "camera when empty.")]
        [SerializeField] private Camera aimCamera;
        [Tooltip("The player's movement facade: aiming is refused while it is busy " +
                 "(dash, tether, skateboard), and it is told to face the crosshair " +
                 "while aiming. A channel cancels when it leaves the ground. Found on this " +
                 "object when empty.")]
        [SerializeField] private ChibiLocomotion locomotion;
        [Tooltip("The caster's own health: what a Channel spell (Heal) restores, and being " +
                 "hurt cancels a channel. Found on this object when empty.")]
        [SerializeField] private Health health;
        [Tooltip("Read, never consumed, during a channel: any movement input cancels it. " +
                 "Found on this object when empty; without it only the motor's state counts.")]
        [SerializeField] private PlayerMovementInput movementInput;
        [Tooltip("Optional. A channel cancels the moment Spell is blocked (a conversation), " +
                 "and says so. Found on this object when empty.")]
        [SerializeField] private PlayerInputGate inputGate;

        [Header("Aim")]
        [Tooltip("Layers the crosshair ray can stop on. Triggers are always ignored, " +
                 "and so are the caster's own colliders.")]
        [SerializeField] private LayerMask aimMask = ~0;

        /// <summary>
        /// Aiming began: aim is held and allowed. Raised from Update on the first
        /// such frame.
        /// </summary>
        public event Action AimStarted;

        /// <summary>
        /// Aiming stopped. <b>Guaranteed</b> exactly once after every
        /// <see cref="AimStarted"/>, whatever the reason: the button was released,
        /// a busy move (dash, tether, skateboard) took the body, the spell or input
        /// went missing, or this component was disabled or destroyed (raised from
        /// OnDisable, which Unity runs before OnDestroy and before a script
        /// reload). Listeners that claim something on AimStarted — a movement cap,
        /// an input gate — can rely on it to release the claim.
        /// </summary>
        public event Action AimEnded;

        /// <summary>A spell was cast and paid for: an orb launched, a strike called,
        /// a zone placed, or a channel completed (after the heal). Raised after
        /// the cooldown has started.</summary>
        public event Action<SpellDefinition> Cast;

        /// <summary>Fire was pressed while aiming, off cooldown, but mana was short
        /// (for a channel: at its start, or when its completion could not pay).
        /// Presses during the cooldown are not failures and raise nothing.</summary>
        public event Action CastFailed;

        /// <summary>The spell changed through <see cref="SetSpell"/>.</summary>
        public event Action<SpellDefinition> SpellChanged;

        /// <summary>A channel began (fire pressed while aiming a Channel spell).</summary>
        public event Action<SpellDefinition> ChannelStarted;

        /// <summary>A channel completed and its effect applied (raised before
        /// <see cref="Cast"/>).</summary>
        public event Action<SpellDefinition> ChannelCompleted;

        /// <summary>A channel was refused at its start or cancelled while running,
        /// and why. A refusal raises this without <see cref="ChannelStarted"/>.</summary>
        public event Action<SpellDefinition, ChannelStopReason> ChannelCancelled;

        RaycastHit[] _aimHits;
        Vector3 _aimPoint;
        Transform _aimTarget;
        Vector3 _groundPoint;
        bool _groundOk;
        float _lastHealed;
        int _aimFrame = -1;
        bool _aimResolved;
        bool _aimLatched;
        bool _warnedNoPrefab, _warnedNoMana, _warnedNoHealth;
        bool _hurtPending;

        // Built on first use, never readonly: a script reload resets them to
        // null (cooldowns then read as ready, a running channel just ends).
        SpellCooldowns _cooldowns;
        SpellChannel _channel;
        SpellDefinition _channelSpell;
        Health _subscribedHealth;

        SpellCooldowns Cooldowns => _cooldowns ??= new SpellCooldowns();
        SpellChannel Channel => _channel ??= new SpellChannel();

        /// <summary>
        /// True every frame aim is held and allowed: aim button down, a spell
        /// assigned, the body not owned by a busy move, and this component active.
        /// Evaluated live, so it can lead <see cref="AimStarted"/> /
        /// <see cref="AimEnded"/> (raised from Update) by up to a frame.
        /// </summary>
        public bool IsAiming => isActiveAndEnabled && spell != null && input != null && input.AimHeld
                                && (locomotion == null || !locomotion.IsBusy);

        /// <summary>The selected spell; may be null (never aims).</summary>
        public SpellDefinition Spell => spell;

        /// <summary>The mana pool paid from; may be null (every shot fails).</summary>
        public Mana Mana => mana;

        /// <summary>The caster's health (what Heal restores); may be null.</summary>
        public Health Health => health;

        /// <summary>Mana points per cast of the selected spell; 0 without a spell.</summary>
        public int Cost => spell != null ? spell.ManaCost : 0;

        /// <summary>True while aiming a spell that uses the crosshair (everything
        /// but a channel). The crosshair HUD shows on this, not on aim alone.</summary>
        public bool ShowsCrosshair => IsAiming && spell.UsesCrosshair;

        /// <summary>World point under the crosshair this frame while aiming (a miss
        /// is the point at max range along the view). Holds the last aimed point
        /// when not aiming.</summary>
        public Vector3 AimPoint
        {
            get
            {
                RefreshAim();
                return _aimPoint;
            }
        }

        /// <summary>True while aiming with the crosshair (aim assist included) on
        /// something damageable that is not the caster.</summary>
        public bool AimOnTarget => AimTarget != null;

        /// <summary>The collider transform under the crosshair when
        /// <see cref="AimOnTarget"/>; what the next orb homes on, or a strike
        /// follows. Null otherwise.</summary>
        public Transform AimTarget
        {
            get
            {
                RefreshAim();
                return _aimTarget;
            }
        }

        /// <summary>True while aiming a spell that can land where it is aimed. Always
        /// true for orbs and channels. A ground spell (Strike, Zone) needs floor
        /// under the crosshair within range (<see cref="GroundProbe.TryGroundTarget"/>);
        /// a Thunderbolt on an enemy always has it. A press while this is false is
        /// refused, free, with <see cref="CastFailed"/>.</summary>
        public bool AimPlaceable
        {
            get
            {
                if (!IsAiming) return false;
                if (!NeedsGround(spell)) return true;
                RefreshAim();
                return _groundOk;
            }
        }

        /// <summary>Health the last completed channel actually restored (at most its
        /// heal amount, less when little was missing).</summary>
        public float LastHealed => _lastHealed;

        /// <summary>True when a fire press now would cast: aiming, off cooldown,
        /// the delivery's prefab (or, for a channel, health) present, somewhere to
        /// land (<see cref="AimPlaceable"/>) and the cost affordable.</summary>
        public bool CanCast => IsAiming && CooldownRemaining <= 0f && HasPayload(spell) && AimPlaceable
                               && mana != null && mana.CanAfford(Cost);

        /// <summary>1 right after a cast of the selected spell, falling to 0 when
        /// it can be cast again.</summary>
        public float CooldownNormalised => spell != null ? Cooldowns.Normalised(spell) : 0f;

        /// <summary>Seconds until the selected spell can be cast again; 0 when ready.</summary>
        public float CooldownRemaining => spell != null ? Cooldowns.Remaining(spell) : 0f;

        /// <summary>True while a channel runs.</summary>
        public bool IsChanneling => _channel != null && _channel.IsChanneling;

        /// <summary>0-1 through the running channel; after a cancel it holds where
        /// it broke, after a completion 1.</summary>
        public float ChannelProgress => _channel != null ? _channel.Progress : 0f;

        /// <summary>The spell being (or last) channelled; null before any.</summary>
        public SpellDefinition ChannelingSpell => _channelSpell;

        /// <summary>Seconds until <paramref name="which"/> can be cast again (selector HUD).</summary>
        public float CooldownRemainingFor(SpellDefinition which) => which != null ? Cooldowns.Remaining(which) : 0f;

        /// <summary>1 right after a cast of <paramref name="which"/>, falling to 0 (selector HUD).</summary>
        public float CooldownNormalisedFor(SpellDefinition which) => which != null ? Cooldowns.Normalised(which) : 0f;

        /// <summary>
        /// Selects <paramref name="next"/> as the spell cast. A running channel is
        /// cancelled (<see cref="ChannelStopReason.Switched"/>). Aiming carries
        /// on with the new spell; null ends it. Raises <see cref="SpellChanged"/>
        /// when it actually changed.
        /// </summary>
        public void SetSpell(SpellDefinition next)
        {
            if (next == spell) return;
            if (IsChanneling) StopChannel(ChannelStopReason.Switched);
            spell = next;
            _warnedNoPrefab = false;
            SpellChanged?.Invoke(spell);
        }

        /// <summary>The transform spells treat as their owner (never damaged, never
        /// aimed at): the player root when locomotion is wired, else this one.</summary>
        Transform Owner => locomotion != null ? locomotion.transform : transform;

        Vector3 CastOriginPosition => castOrigin != null
            ? castOrigin.position
            : transform.position + Vector3.up * FallbackOriginHeight + transform.forward * FallbackOriginForward;

        void Awake()
        {
            if (mana == null) mana = GetComponent<Mana>();
            if (input == null) input = GetComponent<SpellInput>();
            if (locomotion == null) locomotion = GetComponent<ChibiLocomotion>();
            if (health == null) health = GetComponent<Health>();
            if (movementInput == null) movementInput = GetComponent<PlayerMovementInput>();
            if (inputGate == null) inputGate = GetComponent<PlayerInputGate>();
            if (aimCamera == null) aimCamera = Camera.main;
        }

        void OnEnable()
        {
            _aimHits = new RaycastHit[AimHitCapacity];
            _aimLatched = false;
            _aimTarget = null;
            _aimFrame = -1;
            _aimResolved = false;
            _hurtPending = false;
            if (locomotion != null) locomotion.SetCombatFacingSource(this);
            SubscribeHealth(health);
            if (spell == null)
                Debug.LogWarning($"[SpellCaster] {name} has no SpellDefinition — aiming is off. " +
                                 "Run Tools ▸ Pickleball ▸ Setup Magic.", this);
        }

        void OnDisable()
        {
            // ChibiLocomotion has no getter for its combat facing source, so this
            // cannot check the slot is still ours; nothing else registers one.
            if (locomotion != null) locomotion.SetCombatFacingSource(null);
            _aimTarget = null;
            if (IsChanneling) StopChannel(ChannelStopReason.Disabled);
            SubscribeHealth(null);
            if (_aimLatched)
            {
                _aimLatched = false;
                AimEnded?.Invoke();
            }
        }

        void Update()
        {
            Cooldowns.Tick(Time.deltaTime);

            bool aiming = IsAiming;
            if (aiming != _aimLatched)
            {
                // Latch before raising, so a throwing listener cannot desync the edges.
                _aimLatched = aiming;
                if (aiming) AimStarted?.Invoke();
                else
                {
                    _aimTarget = null;
                    AimEnded?.Invoke();
                }
            }
            if (!aiming)
            {
                if (IsChanneling) StopChannel(AimLostReason());
                _hurtPending = false;
                return;
            }

            RefreshAim();
            if (spell.Delivery == SpellDelivery.Channel) UpdateChannel(Time.deltaTime);
            else TryFire();
            _hurtPending = false;
        }

        // ------------------------------------------------------------ firing

        void TryFire()
        {
            bool pressed = input.FirePressedThisFrame;
            bool wants = pressed || (spell.AutoRepeat && input.FireHeld);
            if (!wants || CooldownRemaining > 0f) return;   // cooldown-gated presses are silent

            if (!HasPayload(spell))
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogWarning($"[SpellCaster] Spell '{spell.name}' has no {spell.Delivery} prefab — " +
                                     "nothing to cast. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
                }
                return;
            }

            // No floor under the crosshair (the sky, a pit): refused before any
            // spend, so it costs nothing, with the same feedback as low mana.
            if (NeedsGround(spell) && !_groundOk)
            {
                if (pressed) CastFailed?.Invoke();
                return;
            }

            if (mana == null)
            {
                WarnNoMana();
                if (pressed) CastFailed?.Invoke();
                return;
            }

            // Held auto-repeat that runs dry stays quiet; only a fresh press says
            // "no mana". Checked before TrySpend, because a failed TrySpend raises
            // Mana.SpendFailed — every frame, while the button is held.
            if (!pressed && !mana.CanAfford(Cost)) return;
            if (!mana.TrySpend(Cost))
            {
                if (pressed) CastFailed?.Invoke();
                return;
            }

            switch (spell.Delivery)
            {
                case SpellDelivery.Strike: CallStrike(); break;
                case SpellDelivery.Zone: PlaceZone(); break;
                default: LaunchOrb(); break;
            }

            Cooldowns.Start(spell, spell.Cooldown);
            Cast?.Invoke(spell);
        }

        void LaunchOrb()
        {
            Vector3 origin = CastOriginPosition;
            Vector3 viewForward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            // On a target, fly at its body whatever the distance: the near-aim
            // fallback (camera forward) points at the floor at point-blank range.
            Vector3 direction = _aimTarget != null
                ? OrbMath.TargetLaunchDirection(origin, CentreOf(_aimTarget), viewForward)
                : OrbMath.LaunchDirection(origin, _aimPoint, viewForward, spell.MinAimDistance);

            var orb = Instantiate(spell.OrbPrefab, origin, Quaternion.LookRotation(direction));
            orb.Launch(origin, direction, spell, Owner, _aimTarget);
        }

        void CallStrike()
        {
            var strike = Instantiate(spell.StrikePrefab, _groundPoint, Quaternion.identity);
            strike.Begin(_groundPoint, _aimTarget, spell, Owner);
        }

        void PlaceZone()
        {
            var zone = Instantiate(spell.ZonePrefab, _groundPoint, Quaternion.identity);
            zone.Place(_groundPoint, spell, Owner);
        }

        static bool NeedsGround(SpellDefinition which)
            => which != null && (which.Delivery == SpellDelivery.Strike || which.Delivery == SpellDelivery.Zone);

        static Vector3 CentreOf(Transform target)
            => target.TryGetComponent(out Collider col) && col.enabled ? col.bounds.center : target.position;

        /// <summary>
        /// Where this frame's ground spell would land (<see cref="_groundPoint"/>)
        /// and whether it may (<see cref="_groundOk"/>). A Thunderbolt on an enemy
        /// lands on its feet (it follows the enemy anyway). Otherwise the floor
        /// under the crosshair, or under the enemy's feet for a zone, within the
        /// probe depth and the spell's range; none means refuse, never mid-air.
        /// </summary>
        void ResolveGround()
        {
            _groundOk = false;
            _groundPoint = _aimPoint;
            if (!NeedsGround(spell)) return;
            if (spell.Delivery == SpellDelivery.Strike && _aimTarget != null)
            {
                _groundPoint = GroundProbe.FeetOf(_aimTarget);
                _groundOk = true;
                return;
            }
            if (_aimHits == null) _aimHits = new RaycastHit[AimHitCapacity];
            Vector3 aim = _aimTarget != null ? GroundProbe.FeetOf(_aimTarget) : _aimPoint;
            _groundOk = GroundProbe.TryGroundTarget(aim, Owner.position, spell.MaxRange, spell.GroundProbeDistance,
                                                    spell.HitMask, Owner, _aimHits, out _groundPoint);
        }

        /// <summary>Whether <paramref name="which"/> has what its delivery needs to
        /// cast: its prefab, or for a channel the health it restores.</summary>
        bool HasPayload(SpellDefinition which)
        {
            if (which == null) return false;
            return which.Delivery switch
            {
                SpellDelivery.Strike => which.StrikePrefab != null,
                SpellDelivery.Zone => which.ZonePrefab != null,
                SpellDelivery.Channel => health != null,
                _ => which.OrbPrefab != null,
            };
        }

        void WarnNoMana()
        {
            if (_warnedNoMana) return;
            _warnedNoMana = true;
            Debug.LogWarning($"[SpellCaster] {name} has no Mana to pay with — every shot " +
                             "fails. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
        }

        // ----------------------------------------------------------- channel

        void UpdateChannel(float dt)
        {
            var signals = ReadSignals();

            if (!IsChanneling)
            {
                // A channel starts on a fresh press only, off cooldown.
                if (!input.FirePressedThisFrame || CooldownRemaining > 0f) return;
                if (health == null && !_warnedNoHealth)
                {
                    _warnedNoHealth = true;
                    Debug.LogWarning($"[SpellCaster] {name} has no Health to heal — '{spell.name}' " +
                                     "cannot be channelled. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
                }
                if (mana == null) WarnNoMana();

                var refused = Channel.TryBegin(signals, spell.ChannelSeconds, Cost, spell.ChannelManaTiming, mana,
                                               spell.ChannelMaxDrift);
                if (refused != ChannelStopReason.None)
                {
                    ChannelCancelled?.Invoke(spell, refused);
                    if (refused == ChannelStopReason.NoMana) CastFailed?.Invoke();
                    return;
                }
                _channelSpell = spell;
                ChannelStarted?.Invoke(spell);
                return;
            }

            switch (Channel.Tick(dt, signals, mana))
            {
                case ChannelStep.Completed:
                    _lastHealed = 0f;
                    if (health != null)
                    {
                        float before = health.Current;
                        health.Heal(spell.HealAmount);
                        _lastHealed = Mathf.Max(0f, health.Current - before);
                    }
                    Cooldowns.Start(spell, spell.Cooldown);
                    ChannelCompleted?.Invoke(spell);
                    Cast?.Invoke(spell);
                    break;
                case ChannelStep.Cancelled:
                    ChannelCancelled?.Invoke(_channelSpell, Channel.LastStop);
                    if (Channel.LastStop == ChannelStopReason.NoMana) CastFailed?.Invoke();
                    break;
            }
        }

        /// <summary>This frame's channel rules: fire held, movement input (read,
        /// not consumed), the motor's busy / grounded state, a hit since last
        /// frame, the gate, and whether there is health to restore.</summary>
        ChannelSignals ReadSignals()
        {
            var signals = new ChannelSignals
            {
                Held = input != null && input.FireHeld,
                Hurt = _hurtPending,
                Blocked = inputGate != null && inputGate.IsBlocked(InputBlock.Spell),
                Busy = locomotion != null && locomotion.IsBusy,
                Airborne = locomotion != null && !locomotion.IsGrounded,
                HealthUnavailable = health == null || health.IsDead,
                HealthFull = health != null && spell.RefuseAtFullHealth && health.Current >= health.Max,
                Position = Owner.position,
            };
            if (movementInput != null && movementInput.isActiveAndEnabled)
                signals.AddIntent(movementInput.Read(), spell.ChannelMoveDeadzone);
            return signals;
        }

        ChannelStopReason AimLostReason()
        {
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Spell)) return ChannelStopReason.Blocked;
            if (locomotion != null && locomotion.IsBusy) return ChannelStopReason.Busy;
            if (spell == null) return ChannelStopReason.Switched;
            if (!isActiveAndEnabled) return ChannelStopReason.Disabled;
            return ChannelStopReason.Released;
        }

        void StopChannel(ChannelStopReason reason)
        {
            if (!Channel.Cancel(reason)) return;
            ChannelCancelled?.Invoke(_channelSpell, Channel.LastStop);
        }

        void SubscribeHealth(Health next)
        {
            if (_subscribedHealth != null) _subscribedHealth.Hurt -= OnHurt;
            _subscribedHealth = next;
            if (_subscribedHealth != null) _subscribedHealth.Hurt += OnHurt;
        }

        // One rule (SpellChannel.HitInterrupts): a real hit interrupts, a chip
        // tick below the spell's channelInterruptDamage does not.
        void OnHurt(float amount, Vector3 at)
        {
            float min = spell != null ? spell.ChannelInterruptDamage : 0f;
            if (SpellChannel.HitInterrupts(amount, min)) _hurtPending = true;
        }

        // --------------------------------------------------------------- aim

        /// <summary>Resolve the crosshair once per frame, and only while aiming. A
        /// frame that was read before aiming became allowed (a dash ending in the
        /// motor's Update) still gets resolved once it is.</summary>
        void RefreshAim()
        {
            bool aiming = IsAiming;
            if (_aimFrame == Time.frameCount && (_aimResolved || !aiming)) return;
            _aimFrame = Time.frameCount;
            _aimResolved = aiming;
            if (!aiming)
            {
                _aimTarget = null;
                _groundOk = false;
                return;
            }
            ResolveAim();
            ResolveGround();
        }

        /// <summary>
        /// The camera's centre ray, nearest hit that is not ours. If that is not
        /// something damageable, a thicker sphere cast (aim assist) looks for
        /// one — but only as far as the thin ray got, so assist never locks on
        /// through the wall the crosshair is actually on.
        /// </summary>
        void ResolveAim()
        {
            if (_aimHits == null) _aimHits = new RaycastHit[AimHitCapacity];

            Vector3 origin, direction;
            if (aimCamera != null)
            {
                origin = aimCamera.transform.position;
                direction = aimCamera.transform.forward;
            }
            else
            {
                origin = CastOriginPosition;
                direction = transform.forward;
            }

            float range = Mathf.Max(spell.MaxRange, 0.01f);
            _aimPoint = origin + direction * range;
            _aimTarget = null;

            Collider nearest = null;
            float nearestDistance = range;
            int count = Physics.RaycastNonAlloc(origin, direction, _aimHits, range, aimMask,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = _aimHits[i];
                if (hit.collider == null || IsOurs(hit.collider.transform)) continue;
                if (hit.distance >= nearestDistance) continue;
                nearestDistance = hit.distance;
                nearest = hit.collider;
                _aimPoint = hit.point;
            }

            if (nearest != null && IsDamageable(nearest))
            {
                _aimTarget = nearest.transform;
                return;
            }

            float assist = spell.AimAssistRadius;
            if (assist <= 0f) return;

            float best = float.PositiveInfinity;
            count = Physics.SphereCastNonAlloc(origin, assist, direction, _aimHits, nearestDistance, aimMask,
                                               QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = _aimHits[i];
                // Distance 0 = overlapping the camera at the start: no contact
                // point, and not something you are aiming at anyway.
                if (hit.distance <= 0f || hit.collider == null || IsOurs(hit.collider.transform)) continue;
                if (hit.distance >= best || !IsDamageable(hit.collider)) continue;
                best = hit.distance;
                _aimTarget = hit.collider.transform;
                _aimPoint = hit.point;
            }
        }

        bool IsOurs(Transform t) => t.IsChildOf(Owner);

        bool IsDamageable(Collider col)
        {
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null) return false;
            if (target is Object unityObject && unityObject == null) return false;   // destroyed
            return !(target is Component component && IsOurs(component.transform));
        }

        // ------------------------------------------------------------ facing

        /// <summary>
        /// While aiming: face the crosshair, urgently (the motor strafes). The
        /// point is <see cref="AimPoint"/>, except when that is too close to the
        /// body or behind it along the view (the crosshair on something between
        /// camera and player), where the body faces along the camera instead —
        /// the same rule the orb's launch direction uses, so he never spins round
        /// to face the lens. Not aiming: declines, and lock-on facing applies.
        /// </summary>
        bool IFacingSource.TryGetFacingTarget(out Vector3 point, out bool urgent)
        {
            if (!IsAiming)
            {
                point = default;
                urgent = false;
                return false;
            }

            RefreshAim();
            Vector3 body = transform.position;
            Vector3 viewForward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            Vector3 direction = OrbMath.LaunchDirection(body, _aimPoint, viewForward, spell.MinAimDistance);
            point = body + direction * Mathf.Max(spell.MaxRange, 1f);
            urgent = true;
            return true;
        }
    }
}

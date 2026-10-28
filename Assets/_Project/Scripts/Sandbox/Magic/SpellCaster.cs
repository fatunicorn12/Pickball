using System;
using Pickleball.Sandbox.Movement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The aimed spell's gameplay, Expedition 33 style: hold aim (right mouse /
    /// LT) to raise the crosshair, press fire (left mouse / RT) to launch a
    /// slow arcane orb at whatever is under it, paying mana per shot.
    ///
    /// It only simulates. Buttons come from <see cref="SpellInput"/>, mana from
    /// <see cref="Mana"/>, and everything else is announced: the camera, lock-on
    /// and sword read <see cref="IsAiming"/> through <see cref="IAimState"/> and
    /// step aside; the HUD reads the aim and cooldown state; whatever caps
    /// movement while aiming listens to <see cref="AimStarted"/> /
    /// <see cref="AimEnded"/>. The caster itself never touches movement input.
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
        [Tooltip("The spell cast: cost, cooldown, aim and orb tuning. Without one " +
                 "the caster never aims.")]
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
                 "while aiming. Found on this object when empty.")]
        [SerializeField] private ChibiLocomotion locomotion;

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

        /// <summary>A shot was fired and paid for; the orb is already launched.</summary>
        public event Action<MagicOrb> Cast;

        /// <summary>Fire was pressed while aiming, off cooldown, but mana was short.
        /// Presses during the cooldown are not failures and raise nothing.</summary>
        public event Action CastFailed;

        RaycastHit[] _aimHits;
        Vector3 _aimPoint;
        Transform _aimTarget;
        int _aimFrame = -1;
        bool _aimResolved;
        bool _aimLatched;
        float _cooldownLeft, _cooldownTotal;
        bool _warnedNoPrefab, _warnedNoMana;

        /// <summary>
        /// True every frame aim is held and allowed: aim button down, a spell
        /// assigned, the body not owned by a busy move, and this component active.
        /// Evaluated live, so it can lead <see cref="AimStarted"/> /
        /// <see cref="AimEnded"/> (raised from Update) by up to a frame.
        /// </summary>
        public bool IsAiming => isActiveAndEnabled && spell != null && input != null && input.AimHeld
                                && (locomotion == null || !locomotion.IsBusy);

        /// <summary>The spell cast; may be null (never aims).</summary>
        public SpellDefinition Spell => spell;

        /// <summary>The mana pool paid from; may be null (every shot fails).</summary>
        public Mana Mana => mana;

        /// <summary>Mana points per shot; 0 without a spell.</summary>
        public int Cost => spell != null ? spell.ManaCost : 0;

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
        /// <see cref="AimOnTarget"/>; what the next orb homes on. Null otherwise.</summary>
        public Transform AimTarget
        {
            get
            {
                RefreshAim();
                return _aimTarget;
            }
        }

        /// <summary>True when a fire press now would launch an orb: aiming, off
        /// cooldown, an orb prefab assigned and the cost affordable.</summary>
        public bool CanCast => IsAiming && _cooldownLeft <= 0f && spell.OrbPrefab != null
                               && mana != null && mana.CanAfford(Cost);

        /// <summary>1 right after a shot, falling to 0 when the next can fire.</summary>
        public float CooldownNormalised => _cooldownTotal > 0f ? Mathf.Clamp01(_cooldownLeft / _cooldownTotal) : 0f;

        /// <summary>Seconds until the next shot can fire; 0 when ready.</summary>
        public float CooldownRemaining => _cooldownLeft;

        /// <summary>The transform orbs treat as their owner (never damaged, never
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
            if (aimCamera == null) aimCamera = Camera.main;
        }

        void OnEnable()
        {
            _aimHits = new RaycastHit[AimHitCapacity];
            _aimLatched = false;
            _aimTarget = null;
            _aimFrame = -1;
            _aimResolved = false;
            if (locomotion != null) locomotion.SetCombatFacingSource(this);
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
            if (_aimLatched)
            {
                _aimLatched = false;
                AimEnded?.Invoke();
            }
        }

        void Update()
        {
            if (_cooldownLeft > 0f) _cooldownLeft = Mathf.Max(0f, _cooldownLeft - Time.deltaTime);

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
            if (!aiming) return;

            RefreshAim();
            TryFire();
        }

        // ------------------------------------------------------------ firing

        void TryFire()
        {
            bool pressed = input.FirePressedThisFrame;
            bool wants = pressed || (spell.AutoRepeat && input.FireHeld);
            if (!wants || _cooldownLeft > 0f) return;   // cooldown-gated presses are silent

            if (spell.OrbPrefab == null)
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogWarning($"[SpellCaster] Spell '{spell.name}' has no orb prefab — nothing " +
                                     "to fire. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
                }
                return;
            }

            if (mana == null)
            {
                if (!_warnedNoMana)
                {
                    _warnedNoMana = true;
                    Debug.LogWarning($"[SpellCaster] {name} has no Mana to pay with — every shot " +
                                     "fails. Run Tools ▸ Pickleball ▸ Setup Magic.", this);
                }
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

            Launch();
        }

        void Launch()
        {
            Vector3 origin = CastOriginPosition;
            Vector3 viewForward = aimCamera != null ? aimCamera.transform.forward : transform.forward;
            Vector3 direction = OrbMath.LaunchDirection(origin, _aimPoint, viewForward, spell.MinAimDistance);

            var orb = Instantiate(spell.OrbPrefab, origin, Quaternion.LookRotation(direction));
            orb.Launch(origin, direction, spell, Owner, _aimTarget);

            _cooldownTotal = spell.Cooldown;
            _cooldownLeft = spell.Cooldown;
            Cast?.Invoke(orb);
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
                return;
            }
            ResolveAim();
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

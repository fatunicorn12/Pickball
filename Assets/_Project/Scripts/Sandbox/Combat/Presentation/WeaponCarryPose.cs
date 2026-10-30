using Pickleball.Sandbox.Inventory;
using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Plays the equipped weapon's carry pose on the Animator's masked Carry layer: how
    /// the weapon arm holds a big blade at rest (the ruler's low trail) instead of the
    /// locomotion clips' arm, which was made for the 0.85 m compass and holds the 1.05 m
    /// ruler sideways behind the head and through the hair. Pure listener: it owns
    /// nothing gameplay reads.
    ///
    /// <b>What it listens to.</b>
    ///  - <see cref="WeaponEquipper.WeaponEquipped"/>: the weapon in the hand. Its
    ///    <see cref="WeaponDefinition.CarryPose"/> names the Carry-layer state, crossfaded
    ///    to (or switched outright while the layer is hidden). A weapon with no carry pose
    ///    fades the layer out. The equipper never swaps mid-swing, so neither does this.
    ///  - <see cref="MeleeCombat"/>: <c>IsAttacking</c> and <c>IsInHitstop</c>, polled
    ///    each frame like <see cref="SwordAnimatorDriver"/> does, rather than its swing
    ///    events. A flag kept from SwingStarted / ChainEnded would be lost by a script
    ///    reload mid-swing; the polled state can't be.
    ///  - The movement state (<see cref="IMovementState.Current"/>): the moves listed in
    ///    <see cref="fadeOutDuring"/> use the arm for themselves and fade the carry out.
    ///
    /// The weight rules are <see cref="WeaponCarryBlend"/> (EditMode-tested). In short:
    /// in on equip, out on a weapon without a carry, held under every swing so the swing
    /// blends in from the carry and back to it, and out of the way during those moves.
    ///
    /// <b>Layer order</b> (built by WeaponCarrySetup): Base, AttackBody, <b>Carry</b>,
    /// Gesture, Attack. Gesture's hook throw and every swing override it; it overrides
    /// only the base layer's right arm. SwingTwist reads only the Attack layer's weight
    /// and WeaponClearance runs after the Animator, so both work on top of it unchanged.
    /// </summary>
    public class WeaponCarryPose : MonoBehaviour
    {
        [SerializeField] private WeaponEquipper equipper;
        [SerializeField] private MeleeCombat combat;
        [SerializeField] private Animator animator;

        [Tooltip("Read-only movement state (the MovementMotor). Optional: without it the " +
                 "carry never fades out for a move.")]
        [SerializeField] private MonoBehaviour movementSource;

        [Tooltip("Masked right-arm layer the carry states live on. Must match " +
                 "WeaponCarrySetup.Layer.")]
        [SerializeField] private string carryLayer = "Carry";

        [Tooltip("Moves during which the carry fades out, because the move poses the arm " +
                 "itself or the low trail would hit the floor: the pendulum hang holds the " +
                 "rope overhead, the skateboard arms balance, the dive and belly slide reach " +
                 "ahead with both hands, and a crouch drops the fist 0.3 m, which put the " +
                 "trailing blade 16 cm into the ground.")]
        [SerializeField] private MoveKind[] fadeOutDuring =
            { MoveKind.Tether, MoveKind.Skate, MoveKind.Dive, MoveKind.BellySlide, MoveKind.Crouch };

        [Tooltip("Seconds the carry takes to get out of the way when one of those moves " +
                 "starts (s). Short, so the move's own pose arrives clean; it comes back " +
                 "over the weapon's own blend time.")]
        [Range(0.02f, 0.5f)] [SerializeField] private float suppressSeconds = 0.1f;

        int _layer = -1;
        bool _layerResolved;
        // Rebuilt in OnEnable: a script reload re-runs OnEnable, never Awake.
        WeaponCarryBlend _blend;
        IMovementState _movement;
        WeaponEquipper _subscribed;
        WeaponDefinition _weapon;
        bool _stateDirty;
        int _stateHash;
        float _blendSeconds;
        float _weightSet = -1f;
        bool _warnedMissingState;

        /// <summary>Weight of the Carry layer this frame, 0-1, for debugging and playtests.</summary>
        public float Weight => _blend != null ? _blend.Weight : 0f;

        void Awake()
        {
            if (equipper == null) equipper = GetComponent<WeaponEquipper>();
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void OnEnable()
        {
            _blend = new WeaponCarryBlend();
            _movement = movementSource as IMovementState;
            _weightSet = -1f;
            Subscribe(equipper);
            // After a reload the layer still shows what it showed: carry on from there.
            int layer = Layer();
            if (layer >= 0) _blend.SyncTo(animator.GetLayerWeight(layer));
            if (equipper != null && equipper.Current != null) OnWeaponEquipped(equipper.Current);
        }

        void OnDisable()
        {
            Subscribe(null);
            // Never leave the arm in a pose nothing is managing.
            _blend?.Reset();
            int layer = _layerResolved ? _layer : -1;
            if (animator != null && layer >= 0) animator.SetLayerWeight(layer, 0f);
            _weightSet = -1f;
        }

        void Subscribe(WeaponEquipper next)
        {
            if (_subscribed != null) _subscribed.WeaponEquipped -= OnWeaponEquipped;
            _subscribed = next;
            if (_subscribed != null) _subscribed.WeaponEquipped += OnWeaponEquipped;
        }

        /// <summary>
        /// Layer index, resolved on first use rather than in Awake: the Animator is on the
        /// model child and this component on the root, so the root wakes first, before the
        /// Animator has built its graph (see SwordAnimatorDriver.Layer).
        /// </summary>
        int Layer()
        {
            if (_layerResolved) return _layer;
            if (animator == null || animator.runtimeAnimatorController == null || !animator.isInitialized)
                return -1;
            _layerResolved = true;
            _layer = animator.GetLayerIndex(carryLayer);
            if (_layer < 0)
                Debug.LogWarning($"[WeaponCarryPose] No '{carryLayer}' layer on the Animator, so weapons " +
                                 "keep the locomotion clips' arm. Run Tools ▸ Pickleball ▸ Setup Weapon Carry.", this);
            return _layer;
        }

        void OnWeaponEquipped(WeaponDefinition weapon)
        {
            _weapon = weapon;
            _stateDirty = true;
        }

        /// <summary>Takes the equipped weapon's carry: its state and its fade.</summary>
        void ApplyWeapon(int layer)
        {
            _stateDirty = false;
            bool has = _weapon != null && _weapon.HasCarryPose;
            int hash = 0;
            if (has)
            {
                // The clip's name is read once per equip, never per frame.
                hash = Animator.StringToHash(_weapon.CarryPose.name);
                if (!animator.HasState(layer, hash))
                {
                    if (!_warnedMissingState)
                        Debug.LogWarning($"[WeaponCarryPose] The '{carryLayer}' layer has no " +
                                         $"'{_weapon.CarryPose.name}' state for {_weapon.DisplayName}. " +
                                         "Run Tools ▸ Pickleball ▸ Setup Weapon Carry.", this);
                    _warnedMissingState = true;
                    has = false;
                }
            }

            _blendSeconds = _weapon != null ? _weapon.CarryBlendSeconds : 0.25f;
            _blend.SetWeapon(has, has ? _weapon.CarryWeight : 0f, _blendSeconds);
            if (!has || hash == _stateHash) return;

            if (_blend.IsHidden) animator.Play(hash, layer, 0f);
            else animator.CrossFadeInFixedTime(hash, _blendSeconds, layer, 0f);
            _stateHash = hash;
        }

        bool Suppressed()
        {
            if (_movement == null || fadeOutDuring == null) return false;
            var move = _movement.Current;
            for (int i = 0; i < fadeOutDuring.Length; i++)
                if (fadeOutDuring[i] == move) return true;
            return false;
        }

        void Update()
        {
            if (animator == null || _blend == null) return;
            int layer = Layer();
            if (layer < 0) return;
            if (_stateDirty) ApplyWeapon(layer);

            bool swinging = combat != null && (combat.IsAttacking || combat.IsInHitstop);
            float weight = _blend.Tick(Time.deltaTime, swinging, Suppressed(), suppressSeconds);
            if (!Mathf.Approximately(weight, _weightSet))
            {
                _weightSet = weight;
                animator.SetLayerWeight(layer, weight);
            }
        }

        /// <summary>Wires the component. For setup scripts and tests.</summary>
        public void Bind(WeaponEquipper weaponEquipper, MeleeCombat melee, Animator target, MonoBehaviour movement)
        {
            equipper = weaponEquipper;
            combat = melee;
            animator = target;
            movementSource = movement;
            _layerResolved = false;
            if (isActiveAndEnabled)
            {
                Subscribe(equipper);
                _movement = movementSource as IMovementState;
            }
        }
    }
}

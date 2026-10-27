using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The player's movement, as the rest of the game has always addressed it.
    ///
    /// This used to be a 1,342-line monolith doing input, every move, gravity,
    /// force fields, animation, VFX and lock-on facing at once. Movement now
    /// lives in <c>Sandbox/Movement</c> — <see cref="MovementMotor"/> (the
    /// simulation and one state class per move), <see cref="PlayerMovementInput"/>
    /// (input) and the presentation listeners — and this component is what is
    /// left: a stable facade, plus the player-specific glue the generic motor
    /// must not know about.
    ///
    /// <b>Facade.</b> <see cref="IsBusy"/>, <see cref="IsGrounded"/>,
    /// <see cref="Velocity"/>, <see cref="AddMomentum"/>,
    /// <see cref="AddAcceleration"/>, <see cref="AppliedFieldVelocity"/>,
    /// <see cref="LockMovement"/>, <see cref="IsMovementLocked"/> and
    /// <see cref="IsAnchored"/> keep their exact contracts (MeleeCombat, the
    /// Newton fight and the editor setups depend on them) and forward to the
    /// motor. New code should read <see cref="Movement"/> instead.
    ///
    /// <b>Glue.</b> Lock-on and combat decide where the body faces; movement
    /// decides when. This component implements <see cref="IFacingSource"/> from
    /// <see cref="LockOnTargeting"/> and <see cref="MeleeCombat.IsAttacking"/>, so
    /// the motor honours both without referencing either. It also points the
    /// head at the lock-on target. The MeleeCombat read is the one remaining
    /// movement-side dependency on combat; it goes once combat can push its own
    /// facing request (MeleeCombat is owned elsewhere).
    /// </summary>
    [RequireComponent(typeof(MovementMotor))]
    public class ChibiLocomotion : MonoBehaviour, IFacingSource
    {
        [Header("Lock-on")]
        [Tooltip("Found on this object if empty. While it holds a target he turns to face " +
                 "it when stopped and on every swing. Wired by Tools ▸ Pickleball ▸ Setup Lock-On.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("Turn the head toward the lock-on target while locked (ChibiFootIK does the " +
                 "look-at in the IK pass; this only tells it where).")]
        [SerializeField] private bool lookAtLockTarget = true;

        MovementMotor _motor;
        MeleeCombat _combat;
        ChibiFootIK _footIK;
        bool _warnedMissingMotor;

        /// <summary>The movement simulation this facade forwards to.</summary>
        public MovementMotor Motor
        {
            get
            {
                if (_motor == null) _motor = GetComponent<MovementMotor>();
                if (_motor == null && !_warnedMissingMotor)
                {
                    _warnedMissingMotor = true;
                    Debug.LogError($"{name}: ChibiLocomotion has no MovementMotor. Run " +
                                   "Tools ▸ Pickleball ▸ Setup Movement.", this);
                }
                return _motor;
            }
        }

        /// <summary>Read-only movement state: current move, MoveChanged, speeds.</summary>
        public IMovementState Movement => Motor;

        /// <summary>True while a move owns the controller (dash, tether, skateboard).
        /// Attacks refuse to start during these rather than fight them.</summary>
        public bool IsBusy => Motor != null && Motor.IsBusy;

        /// <summary>Feet on walkable ground this frame.</summary>
        public bool IsGrounded => Motor != null && Motor.IsGrounded;

        /// <summary>Measured world velocity — what the character actually did. The
        /// Fluxion Beam differentiates this to price its damage.</summary>
        public Vector3 Velocity => Motor != null ? Motor.Velocity : Vector3.zero;

        /// <summary>Field share of <see cref="Velocity"/>; see
        /// <see cref="MovementMotor.AppliedFieldVelocity"/> for the exact contract.</summary>
        public Vector3 AppliedFieldVelocity => Motor != null ? Motor.AppliedFieldVelocity : Vector3.zero;

        /// <summary>An IMPULSE (launcher, knockback): call once per event, never per
        /// frame. See <see cref="MovementMotor.AddMomentum"/>.</summary>
        public void AddMomentum(Vector3 delta)
        {
            if (Motor != null) Motor.AddMomentum(delta);
        }

        /// <summary>A continuous force in m/s², called EVERY frame it acts. See
        /// <see cref="MovementMotor.AddAcceleration"/>.</summary>
        public void AddAcceleration(Vector3 metresPerSecondSquared)
        {
            if (Motor != null) Motor.AddAcceleration(metresPerSecondSquared);
        }

        /// <summary>Take away his drive for a moment (a well core has him). Refreshed,
        /// not accumulated.</summary>
        public void LockMovement(float seconds)
        {
            if (Motor != null) Motor.LockMovement(seconds);
        }

        public bool IsMovementLocked => Motor != null && Motor.IsMovementLocked;

        /// <summary>True while the Gravity Anchor is holding his own gravity off.</summary>
        public bool IsAnchored => Motor != null && Motor.IsAnchored;

        void Awake()
        {
            _combat = GetComponent<MeleeCombat>();
            if (lockOn == null) lockOn = GetComponent<LockOnTargeting>();
            _footIK = GetComponentInChildren<ChibiFootIK>();
        }

        void OnEnable()
        {
            if (Motor != null) Motor.FacingSource = this;
        }

        void OnDisable()
        {
            if (_motor != null && ReferenceEquals(_motor.FacingSource, this)) _motor.FacingSource = null;
            if (_footIK != null) _footIK.LookTarget = null;
        }

        /// <summary>Points the head at the lock-on target while locked — set every
        /// frame so it can never be left pointing at a released target.</summary>
        void Update()
        {
            if (_footIK == null) return;
            bool look = lookAtLockTarget && lockOn != null && lockOn.IsLocked
                        && Motor != null && Motor.Current != MoveKind.Tether;
            _footIK.LookTarget = look ? lockOn.AimPoint : (Vector3?)null;
        }

        /// <summary>Lock-on facing: square up to the target, urgently while a swing
        /// is starting (it has to land on the target), otherwise only once stopped.</summary>
        bool IFacingSource.TryGetFacingTarget(out Vector3 point, out bool urgent)
        {
            point = default;
            urgent = false;
            if (lockOn == null || !lockOn.IsLocked) return false;
            point = lockOn.AimPoint;
            urgent = _combat != null && _combat.IsAttacking;
            return true;
        }
    }
}

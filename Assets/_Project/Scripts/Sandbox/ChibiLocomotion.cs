using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Camera-relative locomotion for the chibi: walk/jog/sprint blending,
    /// jump (Space) and crouch (Ctrl).
    ///
    /// Speeds are taken from what the Mixamo clips are actually authored at,
    /// measured off the source FBXs rather than guessed. The base set is the
    /// ARMED sword-and-shield locomotion:
    ///   armed walk 0.61 m/s; armed run 4.84 m/s (raw 1.686 on the pack's
    ///   small rig, scale-normalised to the old Running.fbx rig — see
    ///   ChibiSandboxSetup.WalkSpeed/RunSpeed for the measurements).
    /// The blend-tree thresholds use the same numbers, so the stride matches
    /// real travel and the feet stop sliding. Holding no modifier jogs; the
    /// walk clip is what he passes through while accelerating from rest.
    ///
    /// Jump physics are matched to Jumping.fbx: the clip is airborne for 0.37s
    /// with the hips rising ~0.24, so the default jump height is set to put the
    /// character in the air for about the same time.
    ///
    /// Standalone from the pickleball systems, and Input-System-only (this
    /// project has activeInputHandler = 1, so legacy Input would throw).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class ChibiLocomotion : MonoBehaviour
    {
        [Header("Speeds (m/s) — match the clips' authored speeds")]
        [Tooltip("Default move speed. Sits between the armed walk (0.61) and " +
                 "run (4.84) clips so the blend tree crossfades walk->run, which " +
                 "reads better than the Standing Sprint Forward clip did.")]
        [SerializeField] private float moveSpeed = 2.8f;
        [SerializeField] private float sprintSpeed = 4.84f;
        [Tooltip("Authored speed of the Crouched Walking clip.")]
        [SerializeField] private float crouchSpeed = 0.39f;
        [SerializeField] private float acceleration = 14f;

        [Header("Run to stop")]
        [Tooltip("Releasing the stick above this speed plays the Run To Stop clip.")]
        [SerializeField] private float stopTriggerSpeed = 3f;
        [Tooltip("Deceleration used for that stop. Run To Stop takes 0.97s to " +
                 "come to rest, so 4.75 / 5 lands almost exactly on the clip.")]
        [SerializeField] private float stopDeceleration = 5f;

        [Header("Turning")]
        [SerializeField] private float turnSmoothTime = 0.09f;

        [Header("Jump / gravity")]
        [Tooltip("Standing jump. Jumping.fbx is aloft 0.37s, which this matches.")]
        [SerializeField] private float jumpHeight = 0.35f;
        [Tooltip("Running jump. Running Jump.fbx is aloft 0.50s, needing a taller " +
                 "arc; height is lerped between the two by current speed so the " +
                 "clip and the physics agree at both ends.")]
        [SerializeField] private float runJumpHeight = 0.56f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float groundedStick = -2f;
        [Tooltip("Grace period after leaving a ledge during which jump still works.")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing still fires on landing.")]
        [SerializeField] private float jumpBuffer = 0.12f;

        [Header("Double jump (feather)")]
        [Tooltip("Higher than the ground jump so the flip has room to read. " +
                 "The clip is also time-scaled to the airtime, so this can be " +
                 "tuned freely without the animation ever over- or under-running.")]
        [SerializeField] private float doubleJumpHeight = 1.5f;
        [Tooltip("Airborne length of the trimmed Front Flip (frames 5-41).")]
        [SerializeField] private float flipAirborneSeconds = 1.20f;
        [Tooltip("Puff spawned at the feet on the air jump — the cloud-in-a-bottle beat.")]
        [SerializeField] private ParticleSystem doubleJumpEffect;

        [Header("Dash (protractor) — fixed 45°")]
        [Tooltip("Speed along the dash vector. 14 m/s over 0.22s covers 3.08m: " +
                 "2.18 across and 2.18 up. Fixed, so gaps and ledges can be " +
                 "designed against an exact reach.")]
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashDuration = 0.22f;
        [SerializeField] private float dashCooldown = 0.6f;
        [Tooltip("Fraction of the dash's upward speed kept when it ends. Low " +
                 "values give a crisp stop at the apex; high values float on.")]
        [SerializeField, Range(0f, 1f)] private float dashExitVerticalKeep = 0.35f;
        [SerializeField, Range(0f, 1f)] private float dashExitHorizontalKeep = 0.7f;
        [SerializeField] private DashEffects dashEffects;

        [Tooltip("How strongly gravity wells bend a dash, as a multiple of the " +
                 "pull they apply to walking. A dash is the dodge, and Newton's " +
                 "wells exist to disrupt dodges: dashing past a well curves the " +
                 "45° arc toward it. 0 restores the old immune dash.")]
        [SerializeField] private float dashFieldBend = 2.5f;

        [Header("Gravity Anchor (Newton's reward)")]
        [Tooltip("Seconds one anchor lasts. G halts your own gravity (hover); " +
                 "holding Jump while anchored inverts it and you rise.")]
        [SerializeField] private float anchorDuration = 2f;
        [SerializeField] private float anchorCooldown = 1.5f;
        [Tooltip("Upward acceleration while anchored with Jump held — gravity, " +
                 "inverted and gentler.")]
        [SerializeField] private float anchorRiseAcceleration = 9f;
        [SerializeField] private float anchorMaxRiseSpeed = 4.5f;
        [Tooltip("How quickly vertical speed is brought to rest when the anchor " +
                 "halts you, m/s².")]
        [SerializeField] private float anchorHaltRate = 25f;

        [Header("Carried momentum")]
        [Tooltip("Horizontal velocity carried from swings and launches, on top " +
                 "of input movement. Walking alone never sets this; it exists " +
                 "so a tether release can throw you and have it mean something.")]
        [SerializeField] private float momentumAirDrag = 1.6f;
        [SerializeField] private float momentumGroundDrag = 11f;

        [Tooltip("How quickly force-field velocity relaxes toward the speed the " +
                 "field is asking for, in 1/s. This is a DAMPING RATE, not a " +
                 "deceleration: a steady pull settles at (acceleration / this), so " +
                 "half the pull is half the drift and there is no cut-off below " +
                 "which a field does nothing. At 2.5, a strength-90 well settles " +
                 "you at 0.18 m/s at 14 m, 0.56 at 8 m, 1.44 at 5 m, 4.0 at 3 m " +
                 "and 11.1 at the core rim, against a 4.84 m/s sprint. " +
                 "PROVISIONAL — the fight's tuning pass owns the final value.")]
        [SerializeField] private float fieldDamping = 2.5f;

        [Tooltip("Backstop on force-field velocity. The damping above already gives " +
                 "the field a terminal speed of its own (11.1 m/s at one well's core " +
                 "rim), so this only bites when several wells stack or strength is " +
                 "pushed well past its shipped value — it is a guard rail, not the " +
                 "thing that shapes the pull. PROVISIONAL, with the numbers above.")]
        [SerializeField] private float maxFieldSpeed = 18f;

        [Header("Tether (orbital)")]
        [SerializeField] private float tetherMaxLength = 14f;
        [Tooltip("Floor on rope length. Short ropes orbit viciously fast — at " +
                 "1.6m a swing from horizontal already laps the anchor about " +
                 "once a second — so latching close still gives a sane arc.")]
        [SerializeField] private float tetherMinLength = 4f;
        [Tooltip("Gravity while swinging. Gentler than the jump gravity, which " +
                 "is tuned for snappy platforming and makes a brutal pendulum.")]
        [SerializeField] private float swingGravity = -11f;
        [Tooltip("Push along the tangent from movement input — this is now the " +
                 "only way to gain speed, so a swing has to be worked up.")]
        [SerializeField] private float swingPumpAccel = 7f;
        [Tooltip("Bleeds off swing energy so it settles instead of oscillating forever.")]
        [SerializeField] private float swingDamping = 0.12f;
        [Tooltip("Hard ceiling on swing speed. Without it any small energy gain " +
                 "compounds every orbit until the solver is flinging you.")]
        [SerializeField] private float maxSwingSpeed = 13f;
        [Tooltip("How much of your run-up carries into the swing on latching. " +
                 "Low, so you start gentle and build from there.")]
        [SerializeField] private float entrySpeedCap = 4.5f;
        [Tooltip("Speed kept after a solid collision mid-swing. 0 = dead stop.")]
        [SerializeField, Range(0f, 1f)] private float impactSpeedRetain = 0.25f;
        [Tooltip("Fraction of travel that must be lost before it counts as a hit " +
                 "rather than a graze along a wall.")]
        [SerializeField, Range(0f, 1f)] private float impactMinLoss = 0.25f;
        [Tooltip("Upward kick when you jump off the rope, on top of the swing's own velocity.")]
        [SerializeField] private float tetherReleaseJump = 4.2f;
        [SerializeField] private TetherSystem tether;
        [Tooltip("How long the hook-throw gesture is blended in over the upper " +
                 "body. The latch itself is instant; this is purely the tell. " +
                 "Matched to the trimmed throw clip (frames 55-80 = 0.87s) so " +
                 "the arm finishes its follow-through rather than snapping back.")]
        [SerializeField] private float gestureSeconds = 0.87f;

        [Header("Skateboard mount")]
        [Tooltip("Skating is fast; the clips are authored ~0.95 m/s but the feet " +
                 "never leave the board, so a faster ride does not read as sliding.")]
        [SerializeField] private float skateSpeed = 6.2f;
        [SerializeField] private float skateAcceleration = 5f;
        [Tooltip("Skating turns lazily — a board cannot pivot on the spot.")]
        [SerializeField] private float skateTurnSmoothTime = 0.28f;
        [Tooltip("How far the model rises when mounted — he stands ON the deck, " +
                 "not in it. Measured from the board's bounds by the setup.")]
        [SerializeField] private float skateRideHeight = 0.2f;

        [Header("Crouch")]
        [Tooltip("Capsule height while crouched, as a fraction of standing height.")]
        [SerializeField, Range(0.3f, 1f)] private float crouchHeightFraction = 0.62f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [Tooltip("The model under this controller. Offsetting it moves the " +
                 "visual without moving the capsule.")]
        [SerializeField] private Transform modelRoot;
        [Tooltip("Vertical nudge for the model against the capsule. Humanoid " +
                 "retargeting onto chibi proportions can leave the feet below " +
                 "the capsule base; raise this slightly if they still clip. " +
                 "Try Foot IK first — this is the blunt instrument.")]
        [SerializeField, Range(-0.3f, 0.7f)] private float modelYOffset;
        [Tooltip("Nudge if the feet still slide — scales clip playback against travel.")]
        [SerializeField, Range(0.5f, 2f)] private float strideScale = 1f;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;

        [Header("Lock-on")]
        [Tooltip("Found on this object if empty. While it holds a target he turns " +
                 "to face it when stopped and on every swing.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("Below this speed, with no stick input, a locked-on character " +
                 "turns to face the target. Kept above zero so the turn starts " +
                 "while Run To Stop is still settling, not after.")]
        [SerializeField] private float lockFaceSpeed = 0.6f;
        [Tooltip("Turn time when a swing starts while locked on. Much faster " +
                 "than walking turns: the swing has to land on the target.")]
        [SerializeField] private float lockAttackTurnTime = 0.04f;

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int MultiplierHash = Animator.StringToHash("SpeedMultiplier");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int CrouchHash = Animator.StringToHash("Crouch");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int VerticalHash = Animator.StringToHash("VerticalSpeed");
        static readonly int StopHash = Animator.StringToHash("Stop");
        static readonly int FlipHash = Animator.StringToHash("Flip");
        static readonly int SkatingHash = Animator.StringToHash("Skating");
        static readonly int FlipSpeedHash = Animator.StringToHash("FlipSpeed");
        static readonly int DashHash = Animator.StringToHash("Dash");
        static readonly int TetheredHash = Animator.StringToHash("Tethered");
        static readonly int FireHash = Animator.StringToHash("Fire");

        CharacterController _cc;
        MeleeCombat _combat;
        /// <summary>Travel direction while facing is held on the lock-on target,
        /// so turning to face it does not drag movement round with it.</summary>
        Vector3 _lockTravel;
        InputAction _move, _sprint, _jump, _crouch, _flip, _mount, _dash, _tether, _anchor;
        float _anchorLeft, _anchorReadyAt;
        Vector3 _dashVelocity;
        float _dashTimeLeft, _dashReadyAt;
        bool _airDashUsed;

        /// <summary>Horizontal momentum from swings/launches, decaying. Kept
        /// separate from input movement so steering still works while it bleeds
        /// off, instead of the two fighting for the same variable.</summary>
        Vector3 _momentum;
        Vector3 _swingVelocity;
        int _gestureLayer = -1;
        float _gestureTimeLeft, _gestureWeight;
        TetherAnchor _tetherAnchor;
        float _ropeLength;
        bool _tethered;
        PlayerAbilities _abilities;
        SkateboardMount _board;
        int _airJumpsUsed;
        bool _skating;
        float _rideLift;

        float _currentSpeed, _turnVelocity, _verticalVelocity;
        Vector3 _fieldVelocity, _externalAccel, _appliedFieldVelocity;
        float _moveLockLeft;
        float _lastGroundedTime, _jumpPressedTime, _jumpLatchUntil;
        bool _crouching, _stopping;
        float _standHeight, _standRadius;
        Vector3 _standCenter;

        /// <summary>True while another system owns movement — tether, skateboard
        /// or dash. Attacks refuse to start during these rather than fighting
        /// them for the CharacterController.</summary>
        public bool IsBusy => _tethered || _skating || _dashTimeLeft > 0f;

        public bool IsGrounded { get; private set; }

        /// <summary>Push for anything that needs to shove the character without
        /// owning his movement outright — a launcher, a knockback, an explosion.
        ///
        /// This is an IMPULSE: call it once per event with the velocity you want
        /// added, never every frame. Momentum only sheds momentumGroundDrag
        /// (11 m/s) per second, so a per-frame call is a net acceleration and
        /// runs away — that is exactly how the old attack lunge slid the
        /// character tens of metres per combo.</summary>
        public void AddMomentum(Vector3 delta) => _momentum += delta;

        /// <summary>
        /// Continuous force, for anything that pulls or pushes for as long as it
        /// exists — a gravity well, wind, a conveyor.
        ///
        /// This is the opposite contract to <see cref="AddMomentum"/>: call it
        /// EVERY frame the field is acting, and pass an ACCELERATION (m/s²), not
        /// a velocity. The accumulator is consumed and integrated exactly once
        /// per tick, so repeated calls in one frame sum (two wells pull as one
        /// resultant) but never compound across frames.
        ///
        /// Deliberately not routed through AddMomentum. Momentum is drained by
        /// momentumGroundDrag at 11 m/s and is meant to be topped up once per
        /// event; a per-frame caller there is a net acceleration that runs away,
        /// which is the bug that used to slide the character tens of metres per
        /// combo. Field velocity gets its own proportional damping, which gives a
        /// steady pull a terminal speed instead of letting it run away.
        ///
        /// The tether and the dash do not receive any of this — see
        /// <see cref="ClearField"/> for why, and for what happens to a pull that
        /// was already acting when one of them takes over.
        /// </summary>
        public void AddAcceleration(Vector3 metresPerSecondSquared)
            => _externalAccel += metresPerSecondSquared;

        /// <summary>Measured world velocity. The Fluxion Beam differentiates this
        /// to price its damage, so it has to be what the character actually did,
        /// not what he was asked to do.</summary>
        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;

        /// <summary>
        /// The share of <see cref="Velocity"/> that force fields actually
        /// delivered on the last tick — never the share they asked for.
        ///
        /// That distinction is the entire reason this property exists.
        /// <see cref="Velocity"/> is motion ACHIEVED, after the controller has
        /// resolved collisions. A field's banked velocity is what went INTO Move,
        /// before the solver had its say. Subtracting the request from the
        /// achievement only cancels in open space: pinned against a wall, held in
        /// a well core, or stood on a floor absorbing a vertical pull, the
        /// leftover is a phantom velocity the character never had — and one that
        /// moves every frame, so anything differentiating it sees phantom
        /// acceleration. The Fluxion Beam does exactly that to price permanent,
        /// unrecoverable damage, so a phantom there bills a motionless player for
        /// standing still.
        ///
        /// The contract, which holds on every path:
        ///
        ///     Velocity - AppliedFieldVelocity == the character's OWN velocity
        ///
        /// It is EXACTLY zero whenever he is requesting no movement of his own,
        /// however hard the field is pulling and however much of that pull the
        /// geometry is eating. It is also exactly zero — not a stale banked
        /// value — on the paths that ignore fields outright (tether, dash).
        ///
        /// Both sides of that subtraction are read from the same
        /// CharacterController.velocity sample, so the identity is exact by
        /// construction rather than by approximation. See
        /// <see cref="RecordAppliedField"/>.
        /// </summary>
        public Vector3 AppliedFieldVelocity => _appliedFieldVelocity;

        /// <summary>Take away his drive for a moment — a well core has hold of him.
        /// Steering is NOT suppressed (he still turns to face the stick; being spun
        /// around while helpless reads as a broken camera), and neither is gravity
        /// or the field drift, so he keeps falling and keeps being pulled. He
        /// simply cannot walk out under his own power.
        ///
        /// Refreshed rather than accumulated, so standing in the core keeps the
        /// lock alive without stacking into a permanent freeze once he leaves.</summary>
        public void LockMovement(float seconds)
            => _moveLockLeft = Mathf.Max(_moveLockLeft, seconds);

        public bool IsMovementLocked => _moveLockLeft > 0f;

        /// <summary>True while the Gravity Anchor is holding his own gravity off.</summary>
        public bool IsAnchored => _anchorLeft > 0f;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _abilities = GetComponent<PlayerAbilities>();
            _combat = GetComponent<MeleeCombat>();
            if (lockOn == null) lockOn = GetComponent<LockOnTargeting>();
            _board = GetComponentInChildren<SkateboardMount>(true);
            _standHeight = _cc.height;
            _standCenter = _cc.center;
            _standRadius = _cc.radius;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // Helps hold a planted foot still while the body moves over it,
                // which matters more than usual on chibi proportions where
                // retargeting error is large.
                animator.stabilizeFeet = true;
            }
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
            ApplyModelOffset();
        }

        void OnValidate()
        {
            // Live in the inspector so the offset can be dialled in while
            // looking at the feet, without entering play mode.
            ApplyModelOffset();
        }

        void ApplyModelOffset()
        {
            if (modelRoot == null) return;
            var p = modelRoot.localPosition;
            modelRoot.localPosition = new Vector3(p.x, modelYOffset + _rideLift, p.z);
        }

        /// <summary>Eases the model up onto the deck and back down, so mounting
        /// is not an instant pop.</summary>
        void UpdateRideLift(float dt)
        {
            float wanted = _skating ? skateRideHeight : 0f;
            if (Mathf.Approximately(_rideLift, wanted)) return;
            _rideLift = Mathf.MoveTowards(_rideLift, wanted, 1.2f * dt);
            ApplyModelOffset();
        }

        void OnEnable()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftTrigger");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _crouch = new InputAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/rightCtrl");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            _flip = new InputAction("Flip", InputActionType.Button);
            _flip.AddBinding("<Keyboard>/f");
            _flip.AddBinding("<Gamepad>/buttonNorth");

            _mount = new InputAction("Mount", InputActionType.Button);
            _mount.AddBinding("<Keyboard>/r");
            _mount.AddBinding("<Gamepad>/buttonWest");

            _dash = new InputAction("Dash", InputActionType.Button);
            _dash.AddBinding("<Keyboard>/e");
            _dash.AddBinding("<Gamepad>/rightShoulder");

            _tether = new InputAction("Tether", InputActionType.Button);
            _tether.AddBinding("<Keyboard>/q");
            _tether.AddBinding("<Mouse>/rightButton");
            _tether.AddBinding("<Gamepad>/leftShoulder");

            _anchor = new InputAction("GravityAnchor", InputActionType.Button);
            _anchor.AddBinding("<Keyboard>/g");
            _anchor.AddBinding("<Gamepad>/rightStickPress");

            _move.Enable(); _sprint.Enable(); _jump.Enable(); _crouch.Enable();
            _flip.Enable(); _mount.Enable(); _dash.Enable(); _tether.Enable();
            _anchor.Enable();
        }

        void OnDisable()
        {
            foreach (var a in new[] { _move, _sprint, _jump, _crouch, _flip, _mount, _dash, _tether, _anchor })
            {
                a?.Disable();
                a?.Dispose();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool grounded = CheckGrounded();
            IsGrounded = grounded;
            if (grounded) _lastGroundedTime = Time.time;

            // ---- force fields ------------------------------------------------
            // Take the frame's accumulated acceleration and clear the accumulator,
            // exactly once, whichever system ends up owning movement below.
            // Callers re-add every frame they are still acting, so a well that
            // stops existing stops contributing immediately.
            //
            // Draining here rather than down in the planar block is deliberate:
            // the tether and dash paths return early, and letting the accumulator
            // pile up while they run so it all lands in one frame on exit is the
            // same runaway this whole channel exists to avoid. What happens to
            // the reading depends on who owns movement — the planar path
            // integrates it (IntegrateField), the tether and dash discard it
            // along with anything already banked (ClearField).
            Vector3 fieldAccel = _externalAccel;
            _externalAccel = Vector3.zero;

            // Ticked here for the same reason: a lock applied just before a dash
            // would otherwise never expire.
            if (_moveLockLeft > 0f) _moveLockLeft -= dt;
            HandleAnchor(dt);

            HandleMount(grounded);
            UpdateRideLift(dt);
            UpdateGesture(dt);
            if (!_skating) HandleCrouch(grounded);

            // A dash overrides everything: fixed velocity, no gravity, no input
            // steering. Bailing out here keeps the arc exactly 45° and exactly
            // as long as advertised, which is what makes it designable.
            // The tether owns movement entirely while latched.
            HandleTetherInput(grounded);
            if (_tethered) { ClearField(); UpdateSwing(dt); return; }

            if (grounded) _airDashUsed = false;
            if (TryStartDash(grounded)) { ClearField(); return; }
            if (_dashTimeLeft > 0f)
            {
                ClearField();
                // Wells bend the dash rather than being ignored by it. Applied to
                // the dash velocity itself, not banked as field velocity, so it
                // curves the arc while it lasts and leaves nothing to release
                // when the dash hands movement back.
                _dashVelocity += fieldAccel * (dashFieldBend * dt);
                _dashTimeLeft -= dt;
                _cc.Move(_dashVelocity * dt);
                if (_dashTimeLeft <= 0f) EndDash();
                if (animator != null)
                {
                    animator.SetFloat(SpeedHash, _currentSpeed);
                    animator.SetBool(GroundedHash, false);
                }
                return;
            }

            // ---- planar movement -------------------------------------------
            Vector2 input = _move.ReadValue<Vector2>();
            if (input.sqrMagnitude > 1f) input.Normalize();
            // Attacking deliberately does NOT gate this. The combo plays on a
            // masked upper-body layer, so a swing never touches steering or the
            // legs — see MeleeCombat.
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            }

            Vector3 desired = forward * input.y + right * input.x;
            float magnitude = Mathf.Clamp01(desired.magnitude);
            if (magnitude > 0.001f) desired /= magnitude;

            float top = _skating ? skateSpeed
                      : _crouching ? crouchSpeed
                      : _sprint.IsPressed() ? sprintSpeed
                      : moveSpeed;
            float target = magnitude * top;

            // A well core has him: he keeps falling and keeps drifting with the
            // field, but cannot walk out under his own power. Steering is left
            // alone on purpose — being spun around while helpless is nauseating
            // and reads as a broken camera rather than a mechanic.
            bool locked = _moveLockLeft > 0f;
            if (locked) target = 0f;

            // Releasing the stick at speed hands the deceleration to the
            // Run To Stop clip, at a rate slow enough for the plant to read.
            //
            // The lock SUPPRESSES this test rather than clearing _stopping.
            // Clearing it was a trigger cannon: GravityWell re-applies the lock
            // every frame you are in its core, so _stopping was reset immediately
            // before a test that requires !_stopping, and entering a core at
            // sprint speed fired SetTrigger(Stop) on the order of twenty times
            // over a third of a second. Unity latches an unconsumed trigger
            // indefinitely, so the spares sat armed and one would later yank him
            // into Run To Stop mid-run with no input at all.
            //
            // Gating instead means a locked character simply is not a candidate
            // for the stop: he is not choosing to stop, he is being held.
            if (!locked && target < 0.01f && !_crouching && !_skating &&
                _currentSpeed > stopTriggerSpeed && !_stopping)
            {
                _stopping = true;
                if (animator != null) animator.SetTrigger(StopHash);
            }
            else if (_stopping && (target > 0.01f || _currentSpeed < 0.05f))
            {
                _stopping = false;
            }

            float accel = _skating ? skateAcceleration
                        : _stopping ? stopDeceleration
                        : acceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, target, accel * dt);

            // Locked on, he squares up to the target when he stops and whenever
            // he swings — but runs freely the rest of the time. Movement is
            // normally along transform.forward, so while facing is held on the
            // target, travel is carried separately in _lockTravel instead.
            bool attacking = _combat != null && _combat.IsAttacking;
            bool faceTarget = lockOn != null && lockOn.IsLocked && !_skating &&
                              (attacking || (target < 0.01f && _currentSpeed < lockFaceSpeed));
            Vector3 travel = transform.forward;
            if (faceTarget)
            {
                if (magnitude > 0.001f) _lockTravel = desired;
                travel = _lockTravel;

                Vector3 to = lockOn.AimPoint - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float smooth = attacking ? lockAttackTurnTime : turnSmoothTime;
                    float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted,
                                                        ref _turnVelocity, smooth);
                    transform.rotation = Quaternion.Euler(0f, angle, 0f);
                }
            }
            else if (magnitude > 0.001f)
            {
                float wanted = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float smooth = _skating ? skateTurnSmoothTime : turnSmoothTime;
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, wanted,
                                                    ref _turnVelocity, smooth);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
            }
            if (!faceTarget) _lockTravel = transform.forward;

            // ---- jump / gravity --------------------------------------------
            // Front flip on the ground is a trick; in the air it is the double
            // jump's animation and is triggered from the jump path below.
            if (_flip.WasPressedThisFrame() && grounded && !_crouching && !_skating
                && animator != null)
                animator.SetTrigger(FlipHash);

            if (_jump.WasPressedThisFrame()) _jumpPressedTime = Time.time;

            if (grounded) _airJumpsUsed = 0;

            bool canJump = Time.time - _lastGroundedTime <= coyoteTime
                           && !_crouching && !_skating;
            bool wantsJump = Time.time - _jumpPressedTime <= jumpBuffer;

            // Second press while airborne: the feather's double jump. Requires
            // the ability, and only once per airtime.
            bool canAirJump = !canJump && !_skating && _airJumpsUsed < 1
                              && _abilities != null
                              && _abilities.Has(AbilityKind.DoubleJump);

            if (!canJump && canAirJump && _jump.WasPressedThisFrame())
            {
                _airJumpsUsed++;
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * doubleJumpHeight);
                _jumpPressedTime = -99f;
                _jumpLatchUntil = Time.time + 0.15f;
                if (animator != null)
                {
                    // Fit the flip to the hang time exactly: airtime is the rise
                    // plus the fall, i.e. 2v/g. Without this the clip either
                    // finishes early and he falls in a T-pose-ish idle, or is
                    // still spinning when he lands.
                    float airtime = 2f * _verticalVelocity / Mathf.Abs(gravity);
                    float scale = flipAirborneSeconds / Mathf.Max(airtime, 0.05f);
                    animator.SetFloat(FlipSpeedHash, Mathf.Clamp(scale, 0.5f, 2.5f));
                    animator.SetTrigger(FlipHash);
                }
                if (doubleJumpEffect != null)
                {
                    doubleJumpEffect.transform.position = transform.position;
                    doubleJumpEffect.Play();
                }
            }
            else if (canJump && wantsJump)
            {
                // v = sqrt(2gh) puts the apex exactly at the chosen height.
                float h = Mathf.Lerp(jumpHeight, runJumpHeight,
                                     Mathf.Clamp01(_currentSpeed / Mathf.Max(sprintSpeed, 0.01f)));
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * h);
                _stopping = false;
                _jumpPressedTime = -99f;
                _lastGroundedTime = -99f;
                grounded = false;
                // CharacterController.isGrounded still reports true for a frame
                // or two after launch. Without this latch the Jump -> Land
                // transition fires immediately and clips the jump.
                _jumpLatchUntil = Time.time + 0.15f;
                if (animator != null) animator.SetTrigger(JumpHash);
            }
            else if (_anchorLeft > 0f && (!grounded || _jump.IsPressed()))
            {
                // Gravity Anchor: his own gravity is off. Jump held inverts it,
                // which also lifts him straight off the floor.
                if (grounded) _verticalVelocity = Mathf.Max(_verticalVelocity, 0f);
                _verticalVelocity = _jump.IsPressed()
                    ? Mathf.Min(_verticalVelocity + anchorRiseAcceleration * dt, anchorMaxRiseSpeed)
                    : Mathf.MoveTowards(_verticalVelocity, 0f, anchorHaltRate * dt);
            }
            else if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = groundedStick;
            }
            else
            {
                _verticalVelocity += gravity * dt;
            }

            IntegrateField(fieldAccel, dt);

            // Input movement plus whatever momentum is still being carried.
            // Momentum's own y is dropped on purpose — the vertical channel is
            // _verticalVelocity's alone, so a launch has to go through that.
            Vector3 own = travel * _currentSpeed + _momentum;
            own.y = _verticalVelocity;

            // Field velocity rides on top, EXCEPT vertically while he is stood on
            // something, where the floor gets the whole vertical component and it
            // is left out of the request entirely.
            //
            // This line used to add it unconditionally, under a comment claiming a
            // well strong enough to beat gravity would lift him off the floor. It
            // could not, and the comment was worth less than nothing: it told the
            // next reader a case was handled. _verticalVelocity is an unbounded
            // gravity integrator while field velocity is damped to a terminal
            // speed, so gravity outgrows any field within a few frames and always
            // wins. What a mid-strength upward field actually produced was a
            // bounce cycle — grounded snaps to groundedStick, the field beats it,
            // he leaves the floor, gravity re-grabs, repeat — flickering the
            // grounded animator state several times a second. A downward field
            // fared no better: the floor ate it silently, and the difference
            // between the eaten request and the achieved motion then surfaced as
            // phantom acceleration in the Fluxion Beam's reading.
            //
            // Making a well genuinely able to pluck him off the ground is a
            // movement-design change, not a patch: gravity would need a terminal
            // velocity of its own, or the two would have to share one integrator
            // (which would cost the beam its clean field/self split). Not smuggled
            // in here. Nothing places a well overhead today — the boss drops them
            // at his own y on flat ground — so what this excludes is a case that
            // does not yet occur, stated rather than half-implemented.
            Vector3 field = _fieldVelocity;
            if (grounded) field.y = 0f;

            Vector3 motion = own + field;
            _cc.Move(motion * dt);
            RecordAppliedField(motion, field, grounded);

            float drag = grounded ? momentumGroundDrag : momentumAirDrag;
            _momentum = Vector3.MoveTowards(_momentum, Vector3.zero, drag * dt);

            // ---- animator ---------------------------------------------------
            if (animator == null) return;
            animator.SetFloat(SpeedHash, _currentSpeed, 0.08f, dt);
            animator.SetFloat(MultiplierHash, strideScale);
            animator.SetBool(GroundedHash, grounded && Time.time >= _jumpLatchUntil);
            animator.SetBool(CrouchHash, _crouching);
            animator.SetBool(SkatingHash, _skating);
            animator.SetFloat(VerticalHash, _verticalVelocity);
        }

        // ------------------------------------------------------- force fields

        /// <summary>
        /// One tick of the force-field channel: relax the banked field velocity
        /// toward the terminal speed this frame's acceleration implies.
        ///
        /// <b>The damping has to be proportional.</b> It used to be
        /// <c>MoveTowards(v, 0, fieldDrag · dt)</c>, which subtracts a flat
        /// 2.5 m/s per second and clamps at zero — so the net was (a − 2.5) and
        /// ANY acceleration below 2.5 m/s² produced exactly nothing. A well's pull
        /// is strength/r², so with strength 55 that threshold sat at
        /// r = √(55/2.5) = 4.69 m, while the boss is forbidden from dropping a
        /// well closer than 5 m to the player. Every well in the fight spawned
        /// outside its own effective radius, and the design's "readable gradient
        /// with navigable corridors" was in practice an invisible hard edge with
        /// nothing whatsoever outside it. A proportional law has no threshold at
        /// any distance: half the pull is half the drift, out to the rim.
        ///
        /// Solved rather than stepped. dv/dt = a − k·v has the closed form
        /// v(t) = a/k + (v₀ − a/k)·e^(−k·t), and using it directly makes the
        /// settled speed exactly a/k at ANY frame rate. Stepping it the obvious
        /// way instead drifts about 4% between 30 and 144 fps, which is small but
        /// is exactly the class of frame-rate dependence this pass is removing.
        /// </summary>
        void IntegrateField(Vector3 acceleration, float dt)
        {
            if (fieldDamping > 0f)
            {
                // a/k — where a steady pull of this strength eventually settles.
                Vector3 terminal = acceleration / fieldDamping;
                _fieldVelocity = terminal
                               + (_fieldVelocity - terminal) * Mathf.Exp(-fieldDamping * dt);
            }
            else
            {
                // Undamped. An escape hatch for someone tuning by hand, not a
                // supported setting: with no damping there is no terminal speed
                // and maxFieldSpeed becomes the only thing bounding the pull.
                _fieldVelocity += acceleration * dt;
            }

            _fieldVelocity = Vector3.ClampMagnitude(_fieldVelocity, maxFieldSpeed);
        }

        /// <summary>
        /// Cut the character loose from every force field, banked velocity and all.
        ///
        /// <b>The tether ignores wells; the dash is bent by them instead.</b>
        /// Neither reads banked field velocity — the swing runs its own gravity
        /// against a rope constraint, and the dash applies the frame's pull
        /// straight to its own velocity (see <see cref="dashFieldBend"/>) — so this
        /// keeps the banked channel empty while either owns movement.
        ///
        /// Zeroing is the point rather than an implementation detail. Field
        /// velocity used to keep accumulating while those systems owned movement,
        /// unread by any of them, and then switch on at full magnitude the instant
        /// movement was handed back: up to the entire maxFieldSpeed cap arriving in
        /// one frame from a well the player had already swung clear of, then
        /// coasting for seconds. Draining it to zero makes the rule one sentence —
        /// while the rope or the dash has you the field has nothing, and it builds
        /// again from zero when you land — with no exit impulse possible.
        /// </summary>
        void ClearField()
        {
            _fieldVelocity = Vector3.zero;
            _appliedFieldVelocity = Vector3.zero;
        }

        /// <summary>
        /// Split what the controller ACTUALLY moved into the field's share and the
        /// character's own, and bank the field's for
        /// <see cref="AppliedFieldVelocity"/>.
        ///
        /// Move can deliver less than it was asked for — a wall, a step, a floor
        /// under a downward pull — and CharacterController will not say which part
        /// of the request it refused. So the shortfall is attributed in proportion
        /// to how much each side asked for. In the general case that is a
        /// heuristic; in the case the Fluxion Beam depends on it, it is exact. When
        /// the character requests no movement of his own, the field's share of the
        /// shortfall is all of it, the field is credited with precisely the motion
        /// that occurred, and <c>Velocity − AppliedFieldVelocity</c> is exactly
        /// zero. Being dragged into a wall, or held motionless in a well core,
        /// therefore costs nothing — which is what the beam promises in writing.
        ///
        /// <c>achieved</c> is read from the same <c>_cc.velocity</c> that
        /// <see cref="Velocity"/> exposes, so that identity holds by construction
        /// and does not depend on the two agreeing about what a frame's motion was.
        /// </summary>
        void RecordAppliedField(Vector3 requested, Vector3 field, bool grounded)
        {
            Vector3 achieved = _cc.velocity;

            // groundedStick is a hold-down, not a movement request: it exists to
            // weld the capsule to the floor, and the floor absorbs every bit of it
            // every frame. It has to come out of BOTH sides of the attribution —
            // the share AND the shortfall — because taking it out of only one
            // leaves the field carrying a constant phantom of groundedStick in y
            // for as long as the player stands in a field.
            Vector3 own = requested - field;
            if (grounded && own.y < 0f) own.y = 0f;

            float fieldAsked = field.magnitude;
            float ownAsked = own.magnitude;
            float asked = fieldAsked + ownAsked;

            // Nothing was genuinely asked for, so nothing can be owed to the
            // field. Also keeps the share out of a divide by zero.
            if (asked < 1e-4f)
            {
                _appliedFieldVelocity = Vector3.zero;
                return;
            }

            Vector3 shortfall = own + field - achieved;
            _appliedFieldVelocity = field - shortfall * (fieldAsked / asked);
        }

        /// <summary>
        /// Index of the masked gesture layer, resolved on first use.
        ///
        /// This CANNOT be read in Awake. The Animator sits on the model child
        /// while this component sits on the root, so the root wakes first and
        /// the Animator has not built its playable graph yet — GetLayerIndex
        /// then logs "Animator is not playing an AnimatorController" and returns
        /// -1, which silently disabled the gesture layer for good. Checking the
        /// controller first means no error is logged while we wait for it.
        /// </summary>
        int GestureLayer()
        {
            if (_gestureLayer >= 0) return _gestureLayer;
            if (animator == null || animator.runtimeAnimatorController == null) return -1;
            _gestureLayer = animator.GetLayerIndex("Gesture");
            return _gestureLayer;
        }

        /// <summary>Fades the masked gesture layer in and back out. Held at zero
        /// weight the rest of the time, so its empty state cannot overwrite the
        /// arms with the bind pose.</summary>
        void UpdateGesture(float dt)
        {
            int layer = GestureLayer();
            if (layer < 0) return;
            if (_gestureTimeLeft > 0f) _gestureTimeLeft -= dt;
            float wanted = _gestureTimeLeft > 0f ? 1f : 0f;
            if (Mathf.Approximately(_gestureWeight, wanted)) return;
            _gestureWeight = Mathf.MoveTowards(_gestureWeight, wanted, dt * 6f);
            animator.SetLayerWeight(layer, _gestureWeight);
        }

        // ------------------------------------------------------------ tether

        void HandleTetherInput(bool grounded)
        {
            if (_abilities == null || !_abilities.Has(AbilityKind.Tether)) return;

            if (_tether.WasPressedThisFrame())
            {
                if (_tethered) ReleaseTether(false);
                else if (tether != null && tether.Best != null) Latch(tether.Best);
            }
            // Jumping off the rope keeps the swing's momentum and adds a kick —
            // that trade (let go at the right moment) is the whole skill of it.
            else if (_tethered && _jump.WasPressedThisFrame())
            {
                ReleaseTether(true);
            }
        }

        void Latch(TetherAnchor anchor)
        {
            _tetherAnchor = anchor;
            _tethered = true;
            _ropeLength = Mathf.Clamp(
                Vector3.Distance(transform.position, anchor.Position),
                tetherMinLength, tetherMaxLength);

            // Enter gently. Only the TANGENTIAL part of your current motion is
            // carried in — velocity straight along the rope means nothing to a
            // pendulum, and feeding it in is what made latching launch you into
            // an orbit. Capped as well, so the swing is something you build up
            // rather than arrive at.
            Vector3 entry = transform.forward * _currentSpeed + _momentum;
            entry.y = _verticalVelocity;

            Vector3 radial = (transform.position - anchor.Position).normalized;
            entry = Vector3.ProjectOnPlane(entry, radial);
            _swingVelocity = Vector3.ClampMagnitude(entry, entrySpeedCap);

            _currentSpeed = 0f;
            _momentum = Vector3.zero;
            _stopping = false;
            // Field velocity is not cleared here, and does not need to be: the
            // tether branch in Update calls ClearField every frame it owns
            // movement, starting with this one. Said out loud because the two
            // lines above zero their channels here and the asymmetry otherwise
            // reads as an oversight — which is precisely what it used to be.
            if (tether != null) tether.Attach(anchor);
            if (animator != null)
            {
                animator.SetBool(TetheredHash, true);
                animator.SetTrigger(FireHash);
            }
            _gestureTimeLeft = gestureSeconds;
        }

        void ReleaseTether(bool withJump)
        {
            _tethered = false;
            _tetherAnchor = null;
            if (tether != null) tether.Detach();
            if (animator != null) animator.SetBool(TetheredHash, false);

            // Hand the swing's velocity back: horizontal becomes momentum,
            // vertical becomes the jump arc. Releasing at the bottom of a swing
            // should fling you, and this is what makes that true.
            _verticalVelocity = _swingVelocity.y + (withJump ? tetherReleaseJump : 0f);
            _momentum = new Vector3(_swingVelocity.x, 0f, _swingVelocity.z);
            _currentSpeed = 0f;
            _swingVelocity = Vector3.zero;

            // Face the way you are travelling, and level out — the rig was
            // rotated to hang along the rope.
            var flat = new Vector3(_momentum.x, 0f, _momentum.z);
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            else
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        /// <summary>
        /// Pendulum step: integrate freely, then project back onto the sphere of
        /// radius ropeLength and strip the radial velocity. Only constrains when
        /// the rope is taut (dist > length), so it behaves as a rope rather than
        /// a rigid rod — you can fall inward freely.
        /// </summary>
        void UpdateSwing(float dt)
        {
            if (_tetherAnchor == null) { ReleaseTether(false); return; }

            Vector3 anchor = _tetherAnchor.Position;
            Vector3 pos = transform.position;
            Vector2 input = _move.ReadValue<Vector2>();

            // Rope length is fixed at whatever it was when you latched. Reeling
            // it in shrank the radius while tangential speed stayed put, which
            // sent angular velocity through the roof — and since W is the
            // natural "forward" key, it happened without meaning to.
            _swingVelocity += Vector3.up * swingGravity * dt;

            // Movement input pushes along the tangent plane, so you accelerate
            // in whichever direction you lean. Camera-relative, matching every
            // other movement in the game.
            Vector3 radialDir = (pos - anchor).normalized;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 camF = Vector3.forward, camR = Vector3.right;
                if (cameraTransform != null)
                {
                    camF = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                    camR = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                }
                Vector3 push = camF * input.y + camR * input.x;
                push = Vector3.ProjectOnPlane(push, radialDir);   // tangent only
                if (push.sqrMagnitude > 0.0001f)
                    _swingVelocity += push.normalized * (swingPumpAccel * dt);
            }

            _swingVelocity *= 1f - Mathf.Clamp01(swingDamping * dt);
            _swingVelocity = Vector3.ClampMagnitude(_swingVelocity, maxSwingSpeed);

            Vector3 next = pos + _swingVelocity * dt;
            Vector3 offset = next - anchor;
            float dist = offset.magnitude;
            if (dist > _ropeLength && dist > 0.0001f)
            {
                Vector3 radial = offset / dist;
                next = anchor + radial * _ropeLength;
                float outward = Vector3.Dot(_swingVelocity, radial);
                if (outward > 0f) _swingVelocity -= radial * outward;
            }

            float intended = _swingVelocity.magnitude;
            CollisionFlags flags = _cc.Move(next - pos);

            // Re-derive from what actually happened, so hitting a wall kills the
            // swing instead of the solver insisting on velocity it never got.
            // Re-clamped: a collision resolve can spike this for one frame, and
            // an unclamped spike compounds into a runaway orbit.
            if (dt > 0f)
                _swingVelocity = Vector3.ClampMagnitude((transform.position - pos) / dt,
                                                        maxSwingSpeed);

            // Re-deriving alone is not enough: sliding along a wall keeps almost
            // all of the tangential speed, so a scrape reads as no impact at all.
            // Scale the penalty by how much travel was actually lost, so a graze
            // costs little and a solid hit costs most of the swing.
            if (flags != CollisionFlags.None && intended > 0.5f)
            {
                float lost = Mathf.Clamp01(1f - _swingVelocity.magnitude / intended);
                if (lost > impactMinLoss)
                {
                    float keep = Mathf.Lerp(1f, impactSpeedRetain, lost);
                    _swingVelocity *= keep;
                }
            }

            // Hang along the rope, facing the way you are going.
            Vector3 up = (anchor - transform.position).normalized;
            Vector3 travel = Vector3.ProjectOnPlane(_swingVelocity, up);
            if (travel.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.LookRotation(travel.normalized, up);

            if (animator != null)
            {
                animator.SetFloat(SpeedHash, _swingVelocity.magnitude);
                animator.SetBool(GroundedHash, false);
            }
        }

        /// <summary>
        /// Launch a dash if one is available. The vector is built as
        /// (horizontal facing, 1) normalised — with the horizontal part unit
        /// length, that is exactly 45° every time, independent of where the
        /// camera happens to be pitched.
        /// </summary>
        bool TryStartDash(bool grounded)
        {
            if (_dashTimeLeft > 0f) return false;
            if (!_dash.WasPressedThisFrame()) return false;
            if (_skating || _crouching) return false;
            if (Time.time < _dashReadyAt) return false;
            if (_abilities == null || !_abilities.Has(AbilityKind.Dash)) return false;
            if (!grounded && _airDashUsed) return false;   // one per airtime

            if (!grounded) _airDashUsed = true;

            Vector3 flat = transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 dir = (flat + Vector3.up).normalized;   // exactly 45°
            _dashVelocity = dir * dashSpeed;
            _dashTimeLeft = dashDuration;
            _dashReadyAt = Time.time + dashDuration + dashCooldown;
            _verticalVelocity = 0f;
            _stopping = false;

            if (animator != null)
            {
                animator.SetTrigger(DashHash);
                animator.SetBool(GroundedHash, false);
            }
            if (dashEffects != null) dashEffects.Play(dir, dashDuration);
            return true;
        }

        void EndDash()
        {
            _dashTimeLeft = 0f;
            // Hand momentum back so the dash flows into the fall rather than
            // stopping dead in the air.
            _verticalVelocity = _dashVelocity.y * dashExitVerticalKeep;
            var flat = new Vector3(_dashVelocity.x, 0f, _dashVelocity.z);
            _currentSpeed = Mathf.Min(flat.magnitude * dashExitHorizontalKeep, sprintSpeed);
            _dashVelocity = Vector3.zero;
        }

        /// <summary>G toggles the Gravity Anchor once Newton's pickup has been
        /// collected. Ticked at the top of Update so it expires even while a dash
        /// or the tether owns movement.</summary>
        void HandleAnchor(float dt)
        {
            if (_anchorLeft > 0f)
            {
                _anchorLeft -= dt;
                if (_anchorLeft <= 0f) EndAnchor();
            }

            if (_abilities == null || !_abilities.Has(AbilityKind.GravityAnchor)) return;
            if (!_anchor.WasPressedThisFrame()) return;

            if (_anchorLeft > 0f) { EndAnchor(); return; }
            if (Time.time < _anchorReadyAt || _tethered || _skating) return;

            _anchorLeft = anchorDuration;
            // Halting starts from where he is: a fall in progress is caught.
            if (_verticalVelocity < 0f) _verticalVelocity *= 0.25f;
        }

        void EndAnchor()
        {
            _anchorLeft = 0f;
            _anchorReadyAt = Time.time + anchorCooldown;
        }

        /// <summary>R mounts/dismounts the board once the skateboard pickup has
        /// been collected. Mounting is refused mid-air so he cannot conjure a
        /// board under himself during a jump.</summary>
        void HandleMount(bool grounded)
        {
            if (_abilities == null || !_abilities.Has(AbilityKind.Skateboard)) return;
            if (!_mount.WasPressedThisFrame()) return;
            if (!_skating && !grounded) return;

            _skating = !_skating;
            if (_skating)
            {
                // Standing up out of a crouch first, so the capsule is correct.
                if (_crouching && CanStand())
                {
                    _crouching = false;
                    _cc.height = _standHeight;
                    _cc.center = new Vector3(_standCenter.x, _standHeight * 0.5f, _standCenter.z);
                }
                _stopping = false;
            }
            if (_board != null) _board.SetRidden(_skating);
        }

        void HandleCrouch(bool grounded)
        {
            bool want = _crouch.IsPressed() && grounded;
            if (want == _crouching) return;

            // Refuse to stand up under a ceiling, or the capsule pops through it.
            if (!want && !CanStand()) return;

            _crouching = want;
            float h = want ? _standHeight * crouchHeightFraction : _standHeight;
            _cc.height = h;
            _cc.center = new Vector3(_standCenter.x, h * 0.5f, _standCenter.z);
        }

        /// <summary>
        /// Ground test by spherecast rather than CharacterController.isGrounded.
        ///
        /// isGrounded is only refreshed by Move() and is well known to report
        /// true spuriously while still airborne — which surfaces as the landing
        /// animation playing in midair, because the Fall -> Land transition
        /// keys off it. A spherecast against real geometry is stable and also
        /// lets us reject steep faces that the character should slide down.
        /// </summary>
        bool CheckGrounded()
        {
            float radius = Mathf.Max(_cc.radius * 0.9f, 0.05f);
            Vector3 origin = transform.position + Vector3.up * (_cc.radius + 0.05f);
            float distance = _cc.radius + 0.05f + GroundProbe;

            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, distance,
                                             ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f) continue;   // started inside a collider
                if (Vector3.Angle(h.normal, Vector3.up) > _cc.slopeLimit) continue;
                return true;
            }
            return false;
        }

        const float GroundProbe = 0.12f;

        bool CanStand()
        {
            Vector3 bottom = transform.position + Vector3.up * (_standRadius + 0.02f);
            Vector3 top = transform.position + Vector3.up * (_standHeight - _standRadius);
            // OverlapCapsule rather than CheckCapsule: the character's own
            // controller sits inside this volume and would otherwise report a
            // hit every frame, trapping him in a crouch forever.
            var hits = Physics.OverlapCapsule(bottom, top, _standRadius * 0.95f,
                                              ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (h.transform != transform && !h.transform.IsChildOf(transform))
                    return false;
            return true;
        }
    }
}
